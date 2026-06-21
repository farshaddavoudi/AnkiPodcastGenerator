using System.Diagnostics;
using System.Globalization;
using System.Text;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.Kokoro;

public sealed class KokoroTextToSpeechProvider : ITextToSpeechProvider, IMultiSpeakerTextToSpeechProvider
{
    private const int MaxChunkAttempts = 3;

    private readonly KokoroOptions _options;
    private readonly AvalAiOptions _speechOptions;
    private readonly IAudioCombiner _audioCombiner;
    private readonly ILogger<KokoroTextToSpeechProvider> _logger;

    public KokoroTextToSpeechProvider(
        IOptions<KokoroOptions> options,
        IOptions<AvalAiOptions> speechOptions,
        IAudioCombiner audioCombiner,
        ILogger<KokoroTextToSpeechProvider> logger)
    {
        _options = options.Value;
        _speechOptions = speechOptions.Value;
        _audioCombiner = audioCombiner;
        _logger = logger;
    }

    public async Task<TextToSpeechResult> GenerateMp3Async(
        string text,
        string voice,
        string outputPath,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(voice);

        var tempDirectory = CreateTempDirectory();
        try
        {
            var inputPath = Path.Combine(tempDirectory, "input.txt");
            await WriteTextAsync(inputPath, text, cancellationToken);

            await GenerateChunkWithRetryAsync(inputPath, outputPath, voice, 1, cancellationToken);

            var bytesWritten = new FileInfo(outputPath).Length;
            _logger.LogInformation(
                "Local Kokoro TTS cost: {CostSummary}",
                GenerationCostFormatter.Format(ApiCostEstimate.Local, "TTS"));
            return new TextToSpeechResult(_options.ModelName, voice, outputPath, bytesWritten, ApiCostEstimate.Local);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public async Task<TextToSpeechResult> GenerateMultiSpeakerMp3Async(
        IReadOnlyList<PodcastSegment> segments,
        string voiceA,
        string voiceB,
        string outputPath,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(voiceA);
        EnsureConfigured(voiceB);

        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Cannot generate multi-speaker audio with zero script segments.");
        }

        var tempDirectory = CreateTempDirectory();
        try
        {
            var chunkFiles = new List<string>();
            var mergedSegments = MergeAdjacentSegments(segments);

            for (var i = 0; i < mergedSegments.Count; i++)
            {
                var segment = mergedSegments[i];
                var chunkPath = Path.Combine(tempDirectory, $"{i + 1:000}.mp3");

                if (segment.PauseAfterSeconds > 0)
                {
                    await _audioCombiner.CreateSilenceMp3Async(segment.PauseAfterSeconds, chunkPath, cancellationToken);
                    chunkFiles.Add(chunkPath);
                    continue;
                }

                var voice = segment.Speaker == 'B' ? voiceB : voiceA;
                var inputPath = Path.Combine(tempDirectory, $"{i + 1:000}-input.txt");

                await WriteTextAsync(inputPath, segment.Text, cancellationToken);
                await GenerateChunkWithRetryAsync(inputPath, chunkPath, voice, i + 1, cancellationToken);
                chunkFiles.Add(chunkPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (chunkFiles.Count == 1)
            {
                File.Copy(chunkFiles[0], outputPath, overwrite: true);
            }
            else
            {
                await _audioCombiner.CombineMp3Async(chunkFiles, outputPath, cancellationToken);
            }

            var bytesWritten = new FileInfo(outputPath).Length;
            _logger.LogInformation(
                "Local Kokoro TTS cost: {CostSummary}",
                GenerationCostFormatter.Format(ApiCostEstimate.Local, "TTS"));
            return new TextToSpeechResult(_options.ModelName, $"{voiceA}/{voiceB}", outputPath, bytesWritten, ApiCostEstimate.Local);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task GenerateChunkAsync(
        string inputPath,
        string outputPath,
        string voice,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var workingDirectory = GetWorkingDirectory();
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _options.Command,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        processStartInfo.Environment["PYTHONUTF8"] = "1";
        processStartInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        processStartInfo.ArgumentList.Add(inputPath);
        processStartInfo.ArgumentList.Add(outputPath);
        processStartInfo.ArgumentList.Add("--speed");
        processStartInfo.ArgumentList.Add(_speechOptions.TtsSpeed.ToString("0.###", CultureInfo.InvariantCulture));
        processStartInfo.ArgumentList.Add("--lang");
        processStartInfo.ArgumentList.Add(_options.Language);
        processStartInfo.ArgumentList.Add("--voice");
        processStartInfo.ArgumentList.Add(voice);
        processStartInfo.ArgumentList.Add("--format");
        processStartInfo.ArgumentList.Add("mp3");

        _logger.LogInformation(
            "Generating MP3 via local Kokoro TTS. Model={Model}, Voice={Voice}, Command={Command}, WorkingDirectory={WorkingDirectory}",
            _options.ModelName,
            voice,
            _options.Command,
            workingDirectory);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start Kokoro TTS command '{_options.Command}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Kokoro TTS command timed out after {_options.TimeoutSeconds} seconds. " +
                "Use a shorter deck run or increase Kokoro:TimeoutSeconds.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Kokoro TTS command failed with exit code {process.ExitCode}. " +
                $"Stdout: {Truncate(stdout)} Stderr: {Truncate(stderr)}");
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"Kokoro TTS completed but did not create the expected MP3: {outputPath}");
        }

        var bytesWritten = new FileInfo(outputPath).Length;
        if (bytesWritten == 0)
        {
            throw new InvalidOperationException($"Kokoro TTS created an empty MP3: {outputPath}");
        }

        _logger.LogInformation(
            "Saved local Kokoro MP3 chunk. Model={Model}, Voice={Voice}, OutputPath={OutputPath}, Bytes={Bytes}",
            _options.ModelName,
            voice,
            outputPath,
            bytesWritten);
    }

    private async Task GenerateChunkWithRetryAsync(
        string inputPath,
        string outputPath,
        string voice,
        int chunkNumber,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxChunkAttempts; attempt++)
        {
            try
            {
                await GenerateChunkAsync(inputPath, outputPath, voice, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxChunkAttempts && ex is InvalidOperationException or TimeoutException or IOException)
            {
                TryDeleteFile(outputPath);
                _logger.LogWarning(
                    ex,
                    "Kokoro TTS chunk failed. Chunk={Chunk}, Voice={Voice}, Attempt={Attempt}. Retrying.",
                    chunkNumber,
                    voice,
                    attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
            }
        }

        await GenerateChunkAsync(inputPath, outputPath, voice, cancellationToken);
    }

    private string GetWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            return Directory.GetCurrentDirectory();
        }

        var workingDirectory = Environment.ExpandEnvironmentVariables(_options.WorkingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException(
                $"Kokoro working directory does not exist: {workingDirectory}. " +
                "This folder should contain kokoro-v1.0.onnx and voices-v1.0.bin.");
        }

        return workingDirectory;
    }

    private void EnsureConfigured(string voice)
    {
        if (string.IsNullOrWhiteSpace(_options.Command))
        {
            throw new InvalidOperationException("Kokoro:Command is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.ModelName))
        {
            throw new InvalidOperationException("Kokoro:ModelName is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.Language))
        {
            throw new InvalidOperationException("Kokoro:Language is missing.");
        }

        if (string.IsNullOrWhiteSpace(voice))
        {
            throw new InvalidOperationException("A Kokoro voice must be configured.");
        }

        if (_options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Kokoro:TimeoutSeconds must be positive.");
        }

        if (_speechOptions.TtsSpeed is < 0.25 or > 4.0)
        {
            throw new InvalidOperationException("TTS speed must be between 0.25 and 4.0.");
        }
    }

    private static IReadOnlyList<PodcastSegment> MergeAdjacentSegments(IReadOnlyList<PodcastSegment> segments)
    {
        var merged = new List<PodcastSegment>();

        foreach (var segment in segments)
        {
            if (segment.PauseAfterSeconds > 0)
            {
                merged.Add(new PodcastSegment('P', string.Empty, segment.PauseAfterSeconds));
                continue;
            }

            var normalizedSpeaker = segment.Speaker == 'B' ? 'B' : 'A';
            var text = segment.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (merged.Count > 0 && merged[^1].PauseAfterSeconds == 0 && merged[^1].Speaker == normalizedSpeaker)
            {
                var previous = merged[^1];
                merged[^1] = previous with { Text = previous.Text.TrimEnd() + Environment.NewLine + Environment.NewLine + text };
                continue;
            }

            merged.Add(new PodcastSegment(normalizedSpeaker, text));
        }

        return merged;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "AnkiPodcastGenerator",
            $"kokoro-tts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
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
            _logger.LogWarning(ex, "Could not delete temporary Kokoro TTS directory {TempDirectory}", path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Truncate(string value)
    {
        const int maxLength = 4000;
        return string.IsNullOrWhiteSpace(value)
            ? "(empty)"
            : value.Length <= maxLength
                ? value
                : value[..maxLength] + "...";
    }
}
