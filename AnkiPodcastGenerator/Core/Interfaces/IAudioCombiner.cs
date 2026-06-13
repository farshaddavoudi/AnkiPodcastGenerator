namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IAudioCombiner
{
    Task CombineMp3Async(IReadOnlyList<string> inputFiles, string outputPath, CancellationToken cancellationToken);
}
