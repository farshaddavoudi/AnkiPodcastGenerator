using System.Diagnostics;
using AnkiPodcastGenerator.Core.Interfaces;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class FfmpegAudioCombiner : IAudioCombiner
{
    public async Task CombineMp3Async(IReadOnlyList<string> inputFiles, string outputPath, CancellationToken cancellationToken)
    {
        if (inputFiles.Count == 0)
        {
            throw new InvalidOperationException("Cannot combine zero audio files.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "AnkiPodcastGenerator");
        Directory.CreateDirectory(tempDirectory);
        var listPath = Path.Combine(tempDirectory, $".ffmpeg-concat-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllLinesAsync(
                listPath,
                inputFiles.Select(path => $"file '{EscapeForConcatFile(Path.GetFullPath(path))}'"),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in new[]
                     {
                         "-y",
                         "-hide_banner",
                         "-loglevel",
                         "error",
                         "-f",
                         "concat",
                         "-safe",
                         "0",
                         "-i",
                         listPath,
                         "-codec:a",
                         "libmp3lame",
                         "-q:a",
                         "3",
                         outputPath
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}. {stderr}{stdout}");
            }
        }
        finally
        {
            if (File.Exists(listPath))
            {
                TryDeleteFile(listPath);
            }
        }
    }

    public async Task CreateSilenceMp3Async(int seconds, string outputPath, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Silence duration must be positive.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in new[]
                 {
                     "-y",
                     "-hide_banner",
                     "-loglevel",
                     "error",
                     "-f",
                     "lavfi",
                     "-i",
                     "anullsrc=channel_layout=mono:sample_rate=24000",
                     "-t",
                     seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-codec:a",
                     "libmp3lame",
                     "-q:a",
                     "3",
                     outputPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stderr = await stderrTask;
        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}. {stderr}{stdout}");
        }
    }

    private static string EscapeForConcatFile(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup should not make an otherwise successful audio generation fail.
        }
    }
}
