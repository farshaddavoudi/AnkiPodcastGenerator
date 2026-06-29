namespace AnkiPodcastGenerator.Core.Models;

public sealed record TtsNormalizationResult(
    string DisplayScript,
    string TtsScript,
    IReadOnlyList<PronunciationMapItem> PronunciationMap);

public sealed record PronunciationMapItem(
    string Original,
    string Replacement,
    string Reason);
