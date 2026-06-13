namespace AnkiPodcastGenerator.Core.Models;

public sealed record ScriptGenerationResult(
    string Script,
    string Model,
    TokenUsage? TokenUsage);
