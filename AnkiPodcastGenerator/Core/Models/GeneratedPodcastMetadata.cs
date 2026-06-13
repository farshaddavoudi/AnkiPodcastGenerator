namespace AnkiPodcastGenerator.Core.Models;

public sealed class GeneratedPodcastMetadata
{
    public string ProfileName { get; set; } = string.Empty;
    public string ProfileSlug { get; set; } = string.Empty;
    public string AnkiQuery { get; set; } = string.Empty;
    public string CardHash { get; set; } = string.Empty;
    public string GenerationSettingsHash { get; set; } = string.Empty;
    public List<long> CardIds { get; set; } = [];
    public int CardCount { get; set; }
    public int TargetMinutes { get; set; }
    public int MaxCards { get; set; }
    public bool MultiSpeaker { get; set; }
    public bool Reused { get; set; }
    public string? ReusedFromMp3Path { get; set; }
    public string CardsJsonPath { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string Mp3Path { get; set; } = string.Empty;
    public string ScriptModel { get; set; } = string.Empty;
    public string TtsModel { get; set; } = string.Empty;
    public string VoiceA { get; set; } = string.Empty;
    public string VoiceB { get; set; } = string.Empty;
    public double TtsSpeed { get; set; }
    public TokenUsage? TokenUsage { get; set; }
    public double GenerationSeconds { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
}
