using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IOutputPathService
{
    OutputPaths GetPaths(PodcastProfile profile, DateOnly date);
    string GetSlug(PodcastProfile profile);
}
