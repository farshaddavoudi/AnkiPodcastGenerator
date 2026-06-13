using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPodcastGeneratorService
{
    Task<PodcastGenerationResult> GenerateAsync(string profileName, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnkiCard>> PreviewCardsAsync(string profileName, int? maxCards, CancellationToken cancellationToken);
    Task<int> TestAnkiConnectivityAsync(CancellationToken cancellationToken);
}
