using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

public sealed class AvalAiGeminiMultiSpeakerTextToSpeechProvider : IMultiSpeakerTextToSpeechProvider
{
    private const int MaxTranscriptBytesPerRequest = 3500;
    private const int MaxAttemptsPerChunk = 3;
    private const int DefaultSampleRate = 24000;
    private const int DefaultChannels = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AvalAiOptions _options;
    private readonly IPcmAudioEncoder _pcmAudioEncoder;
    private readonly IAudioCombiner _audioCombiner;
    private readonly ILogger<AvalAiGeminiMultiSpeakerTextToSpeechProvider> _logger;
    private string? _lastSuccessfulModel;

    public AvalAiGeminiMultiSpeakerTextToSpeechProvider(
        HttpClient httpClient,
        IOptions<AvalAiOptions> options,
        IPcmAudioEncoder pcmAudioEncoder,
        IAudioCombiner audioCombiner,
        ILogger<AvalAiGeminiMultiSpeakerTextToSpeechProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _pcmAudioEncoder = pcmAudioEncoder;
        _audioCombiner = audioCombiner;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<TextToSpeechResult> GenerateMultiSpeakerMp3Async(
        IReadOnlyList<PodcastSegment> segments,
        string voiceA,
        string voiceB,
        string outputPath,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(voiceA, voiceB);

        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Cannot generate multi-speaker audio with zero script segments.");
        }

        var chunks = ChunkSegments(segments, MaxTranscriptBytesPerRequest);
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "AnkiPodcastGenerator",
            $"native-gemini-tts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var chunkFiles = new List<string>(chunks.Count);
            var modelsUsed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            ApiCostEstimate? totalCost = null;

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunkFile = Path.Combine(tempDirectory, $"{i + 1:000}-native.mp3");
                if (TryGetPauseChunkSeconds(chunks[i], out var pauseSeconds))
                {
                    await _audioCombiner.CreateSilenceMp3Async(pauseSeconds, chunkFile, cancellationToken);
                    chunkFiles.Add(chunkFile);
                    continue;
                }

                var transcript = RenderTranscript(chunks[i]);
                var result = await GenerateNativeChunkAsync(
                    transcript,
                    voiceA,
                    voiceB,
                    chunkFile,
                    tempDirectory,
                    i + 1,
                    cancellationToken);

                modelsUsed.Add(result.Model);
                totalCost = totalCost?.Add(result.EstimatedCost) ?? result.EstimatedCost;
                chunkFiles.Add(chunkFile);
            }

            Directory.CreateDirectory(outputDirectory);
            if (chunkFiles.Count == 1)
            {
                File.Copy(chunkFiles[0], outputPath, overwrite: true);
            }
            else
            {
                await _audioCombiner.CombineMp3Async(chunkFiles, outputPath, cancellationToken);
            }

            var fileInfo = new FileInfo(outputPath);
            var modelSummary = modelsUsed.Count == 0 ? _options.TtsModel : string.Join("+", modelsUsed);

            if (totalCost is not null)
            {
                _logger.LogInformation(
                    "AvalAI TTS cost: {CostSummary}",
                    GenerationCostFormatter.Format(totalCost, "TTS"));
            }

