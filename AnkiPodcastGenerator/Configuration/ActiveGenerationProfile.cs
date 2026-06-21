using System.Text;

namespace AnkiPodcastGenerator.Configuration;

public sealed class ActiveGenerationProfile
{
    public ActiveGenerationProfile(string name, GenerationProfile? profile)
    {
        Name = name;
        Profile = profile;
        Slug = string.IsNullOrWhiteSpace(name) ? null : Sanitize(name);
    }

    public string Name { get; }
    public string? Slug { get; }
    public GenerationProfile? Profile { get; }

    public bool IsConfigured => Profile is not null && !string.IsNullOrWhiteSpace(Name);

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder();
        var lastWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
