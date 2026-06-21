using System.Globalization;
using System.Text.Json;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

internal static class AvalAiCostParser
{
    public static ApiCostEstimate? TryParseEstimatedCost(string? responseBody, string source = "avalai_estimated_cost")
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return TryParseEstimatedCost(document.RootElement, source);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ApiCostEstimate? TryParseEstimatedCost(JsonElement root, string source = "avalai_estimated_cost")
    {
        if (!root.TryGetProperty("estimated_cost", out var estimatedCost) ||
            estimatedCost.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryReadDecimal(estimatedCost, "unit", out var usd))
        {
            return null;
        }

        TryReadDecimal(estimatedCost, "irt", out var irt);
        TryReadDecimal(estimatedCost, "exchange_rate", out var exchangeRate);

        return new ApiCostEstimate(
            usd,
            irt > 0 ? irt : null,
            exchangeRate > 0 ? exchangeRate : null,
            source);
    }

    public static ApiCostEstimate? TryParseOpenRouterUsageCost(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object ||
                !TryReadDecimal(usage, "cost", out var usd))
            {
                return null;
            }

            return new ApiCostEstimate(usd, null, null, "openrouter_usage_cost");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadDecimal(JsonElement parent, string propertyName, out decimal value)
    {
        value = 0m;
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }
}
