using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IMetadataStore
{
    Task<GeneratedPodcastMetadata?> FindReusableAsync(
        OutputPaths outputPaths,
        string cardHash,
        string generationSettingsHash,
        CancellationToken cancellationToken);

    Task<GeneratedPodcastMetadata?> FindLatestAsync(
        OutputPaths outputPaths,
        string generationSettingsHash,
        CancellationToken cancellationToken);

    Task SaveAsync(OutputPaths outputPaths, GeneratedPodcastMetadata metadata, CancellationToken cancellationToken);
}
