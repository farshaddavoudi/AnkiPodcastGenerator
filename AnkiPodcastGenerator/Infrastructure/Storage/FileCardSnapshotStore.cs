using System.Text.Json;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class FileCardSnapshotStore : ICardSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task SaveAsync(CardSnapshot snapshot, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }

    public async Task<CardSnapshot?> LoadAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(inputPath);
            return await JsonSerializer.DeserializeAsync<CardSnapshot>(stream, JsonOptions, cancellationToken);
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
}
