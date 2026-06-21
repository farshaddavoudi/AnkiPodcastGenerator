using System.Text.Json;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class FileMetadataStore : IMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<GeneratedPodcastMetadata?> FindReusableAsync(
        OutputPaths outputPaths,
        string cardHash,
        string generationSettingsHash,
        CancellationToken cancellationToken)
    {
        foreach (var metadataPath in EnumerateCandidateMetadataPaths(outputPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await TryReadMetadataAsync(metadataPath, cancellationToken);
            if (metadata is null)
            {
                continue;
            }

            if (!MatchesDeckSlug(metadata, outputPaths.DeckSlug))
            {
                continue;
            }

            if (!HashesMatch(metadata, cardHash, generationSettingsHash))
            {
                continue;
            }

            if (!File.Exists(metadata.Mp3Path))
            {
                continue;
            }

            return metadata;
        }

        return null;
    }

    public async Task<GeneratedPodcastMetadata?> FindLatestAsync(
        OutputPaths outputPaths,
        string generationSettingsHash,
        CancellationToken cancellationToken)
    {
        foreach (var metadataPath in EnumerateCandidateMetadataPaths(outputPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await TryReadMetadataAsync(metadataPath, cancellationToken);
            if (metadata is null)
            {
                continue;
            }

            if (!MatchesDeckSlug(metadata, outputPaths.DeckSlug))
            {
                continue;
            }

            if (!string.Equals(metadata.GenerationSettingsHash, generationSettingsHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!File.Exists(metadata.Mp3Path))
            {
                continue;
            }

            return metadata;
        }

        return null;
    }

    public async Task SaveAsync(OutputPaths outputPaths, GeneratedPodcastMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPaths.MetadataPath)!);
        await using var stream = File.Create(outputPaths.MetadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
    }

    private static IEnumerable<string> EnumerateCandidateMetadataPaths(OutputPaths outputPaths)
    {
        var candidates = new List<string>();

        if (File.Exists(outputPaths.MetadataPath))
        {
            candidates.Add(outputPaths.MetadataPath);
        }

        candidates.AddRange(FindOtherCandidateMetadataFiles(outputPaths));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc);
    }

    private static IEnumerable<string> FindOtherCandidateMetadataFiles(OutputPaths outputPaths)
    {
        var candidates = new List<string>();
        var legacyMetadataPath = Path.Combine(outputPaths.OutputFolder, outputPaths.DeckSlug, "generated.json");
        if (File.Exists(legacyMetadataPath))
        {
            candidates.Add(legacyMetadataPath);
        }

        try
        {
            var datedMetadataPaths = Directory
                .EnumerateDirectories(outputPaths.OutputFolder)
                .Select(directory => Path.Combine(directory, "_metadata", outputPaths.DeckSlug, "generated.json"))
                .Where(File.Exists);

            candidates.AddRange(datedMetadataPaths);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return candidates
            .Where(path => !SamePath(path, outputPaths.MetadataPath))
            .ToArray();
    }

    private static async Task<GeneratedPodcastMetadata?> TryReadMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync<GeneratedPodcastMetadata>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool MatchesDeckSlug(GeneratedPodcastMetadata metadata, string deckSlug)
    {
        var metadataSlug = string.IsNullOrWhiteSpace(metadata.DeckSlug)
            ? metadata.ProfileSlug
            : metadata.DeckSlug;

        return string.Equals(metadataSlug, deckSlug, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HashesMatch(
        GeneratedPodcastMetadata metadata,
        string cardHash,
        string generationSettingsHash) =>
        string.Equals(metadata.CardHash, cardHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(metadata.GenerationSettingsHash, generationSettingsHash, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
