using System.Text.Json;

namespace AnkiPodcastGenerator.Infrastructure.AvalAi;

internal static class AvalAiHttpErrors
{
    private const string RechargeUrl = "https://avalai.ir";

    public static bool IsQuotaOrBillingError(int statusCode, string? responseBody) =>
        statusCode is 402 or 429 ||
        (statusCode == 403 && ContainsQuotaHint(responseBody)) ||
        ContainsQuotaHint(responseBody);

    public static InvalidOperationException CreateQuotaExhaustedException(
        string operation,
        int statusCode,
        string? responseBody = null)
    {
        var detail = ExtractApiDetail(responseBody);
        var lines = new List<string>
        {
            $"AvalAI credits or quota are exhausted (HTTP {statusCode}).",
            string.Empty,
            $"Action required: open {RechargeUrl}, sign in, and recharge your AvalAI account.",
            "After recharging, rerun the podcast generator.",
            string.Empty,
            $"Operation: {operation}"
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            lines.Add(detail);
        }

        return new InvalidOperationException(string.Join(Environment.NewLine, lines));
    }

    public static void ThrowIfQuotaExhausted(string operation, int statusCode, string? responseBody)
    {
        if (IsQuotaOrBillingError(statusCode, responseBody))
        {
            throw CreateQuotaExhaustedException(operation, statusCode, responseBody);
        }
    }

    public static InvalidOperationException CreateHttpFailureException(
        string serviceLabel,
        string operation,
        int statusCode,
        string responseBody,
        IEnumerable<string>? priorFailures = null)
    {
        if (IsQuotaOrBillingError(statusCode, responseBody))
        {
            return CreateQuotaExhaustedException(operation, statusCode, responseBody);
        }

        var failures = priorFailures?.ToList() ?? [];
        failures.Add(FormatTechnicalFailure(statusCode, responseBody));
        return new InvalidOperationException($"{serviceLabel} failed. {string.Join(Environment.NewLine, failures)}");
    }

    public static InvalidOperationException? TryCreateFromFailureDetails(
        string operation,
        IEnumerable<string> failures)
    {
        foreach (var failure in failures)
        {
            if (!ContainsQuotaHint(failure))
            {
                continue;
            }

            var statusCode = TryExtractStatusCode(failure) ?? 429;
            return CreateQuotaExhaustedException(operation, statusCode);
        }

        return null;
    }

    private static string FormatTechnicalFailure(int statusCode, string responseBody)
    {
        var detail = ExtractApiDetail(responseBody);
        return string.IsNullOrWhiteSpace(detail)
            ? $"HTTP {statusCode}."
            : $"HTTP {statusCode}. {detail}";
    }

    private static string ExtractApiDetail(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return $"API message: {text.Trim()}";
                }
            }
        }
        catch (JsonException)
        {
        }

        var trimmed = responseBody.Trim();
        if (trimmed.Length > 300)
        {
            trimmed = trimmed[..300] + "...";
        }

        return $"API response: {trimmed}";
    }

    private static bool ContainsQuotaHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lower = value.ToLowerInvariant();
        return lower.Contains("quota") ||
               lower.Contains("balance") ||
               lower.Contains("credit") ||
               lower.Contains("insufficient") ||
               lower.Contains("exhausted") ||
               lower.Contains("rate limit") ||
               lower.Contains("too many requests") ||
               lower.Contains("http 429") ||
               lower.Contains("http 402");
    }

    private static int? TryExtractStatusCode(string failure)
    {
        const string marker = "http ";
        var index = failure.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < failure.Length && char.IsDigit(failure[end]))
        {
            end++;
        }

        return int.TryParse(failure[start..end], out var statusCode) ? statusCode : null;
    }
}
