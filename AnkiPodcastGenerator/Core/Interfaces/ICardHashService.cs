using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface ICardHashService
{
    string ComputeHash(IEnumerable<AnkiCard> cards);
}
