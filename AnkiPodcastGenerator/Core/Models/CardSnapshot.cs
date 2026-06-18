namespace AnkiPodcastGenerator.Core.Models;

public sealed record CardSnapshot(
    string DeckName,
    string AnkiQuery,
    DateTimeOffset FetchedAtUtc,
    IReadOnlyList<AnkiCard> Cards);
