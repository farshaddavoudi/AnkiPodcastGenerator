namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IAudioCombiner
{
    Task CombineMp3Async(IReadOnlyList<string> inputFiles, string outputPath, CancellationToken cancellationToken);
    Task CreateSilenceMp3Async(int seconds, string outputPath, CancellationToken cancellationToken);
}
