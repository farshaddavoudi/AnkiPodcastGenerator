using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Configuration;

public sealed class ProfilesOptions
{
    public List<PodcastProfile> Profiles { get; init; } = [];
}
