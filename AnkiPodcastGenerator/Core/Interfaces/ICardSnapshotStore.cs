using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface ICardSnapshotStore
{
    Task SaveAsync(CardSnapshot snapshot, string outputPath, CancellationToken cancellationToken);
}
