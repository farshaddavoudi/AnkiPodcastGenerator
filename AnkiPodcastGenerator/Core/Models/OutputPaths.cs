namespace AnkiPodcastGenerator.Core.Models;

public sealed record OutputPaths(
    string OutputFolder,
    string ProfileSlug,
    string CardsJsonPath,
    string ScriptPath,
    string Mp3Path,
    string MetadataPath);
