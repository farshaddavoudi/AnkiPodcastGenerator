using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

public sealed class AvalAiTextToSpeechProvider : ITextToSpeechProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AvalAiOptions _options;
    private readonly ILogger<AvalAiTextToSpeechProvider> _logger;
    private string? _lastSuccessfulModel;

    public AvalAiTextToSpeechProvider(
        HttpClient httpClient,
        IOptions<AvalAiOptions> options,
        ILogger<AvalAiTextToSpeechProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<TextToSpeechResult> GenerateMp3Async(
        string text,
        string voice,
        string outputPath,
        CancellationToken cancellationToken)
    {
        EnsureApiKeyConfigured();
        EnsureSpeedConfigured();

        var failures = new List<string>();

        foreach (var model in GetTtsModelsToTry())
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var body = new
                {
                    model,
                    voice,
                    input = text,
                    response_format = "mp3",
                    speed = _options.TtsSpeed
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech")
                {
                    Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

                try
                {
                    _logger.LogInformation(
                        "Generating MP3 via AvalAI TTS. Model={Model}, Voice={Voice}, Attempt={Attempt}",
                        model,
                        voice,
                        attempt);

                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    var statusCode = (int)response.StatusCode;

                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var failure = $"model '{model}' failed with HTTP {statusCode}. Body: {responseBody}";
                        failures.Add(failure);
                        _logger.LogWarning("AvalAI TTS {Failure}", failure);

                        if (statusCode >= 500 && attempt < 2)
                        {
                            continue;
                        }

                        break;
                    }

                    var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (audioBytes.Length == 0)
                    {
                        failures.Add($"model '{model}' returned HTTP {statusCode} with an empty audio body.");
                        continue;
                    }

                    if (!LooksLikeMp3(audioBytes))
                    {
                        var preview = Encoding.UTF8.GetString(audioBytes.Take(Math.Min(audioBytes.Length, 1000)).ToArray());
                        failures.Add($"model '{model}' returned HTTP {statusCode}, but the body does not look like MP3. Body preview: {preview}");
                        break;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    await File.WriteAllBytesAsync(outputPath, audioBytes, cancellationToken);

                    _logger.LogInformation(
                        "Saved MP3. Model={Model}, Voice={Voice}, Speed={Speed}, OutputPath={OutputPath}, Bytes={Bytes}",
                        model,
                        voice,
                        _options.TtsSpeed,
                        outputPath,
                        audioBytes.Length);

                    _lastSuccessfulModel = model;
                    return new TextToSpeechResult(model, voice, outputPath, audioBytes.Length);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var failure = attempt < 2
                        ? $"model '{model}' voice '{voice}' timed out on attempt {attempt}. Retrying once."
                        : $"model '{model}' voice '{voice}' timed out on attempt {attempt}.";
                    failures.Add(failure);
                    _logger.LogWarning("AvalAI TTS {Failure}", failure);

                    if (attempt == 2)
                    {
                        break;
                    }
                }
                catch (HttpRequestException ex)
                {
                    var failure = attempt < 2
                        ? $"model '{model}' voice '{voice}' transport error on attempt {attempt}: {ex.Message}. Retrying once."
                        : $"model '{model}' voice '{voice}' transport error on attempt {attempt}: {ex.Message}.";
                    failures.Add(failure);
                    _logger.LogWarning("AvalAI TTS {Failure}", failure);

                    if (attempt == 2)
                    {
                        break;
                    }
                }
            }
        }

        throw new InvalidOperationException("AvalAI TTS failed. " + string.Join(Environment.NewLine, failures));
    }

    private IEnumerable<string> GetTtsModelsToTry()
    {
        if (!string.IsNullOrWhiteSpace(_lastSuccessfulModel))
        {
            yield return _lastSuccessfulModel;
        }

        if (!string.IsNullOrWhiteSpace(_options.TtsModel))
        {
            if (!string.Equals(_lastSuccessfulModel, _options.TtsModel, StringComparison.OrdinalIgnoreCase))
            {
                yield return _options.TtsModel;
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.TtsFallbackModel) &&
            !string.Equals(_lastSuccessfulModel, _options.TtsFallbackModel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_options.TtsModel, _options.TtsFallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            yield return _options.TtsFallbackModel;
        }
    }

    private static bool LooksLikeMp3(byte[] bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33) ||
        (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0);

    private void EnsureApiKeyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "AvalAI API key is missing. Set AvalAi:ApiKey in appsettings.json, AvalAi__ApiKey, or AVALAI_API_KEY.");
        }
    }

    private void EnsureSpeedConfigured()
    {
        if (_options.TtsSpeed is < 0.25 or > 4.0)
        {
            throw new InvalidOperationException("AvalAI TTS speed must be between 0.25 and 4.0.");
        }
    }
}
