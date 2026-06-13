using System.Diagnostics;
using AnkiPodcastGenerator.Core.Interfaces;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class FfmpegPcmAudioEncoder : IPcmAudioEncoder
{
    public async Task EncodePcmToMp3Async(
        string pcmPath,
        int sampleRate,
        int channels,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pcmPath))
        {
            throw new FileNotFoundException("PCM input file was not found.", pcmPath);
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be positive.");
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
                     "s16le",
                     "-ar",
                     sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-ac",
                     channels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-i",
                     pcmPath,
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
            throw new InvalidOperationException($"ffmpeg PCM encode failed with exit code {process.ExitCode}. {stderr}{stdout}");
        }
    }
}