            return new TextToSpeechResult(modelSummary, $"{voiceA}/{voiceB}", outputPath, fileInfo.Length, totalCost);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                TryDeleteDirectory(tempDirectory);
            }
        }
    }

    private async Task<TextToSpeechResult> GenerateNativeChunkAsync(
        string transcript,
        string voiceA,
        string voiceB,
        string outputPath,
        string tempDirectory,
        int chunkNumber,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        foreach (var model in GetTtsModelsToTry())
        {
            for (var attempt = 1; attempt <= MaxAttemptsPerChunk; attempt++)
            {
                using var request = CreateRequest(model, transcript, voiceA, voiceB);

                try
                {
                    _logger.LogInformation(
                        "Generating native Gemini multi-speaker audio via AvalAI. Model={Model}, VoiceA={VoiceA}, VoiceB={VoiceB}, Chunk={Chunk}, Attempt={Attempt}",
                        model,
                        voiceA,
                        voiceB,
                        chunkNumber,
                        attempt);

                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    var statusCode = (int)response.StatusCode;
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        AvalAiHttpErrors.ThrowIfQuotaExhausted("multi-speaker text-to-speech", statusCode, responseBody);

                        var failure = $"model '{model}' failed with HTTP {statusCode}.";
                        failures.Add(failure);
                        _logger.LogWarning("AvalAI native Gemini TTS {Failure}", failure);

                        if (statusCode >= 500 && attempt < MaxAttemptsPerChunk)
                        {
                            await DelayBeforeRetryAsync(attempt, cancellationToken);
                            continue;
                        }

                        break;
                    }

                    var audio = ExtractInlineAudio(responseBody, model);
                    await WriteAudioAsMp3Async(audio, outputPath, tempDirectory, chunkNumber, cancellationToken);

                    var bytesWritten = new FileInfo(outputPath).Length;
                    var chunkCost = AvalAiCostParser.TryParseEstimatedCost(responseBody)
                        ?? AvalAiPricingEstimator.EstimateTtsCostFromAudioSeconds(
                            model,
                            AvalAiPricingEstimator.EstimateMp3DurationSeconds(bytesWritten));

                    _logger.LogInformation(
                        "Saved native Gemini multi-speaker MP3 chunk. Model={Model}, VoiceA={VoiceA}, VoiceB={VoiceB}, MimeType={MimeType}, OutputPath={OutputPath}, Bytes={Bytes}",
                        model,
                        voiceA,
                        voiceB,
                        audio.MimeType,
                        outputPath,
                        bytesWritten);

                    _lastSuccessfulModel = model;
                    return new TextToSpeechResult(model, $"{voiceA}/{voiceB}", outputPath, bytesWritten, chunkCost);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var failure = attempt < MaxAttemptsPerChunk
                        ? $"model '{model}' timed out on chunk {chunkNumber}, attempt {attempt}. Retrying."
                        : $"model '{model}' timed out on chunk {chunkNumber}, attempt {attempt}.";
                    failures.Add(failure);
                    _logger.LogWarning("AvalAI native Gemini TTS {Failure}", failure);

                    if (attempt == MaxAttemptsPerChunk)
                    {
                        break;
                    }

                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    var failure = attempt < MaxAttemptsPerChunk
                        ? $"model '{model}' transport error on chunk {chunkNumber}, attempt {attempt}: {ex.Message}. Retrying."
                        : $"model '{model}' transport error on chunk {chunkNumber}, attempt {attempt}: {ex.Message}.";
                    failures.Add(failure);
                    _logger.LogWarning("AvalAI native Gemini TTS {Failure}", failure);

                    if (attempt == MaxAttemptsPerChunk)
                    {
                        break;
                    }

                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                }
                catch (JsonException ex)
                {
                    failures.Add($"model '{model}' returned an unparseable native Gemini response: {ex.Message}");
                    break;
                }
            }
        }

        if (AvalAiHttpErrors.TryCreateFromFailureDetails("multi-speaker text-to-speech", failures) is { } quotaFailure)
        {
            throw quotaFailure;
        }

        throw new InvalidOperationException("AvalAI native Gemini TTS failed. " + string.Join(Environment.NewLine, failures));
    }

    private HttpRequestMessage CreateRequest(string model, string transcript, string voiceA, string voiceB)
    {
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = BuildNativePrompt(transcript, _options.TtsSpeed)
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    multiSpeakerVoiceConfig = new
                    {
                        speakerVoiceConfigs = new object[]
                        {
                            new
                            {
                                speaker = "Host A",
                                voiceConfig = new
                                {
                                    prebuiltVoiceConfig = new
                                    {
                                        voiceName = voiceA
                                    }
                                }
                            },
                            new
                            {
                                speaker = "Host B",
                                voiceConfig = new
                                {
                                    prebuiltVoiceConfig = new
                                    {
                                        voiceName = voiceB
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{model}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);
        return request;
    }

    private async Task WriteAudioAsMp3Async(
        NativeInlineAudio audio,
        string outputPath,
        string tempDirectory,
        int chunkNumber,
        CancellationToken cancellationToken)
    {
        if (audio.MimeType.StartsWith("audio/mpeg", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllBytesAsync(outputPath, audio.Bytes, cancellationToken);
            return;
        }

        if (audio.MimeType.StartsWith("audio/L16", StringComparison.OrdinalIgnoreCase))
        {
            var sampleRate = ParseSampleRate(audio.MimeType) ?? DefaultSampleRate;
            var pcmPath = Path.Combine(tempDirectory, $"{chunkNumber:000}-native.pcm");
            await File.WriteAllBytesAsync(pcmPath, audio.Bytes, cancellationToken);
            await _pcmAudioEncoder.EncodePcmToMp3Async(pcmPath, sampleRate, DefaultChannels, outputPath, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unsupported native Gemini TTS audio MIME type: {audio.MimeType}");
    }

    private IEnumerable<string> GetTtsModelsToTry()
    {
        if (!string.IsNullOrWhiteSpace(_lastSuccessfulModel))
        {
            yield return _lastSuccessfulModel;
        }

        if (!string.IsNullOrWhiteSpace(_options.TtsModel) &&
            !string.Equals(_lastSuccessfulModel, _options.TtsModel, StringComparison.OrdinalIgnoreCase))
        {
            yield return _options.TtsModel;
        }

        if (!string.IsNullOrWhiteSpace(_options.TtsFallbackModel) &&
            !string.Equals(_lastSuccessfulModel, _options.TtsFallbackModel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_options.TtsModel, _options.TtsFallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            yield return _options.TtsFallbackModel;
        }
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(10 * attempt), cancellationToken);

    private static NativeInlineAudio ExtractInlineAudio(string responseBody, string model)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Native Gemini response for model '{model}' did not include candidates.");
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!TryGetInlineData(part, out var inlineData))
                {
                    continue;
                }

                var mimeType = inlineData.TryGetProperty("mimeType", out var mimeProperty)
                    ? mimeProperty.GetString()
                    : inlineData.TryGetProperty("mime_type", out var snakeMimeProperty)
                        ? snakeMimeProperty.GetString()
                        : null;
                var base64Data = inlineData.TryGetProperty("data", out var dataProperty)
                    ? dataProperty.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(mimeType) && !string.IsNullOrWhiteSpace(base64Data))
                {
                    return new NativeInlineAudio(mimeType, Convert.FromBase64String(base64Data));
                }
            }
        }

        throw new JsonException($"Native Gemini response for model '{model}' did not include inline audio data.");
    }

    private static bool TryGetInlineData(JsonElement part, out JsonElement inlineData)
    {
        if (part.TryGetProperty("inlineData", out inlineData))
        {
            return true;
        }

        if (part.TryGetProperty("inline_data", out inlineData))
        {
            return true;
        }

        inlineData = default;
        return false;
    }

    private static int? ParseSampleRate(string mimeType)
    {
        foreach (var part in mimeType.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith("rate=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(part["rate=".Length..], out var sampleRate) && sampleRate > 0)
            {
                return sampleRate;
            }
        }

        return null;
    }

    private static string BuildNativePrompt(string transcript, double speed) =>
        "TTS the following transcript as a calm, focused educational podcast conversation. " +
        "Use the speaker labels only to choose the correct speaker voice; do not read the labels aloud. " +
        GetPaceInstruction(speed) +
        "\n\n" +
        transcript;

    private static string GetPaceInstruction(double speed) =>
        speed switch
        {
            < 0.95 => "Use a slightly slower pace with clear pauses between ideas.",
            > 1.05 => "Use a slightly faster pace while keeping pronunciation clear.",
            _ => "Use a normal pace with clear pauses between ideas."
        };

    private static string RenderTranscript(IReadOnlyList<PodcastSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.PauseAfterSeconds > 0)
            {
                continue;
            }

            var speaker = segment.Speaker == 'B' ? "Host B" : "Host A";
            builder.Append(speaker);
            builder.Append(": ");
            builder.AppendLine(segment.Text.Trim());
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<IReadOnlyList<PodcastSegment>> ChunkSegments(
        IReadOnlyList<PodcastSegment> segments,
        int maxTranscriptBytes)
    {
        var chunks = new List<IReadOnlyList<PodcastSegment>>();
        var current = new List<PodcastSegment>();
        var currentBytes = 0;

        foreach (var segment in ExpandOversizedSegments(segments, maxTranscriptBytes))
        {
            if (segment.PauseAfterSeconds > 0)
            {
                if (current.Count > 0)
                {
                    chunks.Add(current.ToArray());
                    current = [];
                    currentBytes = 0;
                }

                chunks.Add(new[] { segment });
                continue;
            }

            var segmentBytes = GetTranscriptBytes(segment);
            if (current.Count > 0 && currentBytes + segmentBytes > maxTranscriptBytes)
            {
                chunks.Add(current.ToArray());
                current = [];
                currentBytes = 0;
            }

            current.Add(segment);
            currentBytes += segmentBytes;
        }

        if (current.Count > 0)
        {
            chunks.Add(current.ToArray());
        }

        return chunks;
    }

    private static IEnumerable<PodcastSegment> ExpandOversizedSegments(
        IReadOnlyList<PodcastSegment> segments,
        int maxTranscriptBytes)
    {
        foreach (var segment in segments)
        {
            if (segment.PauseAfterSeconds > 0)
            {
                yield return segment;
                continue;
            }

            if (GetTranscriptBytes(segment) <= maxTranscriptBytes)
            {
                yield return segment;
                continue;
            }

            foreach (var text in SplitTextByBytes(segment.Text, maxTranscriptBytes - 32))
            {
                yield return new PodcastSegment(segment.Speaker, text);
            }
        }
    }

    private static IEnumerable<string> SplitTextByBytes(string text, int maxBytes)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var sentences = normalized.Split(['\n', '.', '?', '!'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (Encoding.UTF8.GetByteCount(sentence) > maxBytes)
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString().Trim();
                    builder.Clear();
                }

                foreach (var hardChunk in SplitHardByBytes(sentence, maxBytes))
                {
                    yield return hardChunk;
                }

                continue;
            }

            var separator = builder.Length == 0 ? string.Empty : ". ";
            if (Encoding.UTF8.GetByteCount(builder + separator + sentence) > maxBytes && builder.Length > 0)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
                separator = string.Empty;
            }

            builder.Append(separator);
            builder.Append(sentence);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().Trim();
        }
    }

    private static IEnumerable<string> SplitHardByBytes(string text, int maxBytes)
    {
        var builder = new StringBuilder();

        foreach (var rune in text.EnumerateRunes())
        {
            var next = rune.ToString();
            if (builder.Length > 0 && Encoding.UTF8.GetByteCount(builder.ToString() + next) > maxBytes)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
            }

            builder.Append(next);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().Trim();
        }
    }

    private static int GetTranscriptBytes(PodcastSegment segment)
    {
        if (segment.PauseAfterSeconds > 0)
        {
            return 0;
        }

        var speakerBytes = segment.Speaker == 'B'
            ? Encoding.UTF8.GetByteCount("Host B: ")
            : Encoding.UTF8.GetByteCount("Host A: ");
        return speakerBytes + Encoding.UTF8.GetByteCount(segment.Text) + Environment.NewLine.Length;
    }

    private static bool TryGetPauseChunkSeconds(IReadOnlyList<PodcastSegment> chunk, out int seconds)
    {
        if (chunk.Count == 1 && chunk[0].PauseAfterSeconds > 0)
        {
            seconds = chunk[0].PauseAfterSeconds;
            return true;
        }

        seconds = 0;
        return false;
    }

    private void EnsureConfigured(string voiceA, string voiceB)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "AvalAI API key is missing. Set AvalAi:ApiKey in appsettings.json, AvalAi__ApiKey, or AVALAI_API_KEY.");
        }

        if (string.IsNullOrWhiteSpace(_options.TtsModel))
        {
            throw new InvalidOperationException("AvalAI TTS model is missing.");
        }

        if (string.IsNullOrWhiteSpace(voiceA) || string.IsNullOrWhiteSpace(voiceB))
        {
            throw new InvalidOperationException("Both VoiceA and VoiceB must be configured for multi-speaker TTS.");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete temporary native Gemini TTS directory {TempDirectory}", path);
        }
    }

    private sealed record NativeInlineAudio(string MimeType, byte[] Bytes);
}
