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

    public async Task<GeneratedPodcastMetadata?> LoadAsync(OutputPaths outputPaths, CancellationToken cancellationToken)
    {
        if (File.Exists(outputPaths.MetadataPath))
        {
            await using var stream = File.OpenRead(outputPaths.MetadataPath);
            return await JsonSerializer.DeserializeAsync<GeneratedPodcastMetadata>(stream, JsonOptions, cancellationToken);
        }

        if (!Directory.Exists(outputPaths.OutputFolder))
        {
            return null;
        }

        foreach (var metadataPath in FindCandidateMetadataFiles(outputPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();

            GeneratedPodcastMetadata? metadata;
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                metadata = await JsonSerializer.DeserializeAsync<GeneratedPodcastMetadata>(stream, JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (metadata is null)
            {
                continue;
            }

            var metadataSlug = string.IsNullOrWhiteSpace(metadata.DeckSlug)
                ? metadata.ProfileSlug
                : metadata.DeckSlug;

            if (!string.Equals(metadataSlug, outputPaths.DeckSlug, StringComparison.OrdinalIgnoreCase))
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

    private static IEnumerable<string> FindCandidateMetadataFiles(OutputPaths outputPaths)
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
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc);

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
