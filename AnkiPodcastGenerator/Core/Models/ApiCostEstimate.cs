namespace AnkiPodcastGenerator.Core.Models;

public sealed record ApiCostEstimate(
    decimal Usd,
    decimal? Irt,
    decimal? ExchangeRate,
    string Source)
{
    public static ApiCostEstimate Local => new(0m, 0m, null, "local");

    public static ApiCostEstimate? TryCombine(ApiCostEstimate? left, ApiCostEstimate? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        var exchangeRate = left.ExchangeRate ?? right.ExchangeRate;
        decimal? irt = (left.Irt ?? 0m) + (right.Irt ?? 0m);
        if (left.Irt is null && right.Irt is null)
        {
            irt = exchangeRate is > 0
                ? (left.Usd + right.Usd) * exchangeRate.Value
                : null;
        }

        return new ApiCostEstimate(
            left.Usd + right.Usd,
            irt,
            exchangeRate,
            left.Source == right.Source ? left.Source : "combined");
    }

    public ApiCostEstimate Add(ApiCostEstimate? other) => TryCombine(this, other) ?? this;
}
