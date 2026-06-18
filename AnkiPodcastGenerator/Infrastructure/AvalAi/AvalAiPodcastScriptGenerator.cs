using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

public sealed class AvalAiPodcastScriptGenerator : IPodcastScriptGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly AvalAiOptions _options;
    private readonly ILogger<AvalAiPodcastScriptGenerator> _logger;

    public AvalAiPodcastScriptGenerator(
        HttpClient httpClient,
        IOptions<AvalAiOptions> options,
        ILogger<AvalAiPodcastScriptGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<ScriptGenerationResult> GenerateScriptAsync(
        IReadOnlyList<AnkiCard> cards,
        PodcastDeck deck,
        int targetMinutes,
        CancellationToken cancellationToken)
    {
        EnsureApiKeyConfigured();

        var request = new
        {
            model = _options.ScriptModel,
            temperature = 0.2,
            max_tokens = CalculateMaxCompletionTokens(targetMinutes),
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt()
                },
                new
                {
                    role = "user",
                    content = BuildUserPrompt(cards, deck, targetMinutes)
                }
            }
        };

        var failures = new List<string>();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            try
            {
                _logger.LogInformation(
                    "Generating podcast script with AvalAI model {Model}. Attempt={Attempt}",
                    _options.ScriptModel,
                    attempt);

                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    var failure = $"HTTP {statusCode}. Body: {responseBody}";
                    failures.Add(failure);

                    if (statusCode >= 500 && attempt < 2)
                    {
                        _logger.LogWarning("AvalAI script generation failed transiently: {Failure}", failure);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"AvalAI script generation failed. {string.Join(Environment.NewLine, failures)}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions)
                    ?? throw new InvalidOperationException("AvalAI script generation returned an empty response.");

                var script = completion.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(script))
                {
                    throw new InvalidOperationException("AvalAI script generation returned no message content.");
                }

                return new ScriptGenerationResult(
                    script.Trim(),
                    _options.ScriptModel,
                    completion.Usage?.ToTokenUsage());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                var failure = $"timeout on attempt {attempt}. Retrying once.";
                failures.Add(failure);
                _logger.LogWarning("AvalAI script generation {Failure}", failure);
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                var failure = $"transport error on attempt {attempt}: {ex.Message}. Retrying once.";
                failures.Add(failure);
                _logger.LogWarning("AvalAI script generation {Failure}", failure);
            }
        }

        throw new InvalidOperationException(
            $"AvalAI script generation failed. {string.Join(Environment.NewLine, failures)}");
    }

    private static string BuildSystemPrompt() =>
        """
        You generate educational podcast scripts from Anki cards.
        Output only the script, using this exact speaker marker format:
        [A]
        text...

        [B]
        text...

        Host A is a senior engineer. Host B is an interviewer.
        The tone is friendly, practical, educational, and optimized for recall.
        Keep a calm, slower teaching pace with short sentences.
        Keep the final script close to the requested target duration.
        Use conversational turns, but do not over-dialogue. Every turn should teach or check recall.
        Do not hallucinate facts that are not in the cards.
        Cover every card.
        Do not skip commands, warnings, caveats, examples, paths, flags, or error messages.
        Preserve commands, code, paths, variable names, flags, and syntax exactly when they appear in the cards.
        Spend more time on complex cards and less time on simple cards.
        """;

    private static string BuildUserPrompt(IReadOnlyList<AnkiCard> cards, PodcastDeck deck, int targetMinutes)
    {
        var compactCards = cards.Select((card, index) => new
        {
            number = index + 1,
            cardId = card.CardId,
            deck = card.DeckName,
            front = card.Front,
            back = card.Back,
            tags = card.Tags
        });

        var cardsJson = JsonSerializer.Serialize(compactCards, JsonOptions);

        return $$"""
        Create a two-host podcast script for Anki deck "{{deck.DeckName}}".
        Target duration: about {{targetMinutes}} minutes.

        Requirements:
        - Use [A] and [B] speaker markers exactly.
        - Host A explains as a senior engineer.
        - Host B asks practical interviewer questions and checks understanding.
        - Make it sound like a real conversation, not an article being read aloud.
        - Use a slower, focused pace with short sentences.
        - Keep the length close to the target duration.
        - Avoid filler, greetings, recaps that add no recall value, or excessive back-and-forth.
        - Avoid rushing through dense material. Pause between ideas by starting a new speaker block.
        - Make the script useful for active recall.
        - Cover all cards in the provided JSON.
        - Do not skip commands, warnings, caveats, examples, paths, flags, or error messages.
        - Quote exact commands, code, paths, variable names, flags, and syntax before explaining them.
        - If a card is simple, cover it briefly.
        - If a card is complex, slow down and explain it carefully.
        - Do not invent missing details. Say the card does not specify if needed.

        Cards JSON:
        {{cardsJson}}
        """;
    }

    private static int CalculateMaxCompletionTokens(int targetMinutes)
    {
        var normalizedTarget = Math.Max(1, targetMinutes);
        return Math.Clamp(normalizedTarget * 320, 440, 12_000);
    }

    private void EnsureApiKeyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "AvalAI API key is missing. Set AvalAi:ApiKey in appsettings.json, AvalAi__ApiKey, or AVALAI_API_KEY.");
        }
    }

    public sealed class ChatCompletionResponse
    {
        public List<ChatChoice> Choices { get; set; } = [];
        public UsageDto? Usage { get; set; }
    }

    public sealed class ChatChoice
    {
        public ChatMessage Message { get; set; } = new();
    }

    public sealed class ChatMessage
    {
        public string Content { get; set; } = string.Empty;
    }

    public sealed class UsageDto
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }

        public TokenUsage ToTokenUsage() => new(PromptTokens, CompletionTokens, TotalTokens);
    }
}
