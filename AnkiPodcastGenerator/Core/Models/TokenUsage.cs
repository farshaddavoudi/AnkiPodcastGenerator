namespace AnkiPodcastGenerator.Core.Models;

public sealed record TokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
