using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class LlmTtsScriptNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly AvalAiOptions _options;
    private readonly TtsNormalizerOptions _normalizerOptions;
    private readonly ILogger<LlmTtsScriptNormalizer> _logger;

    public LlmTtsScriptNormalizer(
        HttpClient httpClient,
        IOptions<AvalAiOptions> options,
        IOptions<TtsNormalizerOptions> normalizerOptions,
        ILogger<LlmTtsScriptNormalizer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _normalizerOptions = normalizerOptions.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<TtsNormalizationResult> NormalizeWithLlmAsync(
        string displayScript,
        IReadOnlyList<PronunciationMapItem> staticMap,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("LLM TTS normalization skipped: no AvalAI API key configured");
            return new TtsNormalizationResult(displayScript, displayScript, staticMap);
        }

        var model = string.IsNullOrWhiteSpace(_normalizerOptions.LlmModel)
            ? _options.ScriptModel
            : _normalizerOptions.LlmModel.Trim();
        var prompt = BuildNormalizationPrompt(displayScript);

        var request = new
        {
            model,
            temperature = 0.1,
            max_tokens = 8192,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a text normalization assistant for a text-to-speech engine. You MUST return valid JSON only."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            _logger.LogInformation("Calling LLM for TTS script normalization using model {Model}", model);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var completion = JsonSerializer.Deserialize<NormalizationResponse>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("LLM normalization returned an empty response.");

            var content = completion.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("LLM normalization returned no message content.");
            }

            // Extract JSON from the response (handle potential extra text)
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                content = content[jsonStart..(jsonEnd + 1)];
            }

            var result = JsonSerializer.Deserialize<NormalizationResultJson>(content, JsonOptions);
            if (result is null || string.IsNullOrWhiteSpace(result.TtsScript))
            {
                throw new InvalidOperationException("LLM normalization returned invalid JSON.");
            }

            // Validate the result
            if (result.TtsScript.Length < displayScript.Length * 0.5)
            {
                _logger.LogWarning(
                    "LLM normalization produced a script that is {Ratio:P0} of the original length. Falling back to static-only.",
                    (double)result.TtsScript.Length / displayScript.Length);
                return new TtsNormalizationResult(displayScript, displayScript, staticMap);
            }

            var combinedMap = staticMap.ToList();
            if (result.PronunciationMap is not null)
            {
                foreach (var item in result.PronunciationMap)
                {
                    if (!combinedMap.Any(m =>
                            string.Equals(m.Original, item.Original, StringComparison.OrdinalIgnoreCase)))
                    {
                        combinedMap.Add(new PronunciationMapItem(
                            item.Original ?? string.Empty,
                            item.Replacement ?? string.Empty,
                            item.Reason ?? "LLM normalization"));
                    }
                }
            }

            _logger.LogInformation(
                "LLM normalization complete. Static replacements: {StaticCount}, LLM replacements: {LlmCount}, combined: {TotalCount}",
                staticMap.Count,
                result.PronunciationMap?.Count ?? 0,
                combinedMap.Count);

            return new TtsNormalizationResult(displayScript, result.TtsScript, combinedMap);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "LLM TTS normalization HTTP request failed. Falling back to static-only.");
            return new TtsNormalizationResult(displayScript, displayScript, staticMap);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LLM TTS normalization timed out. Falling back to static-only.");
            return new TtsNormalizationResult(displayScript, displayScript, staticMap);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LLM TTS normalization JSON parsing failed. Falling back to static-only.");
            return new TtsNormalizationResult(displayScript, displayScript, staticMap);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "LLM TTS normalization returned an invalid response. Falling back to static-only.");
            return new TtsNormalizationResult(displayScript, displayScript, staticMap);
        }
    }

    private static string BuildNormalizationPrompt(string script)
    {
        return $$"""
        You are preparing text for a text-to-speech engine.
        Rewrite only the words that may be mispronounced by TTS.
        Do not change meaning.
        Do not summarize.
        Do not add or remove educational content.
        Return strict JSON with:
        {
          "ttsScript": "...",
          "pronunciationMap": [
            {
              "original": "...",
              "replacement": "...",
              "reason": "..."
            }
          ]
        }
        Input script:
        <<<{{script}}>>>
        """;
    }

    private sealed class NormalizationResponse
    {
        public List<NormalizationChoice>? Choices { get; set; }
    }

    private sealed class NormalizationChoice
    {
        public NormalizationMessage? Message { get; set; }
    }

    private sealed class NormalizationMessage
    {
        public string? Content { get; set; }
    }

    private sealed class NormalizationResultJson
    {
        [JsonPropertyName("ttsScript")]
        public string? TtsScript { get; set; }

        [JsonPropertyName("pronunciationMap")]
        public List<PronunciationMapItemJson>? PronunciationMap { get; set; }
    }

    private sealed class PronunciationMapItemJson
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("replacement")]
        public string? Replacement { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
