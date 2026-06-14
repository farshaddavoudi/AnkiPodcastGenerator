using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IAnkiConnectClient
{
    Task<int> GetVersionAsync(CancellationToken cancellationToken);
    Task SyncAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<long>> FindCardsAsync(string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnkiCard>> CardsInfoAsync(IEnumerable<long> cardIds, CancellationToken cancellationToken);
}
