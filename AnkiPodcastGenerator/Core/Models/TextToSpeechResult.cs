namespace AnkiPodcastGenerator.Core.Models;

public sealed record TextToSpeechResult(
    string Model,
    string Voice,
    string OutputPath,
    long BytesWritten);
