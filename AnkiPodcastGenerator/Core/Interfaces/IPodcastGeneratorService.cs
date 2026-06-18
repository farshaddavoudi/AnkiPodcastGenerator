using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPodcastGeneratorService
{
    Task<PodcastGenerationResult> GenerateAsync(string deckName, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnkiCard>> PreviewCardsAsync(string deckName, int? maxCards, CancellationToken cancellationToken);
    Task<int> TestAnkiConnectivityAsync(CancellationToken cancellationToken);
}
