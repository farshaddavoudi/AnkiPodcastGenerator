namespace AnkiPodcastGenerator.Core.Models;

public sealed record PodcastGenerationResult(
    bool Success,
    bool Reused,
    int CardCount,
    string? Mp3Path,
    string Message,
    IReadOnlyList<string>? Mp3Paths = null);
