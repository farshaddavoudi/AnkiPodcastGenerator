using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IMetadataStore
{
    Task<GeneratedPodcastMetadata?> LoadAsync(OutputPaths outputPaths, CancellationToken cancellationToken);
    Task SaveAsync(OutputPaths outputPaths, GeneratedPodcastMetadata metadata, CancellationToken cancellationToken);
}
