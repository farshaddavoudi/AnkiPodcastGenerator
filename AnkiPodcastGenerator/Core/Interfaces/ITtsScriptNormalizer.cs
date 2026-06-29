using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface ITtsScriptNormalizer
{
    Task<TtsNormalizationResult> NormalizeScriptAsync(string displayScript, CancellationToken cancellationToken);
}
