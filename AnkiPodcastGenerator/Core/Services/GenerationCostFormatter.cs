using System.Globalization;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public static class GenerationCostFormatter
{
    public static string Format(ApiCostEstimate? cost, string label)
    {
        if (cost is null)
        {
            return $"{label}: cost unavailable";
        }

        if (cost.Source == "local")
        {
            return $"{label}: $0.00 USD / 0 toman (local, no API charge)";
        }

        var usd = FormatUsd(cost.Usd);
        var toman = cost.Irt is null ? "n/a" : $"{cost.Irt.Value.ToString("N0", CultureInfo.InvariantCulture)} toman";
        var source = cost.Source switch
        {
            "avalai_estimated_cost" => "AvalAI estimated",
            "avalai_calculated_script" => "estimated from script tokens",
            "avalai_calculated_tts" => "estimated from audio duration",
            "openrouter_usage_cost" => "OpenRouter reported",
            _ => cost.Source
        };

        return $"{label}: ${usd} USD / {toman} ({source})";
    }

    public static string FormatSummary(
        ApiCostEstimate? scriptCost,
        ApiCostEstimate? ttsCost,
        ApiCostEstimate? totalCost)
    {
        var parts = new List<string>
        {
            Format(scriptCost, "Script"),
            Format(ttsCost, "TTS"),
            Format(totalCost, "Total")
        };

        return string.Join("; ", parts);
    }

    private static string FormatUsd(decimal usd) =>
        usd.ToString("0.######", CultureInfo.InvariantCulture);
}
