namespace AnkiPodcastGenerator.Configuration;

public sealed class GenerationProfile
{
    public string? Description { get; set; }
    public string? ScriptProvider { get; set; }
    public string? TextToSpeechProvider { get; set; }
    public string? ScriptModel { get; set; }
    public string? TtsModel { get; set; }
    public string? TtsFallbackModel { get; set; }
    public string? VoiceA { get; set; }
    public string? VoiceB { get; set; }
    public double? TtsSpeed { get; set; }
    public int? TargetMinutes { get; set; }
    public bool? MultiSpeaker { get; set; }
}
