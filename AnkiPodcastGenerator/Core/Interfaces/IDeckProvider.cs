using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IDeckProvider
{
    PodcastDeck GetRequiredDeck(string deckName);
    IReadOnlyList<PodcastDeck> GetAllDecks();
}
