namespace AnkiPodcastGenerator.Configuration;

public sealed class AnkiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8765";
    public bool SyncBeforeQuery { get; set; } = true;
}
