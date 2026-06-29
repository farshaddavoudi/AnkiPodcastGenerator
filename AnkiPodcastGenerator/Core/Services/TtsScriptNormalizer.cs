using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class TtsScriptNormalizer : ITtsScriptNormalizer
{
    private readonly TtsNormalizerOptions _options;
    private readonly LlmTtsScriptNormalizer? _llmNormalizer;
    private readonly ILogger<TtsScriptNormalizer> _logger;

    public TtsScriptNormalizer(
        IOptions<TtsNormalizerOptions> options,
        ILogger<TtsScriptNormalizer> logger,
        LlmTtsScriptNormalizer? llmNormalizer = null)
    {
        _options = options.Value;
        _llmNormalizer = llmNormalizer;
        _logger = logger;
    }

    public async Task<TtsNormalizationResult> NormalizeScriptAsync(
        string displayScript,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("TTS normalization is disabled. Passing script through unchanged.");
            return new TtsNormalizationResult(displayScript, displayScript, []);
        }

        if (string.IsNullOrWhiteSpace(displayScript))
        {
            return new TtsNormalizationResult(displayScript, displayScript, []);
        }

        // Step 1: Apply static pronunciation dictionary
        var ttsScript = displayScript;
        var staticMap = StaticPronunciationDictionary.Apply(ref ttsScript);

        _logger.LogInformation(
            "Static pronunciation dictionary applied: {ReplacementCount} replacements",
            staticMap.Count);

        // Step 2: Optionally apply LLM normalization
        var mode = string.IsNullOrWhiteSpace(_options.Mode) ? "StaticThenLlm" : _options.Mode.Trim();
        var combinedMap = staticMap;

        if ((string.Equals(mode, "LlmOnly", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(mode, "StaticThenLlm", StringComparison.OrdinalIgnoreCase)) &&
            _llmNormalizer is not null)
        {
            if (string.Equals(mode, "LlmOnly", StringComparison.OrdinalIgnoreCase))
            {
                // Start from original script, not static-normalized
                ttsScript = displayScript;
                staticMap = [];
            }

            var llmResult = await _llmNormalizer.NormalizeWithLlmAsync(ttsScript, staticMap, cancellationToken);
            ttsScript = llmResult.TtsScript;
            combinedMap = llmResult.PronunciationMap;
        }

        _logger.LogInformation(
            "TTS normalization complete. Total replacements: {TotalCount}",
            combinedMap.Count);

        return new TtsNormalizationResult(displayScript, ttsScript, combinedMap);
    }
}
