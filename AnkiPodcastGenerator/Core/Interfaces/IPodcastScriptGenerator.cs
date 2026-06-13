using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPodcastScriptGenerator
{
    Task<ScriptGenerationResult> GenerateScriptAsync(
        IReadOnlyList<AnkiCard> cards,
        PodcastProfile profile,
        int targetMinutes,
        CancellationToken cancellationToken);
}
