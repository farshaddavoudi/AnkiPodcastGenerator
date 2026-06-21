namespace AnkiPodcastGenerator.Configuration;

public sealed class KokoroOptions
{
    public string Command { get; set; } = "kokoro-tts";
    public string WorkingDirectory { get; set; } = string.Empty;
    public string ModelName { get; set; } = "kokoro-v1.0";
    public string Language { get; set; } = "en-us";
    public int TimeoutSeconds { get; set; } = 900;
}
