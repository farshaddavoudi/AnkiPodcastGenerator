using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
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
    private readonly PodcastOptions _podcastOptions;
    private readonly ILogger<AvalAiPodcastScriptGenerator> _logger;

    public AvalAiPodcastScriptGenerator(
        HttpClient httpClient,
        IOptions<AvalAiOptions> options,
        IOptions<PodcastOptions> podcastOptions,
        ILogger<AvalAiPodcastScriptGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _podcastOptions = podcastOptions.Value;
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
                    AvalAiHttpErrors.ThrowIfQuotaExhausted("script generation", statusCode, responseBody);

                    failures.Add($"HTTP {statusCode}.");

                    if (statusCode >= 500 && attempt < 2)
                    {
                        _logger.LogWarning(
                            "AvalAI script generation failed transiently: HTTP {StatusCode}",
                            statusCode);
                        continue;
                    }

                    throw AvalAiHttpErrors.CreateHttpFailureException(
                        "AvalAI script generation",
                        "script generation",
                        statusCode,
                        responseBody,
                        failures);
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions)
                    ?? throw new InvalidOperationException("AvalAI script generation returned an empty response.");

                var script = completion.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrWhiteSpace(script))
                {
                    throw new InvalidOperationException("AvalAI script generation returned no message content.");
                }

                var tokenUsage = completion.Usage?.ToTokenUsage();
                var estimatedCost = AvalAiCostParser.TryParseEstimatedCost(responseBody)
                    ?? AvalAiPricingEstimator.EstimateScriptCost(_options.ScriptModel, tokenUsage);

                if (estimatedCost is not null)
                {
                    _logger.LogInformation(
                        "AvalAI script cost: {CostSummary}",
                        GenerationCostFormatter.Format(estimatedCost, "Script"));
                }

                return new ScriptGenerationResult(
                    script.Trim(),
                    _options.ScriptModel,
                    tokenUsage,
                    estimatedCost);
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
