namespace AnkiPodcastGenerator.Core.Models;

public sealed record OutputPaths(
    string OutputFolder,
    string DeckSlug,
    string CardsJsonPath,
    string ScriptPath,
    string Mp3Path,
    string MetadataPath,
    int BundleIndex = 1,
    int TotalBundles = 1);
