namespace AnkiPodcastGenerator.Configuration;

public sealed class AvalAiOptions
{
    public string BaseUrl { get; set; } = "https://api.avalai.ir";
    public string ApiKey { get; set; } = string.Empty;
    public string ScriptModel { get; set; } = "claude-sonnet-4-5";
    public string TtsModel { get; set; } = "gemini-2.5-flash-tts";
    public string TtsFallbackModel { get; set; } = string.Empty;
    public string VoiceA { get; set; } = "Kore";
    public string VoiceB { get; set; } = "Algenib";
    public double TtsSpeed { get; set; } = 1.0;
}
