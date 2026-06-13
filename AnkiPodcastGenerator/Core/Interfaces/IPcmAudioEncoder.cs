namespace AnkiPodcastGenerator.Core.Interfaces;

public interface IPcmAudioEncoder
{
    Task EncodePcmToMp3Async(
        string pcmPath,
        int sampleRate,
        int channels,
        string outputPath,
        CancellationToken cancellationToken);
}
