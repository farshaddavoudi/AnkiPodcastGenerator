namespace AnkiPodcastGenerator.Configuration;

public sealed class OpenRouterOptions
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Referer { get; set; } = "https://github.com/fdavo/AnkiPodcastGenerator";
    public string Title { get; set; } = "AnkiPodcastGenerator";
}
