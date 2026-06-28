namespace AnkiPodcastGenerator.Configuration;

public sealed class PodcastOptions
{
    public string OutputFolder { get; set; } = @"D:\AnkiPodcasts";
    public string GenerationProfile { get; set; } = string.Empty;
    public string ScriptProvider { get; set; } = "AvalAi";
    public string TextToSpeechProvider { get; set; } = "AvalAi";
    public int TargetMinutes { get; set; } = 30;
    public bool ReuseIfSameCards { get; set; } = true;
    public bool MultiSpeaker { get; set; }
    public string? CustomPrompt { get; set; }
}
