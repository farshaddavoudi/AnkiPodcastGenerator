using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface ITextToSpeechProvider
{
    Task<TextToSpeechResult> GenerateMp3Async(
        string text,
        string voice,
        string outputPath,
        CancellationToken cancellationToken);
}
