using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Configuration;

public sealed class DecksOptions
{
    public List<PodcastDeck> Decks { get; init; } = [];
}
