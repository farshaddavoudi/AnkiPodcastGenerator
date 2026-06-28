namespace AnkiPodcastGenerator.Core.Models;

public sealed class PodcastDeck
{
    public string DeckName { get; set; } = string.Empty;
    public int MaxCards { get; set; }
    public int? CardsPerPodcast { get; set; }
    public int? TargetMinutes { get; set; }
    public bool? MultiSpeaker { get; set; }
    public string? OutputSlug { get; set; }
    public string? CustomPrompt { get; set; }
}
