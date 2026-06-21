using System.Globalization;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

internal static class AvalAiPricingEstimator
{
    private const decimal DefaultExchangeRateIrt = 131_350m;

    private static readonly Dictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> ScriptModelPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-2.5-flash"] = (0.30m, 2.50m),
            ["gemini-flash-latest"] = (0.30m, 2.50m),
            ["gemini-2.5-flash-lite-preview-09-2025"] = (0.10m, 0.40m),
            ["gemini-flash-lite-latest"] = (0.10m, 0.40m),
            ["claude-sonnet-4-6"] = (3.00m, 15.00m),
            ["anthropic.claude-sonnet-4-6-v1"] = (3.00m, 15.00m),
        };

    private static readonly Dictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> TtsModelPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-2.5-flash-tts"] = (0.50m, 10.00m),
            ["gemini-2.5-pro-tts"] = (1.00m, 20.00m),
        };

    public static ApiCostEstimate? EstimateScriptCost(string model, TokenUsage? tokenUsage)
    {
        if (tokenUsage is null)
        {
            return null;
        }

        if (!ScriptModelPricing.TryGetValue(NormalizeModel(model), out var pricing))
        {
            return null;
        }

        var promptTokens = tokenUsage.PromptTokens ?? 0;
        var completionTokens = tokenUsage.CompletionTokens ?? 0;
        var usd = (promptTokens * pricing.InputPerMillion / 1_000_000m) +
                  (completionTokens * pricing.OutputPerMillion / 1_000_000m);

        return ToEstimate(usd, "avalai_calculated_script");
    }

    public static ApiCostEstimate? EstimateTtsCostFromAudioSeconds(string model, double audioSeconds)
    {
        if (audioSeconds <= 0)
        {
            return null;
        }

        if (!TtsModelPricing.TryGetValue(NormalizeModel(model), out var pricing))
        {
            return null;
        }

        const decimal audioTokensPerSecond = 32m;
        var outputTokens = (decimal)(audioSeconds * (double)audioTokensPerSecond);
        var usd = outputTokens * pricing.OutputPerMillion / 1_000_000m;
        return ToEstimate(usd, "avalai_calculated_tts");
    }

    public static double EstimateMp3DurationSeconds(long mp3Bytes) =>
        mp3Bytes <= 0 ? 0 : mp3Bytes / 16_000d;

    private static ApiCostEstimate ToEstimate(decimal usd, string source) =>
        new(
            decimal.Round(usd, 6, MidpointRounding.AwayFromZero),
            decimal.Round(usd * DefaultExchangeRateIrt, 0, MidpointRounding.AwayFromZero),
            DefaultExchangeRateIrt,
            source);

    private static string NormalizeModel(string model)
    {
        var normalized = model.Trim();
        var colonIndex = normalized.IndexOf(':');
        return colonIndex >= 0 ? normalized[..colonIndex] : normalized;
    }
}
