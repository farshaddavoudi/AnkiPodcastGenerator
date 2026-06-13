using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IProfileProvider
{
    PodcastProfile GetRequiredProfile(string name);
    IReadOnlyList<PodcastProfile> GetAllProfiles();
}
