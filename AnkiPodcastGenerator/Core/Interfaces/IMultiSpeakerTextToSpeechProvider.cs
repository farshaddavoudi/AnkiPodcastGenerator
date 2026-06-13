using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IMultiSpeakerTextToSpeechProvider
{
    Task<TextToSpeechResult> GenerateMultiSpeakerMp3Async(
        IReadOnlyList<PodcastSegment> segments,
        string voiceA,
        string voiceB,
        string outputPath,
        CancellationToken cancellationToken);
}
