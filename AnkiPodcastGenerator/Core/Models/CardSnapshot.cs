namespace AnkiPodcastGenerator.Core.Models;

public sealed record CardSnapshot(
    string ProfileName,
    string AnkiQuery,
    DateTimeOffset FetchedAtUtc,
    IReadOnlyList<AnkiCard> Cards);
