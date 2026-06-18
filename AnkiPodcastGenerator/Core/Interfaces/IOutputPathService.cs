using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IOutputPathService
{
    OutputPaths GetPaths(PodcastDeck deck, DateOnly date);
    string GetSlug(PodcastDeck deck);
}
