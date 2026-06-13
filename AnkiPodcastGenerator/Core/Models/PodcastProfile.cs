namespace AnkiPodcastGenerator.Core.Models;

public sealed class PodcastProfile
{
    public string Name { get; set; } = string.Empty;
    public string AnkiQuery { get; set; } = "is:due";
    public int? TargetMinutes { get; set; }
    public int? MaxCards { get; set; }
    public bool? MultiSpeaker { get; set; }
    public string? OutputSlug { get; set; }
}
