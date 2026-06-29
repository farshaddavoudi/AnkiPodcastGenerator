using System.Text.Json.Serialization;

namespace AnkiPodcastGenerator.Core.Models;

public sealed class GeneratedPodcastMetadata
{
    public string DeckName { get; set; } = string.Empty;
    public string DeckSlug { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ProfileName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? ProfileSlug { get; set; }

    public string AnkiQuery { get; set; } = string.Empty;
    public string CardHash { get; set; } = string.Empty;
    public string GenerationSettingsHash { get; set; } = string.Empty;
    public List<long> CardIds { get; set; } = [];
    public int CardCount { get; set; }
    public int TargetMinutes { get; set; }
    public int MaxCards { get; set; }
    public int? CardsPerPodcast { get; set; }
    public int BundleIndex { get; set; }
    public int TotalBundles { get; set; }
    public bool MultiSpeaker { get; set; }
    public bool Reused { get; set; }
    public string? ReusedFromMp3Path { get; set; }
    public string CardsJsonPath { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string TtsScriptPath { get; set; } = string.Empty;
    public string PronunciationMapPath { get; set; } = string.Empty;
    public string Mp3Path { get; set; } = string.Empty;
    public string ScriptProvider { get; set; } = string.Empty;
    public string TextToSpeechProvider { get; set; } = string.Empty;
    public string ScriptModel { get; set; } = string.Empty;
    public string TtsModel { get; set; } = string.Empty;
    public string VoiceA { get; set; } = string.Empty;
    public string VoiceB { get; set; } = string.Empty;
    public double TtsSpeed { get; set; }
    public TokenUsage? TokenUsage { get; set; }
    public decimal? ScriptCostUsd { get; set; }
    public decimal? ScriptCostIrt { get; set; }
    public decimal? TtsCostUsd { get; set; }
    public decimal? TtsCostIrt { get; set; }
    public decimal? TotalCostUsd { get; set; }
    public decimal? TotalCostIrt { get; set; }
    public string? CostSource { get; set; }
    public double GenerationSeconds { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
}
