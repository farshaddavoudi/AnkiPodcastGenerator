using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using AnkiPodcastGenerator.Infrastructure.AvalAi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.OpenRouter;

public sealed class OpenRouterPodcastScriptGenerator : IPodcastScriptGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _openRouterOptions;
    private readonly AvalAiOptions _scriptOptions;
    private readonly PodcastOptions _podcastOptions;
    private readonly ILogger<OpenRouterPodcastScriptGenerator> _logger;

    public OpenRouterPodcastScriptGenerator(
        HttpClient httpClient,
        IOptions<OpenRouterOptions> openRouterOptions,
        IOptions<AvalAiOptions> scriptOptions,
        IOptions<PodcastOptions> podcastOptions,
        ILogger<OpenRouterPodcastScriptGenerator> logger)
    {
        _httpClient = httpClient;
        _openRouterOptions = openRouterOptions.Value;
        _scriptOptions = scriptOptions.Value;
        _podcastOptions = podcastOptions.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_openRouterOptions.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<ScriptGenerationResult> GenerateScriptAsync(
        IReadOnlyList<AnkiCard> cards,
        PodcastDeck deck,
        int targetMinutes,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var request = new
        {
            model = _scriptOptions.ScriptModel,
            temperature = 0.2,
            max_tokens = PodcastScriptPromptBuilder.CalculateMaxCompletionTokens(targetMinutes),
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = PodcastScriptPromptBuilder.BuildSystemPrompt()
                },
                new
                {
                    role = "user",
                    content = PodcastScriptPromptBuilder.BuildUserPrompt(cards, deck, targetMinutes, _podcastOptions.CustomPrompt)
                }
            }
        };

        var failures = new List<string>();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openRouterOptions.ApiKey);
            AddOptionalHeader(httpRequest, "HTTP-Referer", _openRouterOptions.Referer);
            AddOptionalHeader(httpRequest, "X-Title", _openRouterOptions.Title);

            try
            {
                _logger.LogInformation(
                    "Generating podcast script with OpenRouter model {Model}. Attempt={Attempt}",
                    _scriptOptions.ScriptModel,
                    attempt);

                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    var failure = $"HTTP {statusCode}. Body: {responseBody}";
                    failures.Add(failure);

                    if ((statusCode >= 500 || statusCode == 429) && attempt < 2)
                    {
                        _logger.LogWarning("OpenRouter script generation failed transiently: {Failure}", failure);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"OpenRouter script generation failed. {string.Join(Environment.NewLine, failures)}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions)
                    ?? throw new InvalidOperationException("OpenRouter script generation returned an empty response.");

                var script = completion.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(script))
                {
                    throw new InvalidOperationException("OpenRouter script generation returned no message content.");
                }

                var tokenUsage = completion.Usage?.ToTokenUsage();
                var estimatedCost = AvalAiCostParser.TryParseOpenRouterUsageCost(responseBody);
                if (estimatedCost is not null)
                {
                    _logger.LogInformation(
                        "OpenRouter script cost: {CostSummary}",
                        GenerationCostFormatter.Format(estimatedCost, "Script"));
                }

                return new ScriptGenerationResult(
                    script.Trim(),
                    completion.Model ?? _scriptOptions.ScriptModel,
                    tokenUsage,
                    estimatedCost);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                var failure = $"timeout on attempt {attempt}. Retrying once.";
                failures.Add(failure);
                _logger.LogWarning("OpenRouter script generation {Failure}", failure);
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                var failure = $"transport error on attempt {attempt}: {ex.Message}. Retrying once.";
                failures.Add(failure);
                _logger.LogWarning("OpenRouter script generation {Failure}", failure);
            }
        }

        throw new InvalidOperationException(
            $"OpenRouter script generation failed. {string.Join(Environment.NewLine, failures)}");
    }

    private static void AddOptionalHeader(HttpRequestMessage request, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_openRouterOptions.ApiKey))
        {
            throw new InvalidOperationException(
                "OpenRouter API key is missing. Set OpenRouter:ApiKey, OpenRouter__ApiKey, or OPENROUTER_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(_scriptOptions.ScriptModel))
        {
            throw new InvalidOperationException("Script model is missing.");
        }
    }

    private sealed class ChatCompletionResponse
    {
        public string? Model { get; set; }
        public List<ChatChoice> Choices { get; set; } = [];
        public UsageDto? Usage { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        public string Content { get; set; } = string.Empty;
    }

    private sealed class UsageDto
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
