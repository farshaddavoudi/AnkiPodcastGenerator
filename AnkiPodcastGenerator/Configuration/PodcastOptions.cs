namespace AnkiPodcastGenerator.Configuration;

public sealed class PodcastOptions
{
    public string OutputFolder { get; set; } = @"D:\AnkiPodcasts";
    public int TargetMinutes { get; set; } = 30;
    public int MaxCards { get; set; } = 40;
    public bool ReuseIfSameCards { get; set; } = true;
    public bool MultiSpeaker { get; set; }
}
