using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPodcastScriptGenerator
{
    Task<ScriptGenerationResult> GenerateScriptAsync(
        IReadOnlyList<AnkiCard> cards,
        PodcastDeck deck,
        int targetMinutes,
        CancellationToken cancellationToken);
}
