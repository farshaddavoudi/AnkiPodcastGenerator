using System.Text;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class PodcastScriptParser : IPodcastScriptParser
{
    public IReadOnlyList<PodcastSegment> Parse(string script)
    {
        var segments = new List<PodcastSegment>();
        using var reader = new StringReader(script);
        var currentSpeaker = '\0';
        var buffer = new StringBuilder();

        while (reader.ReadLine() is { } line)
        {
            var marker = TryReadMarker(line);
            if (marker is not null)
            {
                Flush();
                currentSpeaker = marker.Value;
                var rest = line.Trim()[3..].Trim();

                if (rest.Length > 0)
                {
                    buffer.AppendLine(rest);
                }

                continue;
            }

            if (currentSpeaker != '\0')
            {
                buffer.AppendLine(line);
            }
        }

        Flush();

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(script))
        {
            segments.Add(new PodcastSegment('A', script.Trim()));
        }

        return segments;

        void Flush()
        {
            if (currentSpeaker == '\0')
            {
                buffer.Clear();
                return;
            }

            var text = buffer.ToString().Trim();
            if (text.Length > 0)
            {
                segments.Add(new PodcastSegment(currentSpeaker, text));
            }

            buffer.Clear();
        }
    }

    private static char? TryReadMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3 || trimmed[0] != '[' || trimmed[2] != ']')
        {
            return null;
        }

        return trimmed[1] switch
        {
            'A' => 'A',
            'B' => 'B',
            _ => null
        };
    }
}
