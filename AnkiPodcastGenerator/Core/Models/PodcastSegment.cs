namespace AnkiPodcastGenerator.Core.Models;

public sealed record PodcastSegment(char Speaker, string Text, int PauseAfterSeconds = 0);
