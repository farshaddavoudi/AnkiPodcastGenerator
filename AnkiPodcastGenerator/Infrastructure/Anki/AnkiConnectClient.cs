using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Infrastructure.Anki;

public sealed class AnkiConnectClient : IAnkiConnectClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnkiConnectClient> _logger;

    public AnkiConnectClient(
        HttpClient httpClient,
        IOptions<AnkiOptions> options,
        ILogger<AnkiConnectClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public Task<int> GetVersionAsync(CancellationToken cancellationToken) =>
        InvokeAsync<int>("version", null, cancellationToken);

    public Task SyncAsync(CancellationToken cancellationToken) =>
        InvokeVoidAsync("sync", null, cancellationToken);

    public Task<IReadOnlyList<long>> FindCardsAsync(string query, CancellationToken cancellationToken) =>
        InvokeAsync<IReadOnlyList<long>>("findCards", new { query }, cancellationToken);

    public async Task<IReadOnlyList<AnkiCard>> CardsInfoAsync(IEnumerable<long> cardIds, CancellationToken cancellationToken)
    {
        var ids = cardIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var cardsInfo = new List<CardInfoDto>(ids.Length);
        foreach (var chunk in ids.Chunk(100))
        {
            var chunkInfo = await InvokeAsync<IReadOnlyList<CardInfoDto>>("cardsInfo", new { cards = chunk }, cancellationToken);
            cardsInfo.AddRange(chunkInfo);
        }

        return cardsInfo
            .Select(card => new AnkiCard(
                card.CardId,
                card.Note,
                card.DeckName,
                card.Type,
                card.Queue,
                card.Due,
                ExtractField(card.Fields, "Front", index: 0),
                ExtractField(card.Fields, "Back", index: 1),
                card.Tags))
            .ToArray();
    }

    private async Task<T> InvokeAsync<T>(string action, object? parameters, CancellationToken cancellationToken)
    {
        using var document = await InvokeRawAsync(action, parameters, cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("result", out var resultElement) ||
            resultElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"AnkiConnect action '{action}' returned no result. Body: {root.GetRawText()}");
        }

        var result = resultElement.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"AnkiConnect action '{action}' returned an unreadable result.");

        _logger.LogDebug("AnkiConnect action {Action} completed", action);
        return result;
    }

    private async Task InvokeVoidAsync(string action, object? parameters, CancellationToken cancellationToken)
    {
        using var document = await InvokeRawAsync(action, parameters, cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("result", out _))
        {
            throw new InvalidOperationException($"AnkiConnect action '{action}' returned no result field. Body: {root.GetRawText()}");
        }

        _logger.LogDebug("AnkiConnect action {Action} completed", action);
    }

    private async Task<JsonDocument> InvokeRawAsync(string action, object? parameters, CancellationToken cancellationToken)
    {
        object request = parameters is null
            ? new { action, version = 6 }
            : new { action, version = 6, @params = parameters };
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AnkiConnect HTTP request failed with status {(int)response.StatusCode}. Body: {responseBody}");
        }

        var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorElement) &&
            errorElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            var error = errorElement.GetString();
            if (!string.IsNullOrWhiteSpace(error))
            {
                document.Dispose();
                throw new InvalidOperationException($"AnkiConnect action '{action}' failed: {error}");
            }
        }

        return document;
    }

    private static string ExtractField(Dictionary<string, AnkiFieldDto> fields, string preferredName, int index)
    {
        var preferred = fields.FirstOrDefault(field =>
            string.Equals(field.Key, preferredName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(preferred.Key))
        {
            return HtmlToPlainText(preferred.Value.Value);
        }

        var byIndex = fields
            .OrderBy(field => field.Value.Order)
            .ElementAtOrDefault(index);

        return string.IsNullOrWhiteSpace(byIndex.Key)
            ? string.Empty
            : HtmlToPlainText(byIndex.Value.Value);
    }

    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withBreaks = Regex.Replace(html, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        withBreaks = Regex.Replace(withBreaks, @"</\s*(div|p|li|tr|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return string.Join(
            "\n",
            decoded
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
    }

    public sealed class AnkiConnectResponse<T>
    {
        [JsonPropertyName("result")]
        public T? Result { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public sealed class CardInfoDto
    {
        [JsonPropertyName("cardId")]
        public long CardId { get; set; }

        [JsonPropertyName("note")]
        public long Note { get; set; }

        [JsonPropertyName("deckName")]
        public string DeckName { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("queue")]
        public int Queue { get; set; }

        [JsonPropertyName("due")]
        public long Due { get; set; }

        [JsonPropertyName("fields")]
        public Dictionary<string, AnkiFieldDto> Fields { get; set; } = [];

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = [];
    }

    public sealed class AnkiFieldDto
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("order")]
        public int Order { get; set; }
    }
}
