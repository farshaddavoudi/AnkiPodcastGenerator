namespace AnkiPodcastGenerator.Configuration;

public sealed class TtsNormalizerOptions
{
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = "StaticThenLlm";
    public string? LlmModel { get; set; }
}
