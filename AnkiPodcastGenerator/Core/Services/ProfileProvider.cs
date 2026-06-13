using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class ProfileProvider(ProfilesOptions options) : IProfileProvider
{
    public PodcastProfile GetRequiredProfile(string name)
    {
        var profile = options.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            var available = string.Join(", ", options.Profiles.Select(profile => profile.Name));
            throw new InvalidOperationException($"Unknown profile '{name}'. Available profiles: {available}");
        }

        return profile;
    }

    public IReadOnlyList<PodcastProfile> GetAllProfiles() => options.Profiles;
}
