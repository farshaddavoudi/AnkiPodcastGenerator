namespace AnkiPodcastGenerator.Core.Models;

public sealed record AnkiCard(
    long CardId,
    long NoteId,
    string DeckName,
    int Type,
    int Queue,
    long Due,
    string Front,
    string Back,
    IReadOnlyList<string> Tags);
