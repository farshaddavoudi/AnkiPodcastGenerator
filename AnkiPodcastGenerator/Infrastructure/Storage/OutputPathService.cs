using System.Globalization;
using System.Text;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class OutputPathService(IOptions<PodcastOptions> options) : IOutputPathService
{
    public OutputPaths GetPaths(PodcastDeck deck, DateOnly date)
    {
        var outputFolder = options.Value.OutputFolder;
        var slug = GetSlug(deck);
        var datePrefix = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateFolder = Path.Combine(outputFolder, datePrefix);
        var metadataFolder = Path.Combine(dateFolder, "_metadata", slug);

        return new OutputPaths(
            outputFolder,
            slug,
            Path.Combine(metadataFolder, "cards.json"),
            Path.Combine(metadataFolder, "script.txt"),
            Path.Combine(dateFolder, $"{slug}.mp3"),
            Path.Combine(metadataFolder, "generated.json"));
    }

    public string GetSlug(PodcastDeck deck)
    {
        if (!string.IsNullOrWhiteSpace(deck.OutputSlug))
        {
            return Sanitize(deck.OutputSlug);
        }

        return Sanitize(deck.DeckName);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder();
        var lastWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
