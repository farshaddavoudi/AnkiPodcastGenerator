using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPodcastScriptParser
{
    IReadOnlyList<PodcastSegment> Parse(string script);
}
