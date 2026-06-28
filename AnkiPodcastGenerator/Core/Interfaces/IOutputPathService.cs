using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IOutputPathService
{
    OutputPaths GetPaths(PodcastDeck deck, DateOnly date);
    OutputPaths GetPaths(PodcastDeck deck, DateOnly date, int bundleIndex, int totalBundles);
    string GetSlug(PodcastDeck deck);
}
