using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class PodcastGeneratorService(
    IProfileProvider profileProvider,
    IAnkiConnectClient ankiConnectClient,
    ICardSnapshotStore cardSnapshotStore,
    ICardHashService cardHashService,
    IMetadataStore metadataStore,
    IOutputPathService outputPathService,
    IPodcastScriptGenerator scriptGenerator,
    ITextToSpeechProvider textToSpeechProvider,
    IMultiSpeakerTextToSpeechProvider multiSpeakerTextToSpeechProvider,
    IPodcastScriptParser scriptParser,
    IOptions<PodcastOptions> podcastOptions,
    IOptions<AvalAiOptions> avalAiOptions,
    ILogger<PodcastGeneratorService> logger)
    : IPodcastGeneratorService
{
    private const string PromptVersion = "slow-conversation-v2";

    public Task<int> TestAnkiConnectivityAsync(CancellationToken cancellationToken) =>
        ankiConnectClient.GetVersionAsync(cancellationToken);

    public async Task<IReadOnlyList<AnkiCard>> PreviewCardsAsync(
        string profileName,
        int? maxCards,
        CancellationToken cancellationToken)
    {
        var profile = profileProvider.GetRequiredProfile(profileName);
        var options = podcastOptions.Value;
        var ankiQuery = string.IsNullOrWhiteSpace(profile.AnkiQuery) ? "is:due" : profile.AnkiQuery;
        var effectiveMaxCards = maxCards ?? profile.MaxCards ?? options.MaxCards;

        var cardIds = await ankiConnectClient.FindCardsAsync(ankiQuery, cancellationToken);
        var cards = await ankiConnectClient.CardsInfoAsync(cardIds, cancellationToken);

        return OrderCardsForReview(cards)
            .Take(Math.Max(1, effectiveMaxCards))
            .ToArray();
    }

    public async Task<PodcastGenerationResult> GenerateAsync(string profileName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var profile = profileProvider.GetRequiredProfile(profileName);
        var options = podcastOptions.Value;
        var avalAi = avalAiOptions.Value;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var outputPaths = outputPathService.GetPaths(profile, today);

        var targetMinutes = profile.TargetMinutes ?? options.TargetMinutes;
        var maxCards = profile.MaxCards ?? options.MaxCards;
        var multiSpeaker = profile.MultiSpeaker ?? options.MultiSpeaker;
        var ankiQuery = string.IsNullOrWhiteSpace(profile.AnkiQuery) ? "is:due" : profile.AnkiQuery;

        Directory.CreateDirectory(outputPaths.OutputFolder);

        logger.LogInformation(
            "Starting podcast generation for profile {Profile}. Query={AnkiQuery}, TargetMinutes={TargetMinutes}, MaxCards={MaxCards}, MultiSpeaker={MultiSpeaker}",
            profile.Name,
            ankiQuery,
            targetMinutes,
            maxCards,
            multiSpeaker);

        var ankiVersion = await ankiConnectClient.GetVersionAsync(cancellationToken);
        logger.LogInformation("AnkiConnect connectivity OK. Version={AnkiConnectVersion}", ankiVersion);

        var cardIds = await ankiConnectClient.FindCardsAsync(ankiQuery, cancellationToken);
        logger.LogInformation("AnkiConnect returned {CardCount} cards", cardIds.Count);

        var cards = await ankiConnectClient.CardsInfoAsync(cardIds, cancellationToken);
        var orderedCards = OrderCardsForReview(cards)
            .Take(Math.Max(1, maxCards))
            .ToArray();
        logger.LogInformation("Loaded card info for {CardCount} cards; selected {SelectedCardCount}", cards.Count, orderedCards.Length);

        if (orderedCards.Length > 0)
        {
            logger.LogInformation(
                "Selected card IDs in podcast order: {SelectedCardIds}",
                string.Join(", ", orderedCards.Select(card => card.CardId)));
        }

        var snapshot = new CardSnapshot(profile.Name, ankiQuery, DateTimeOffset.UtcNow, orderedCards);
        await cardSnapshotStore.SaveAsync(snapshot, outputPaths.CardsJsonPath, cancellationToken);
        logger.LogInformation("Saved cards JSON to {CardsJsonPath}", outputPaths.CardsJsonPath);

        if (orderedCards.Length == 0)
        {
            return new PodcastGenerationResult(true, false, 0, null, "No cards matched the profile query. No MP3 was generated.");
        }

        var cardHash = cardHashService.ComputeHash(orderedCards);
        var generationSettingsHash = ComputeGenerationSettingsHash(
            ankiQuery,
            targetMinutes,
            maxCards,
            multiSpeaker,
            avalAi.ScriptModel,
            avalAi.TtsModel,
            avalAi.TtsFallbackModel,
            avalAi.VoiceA,
            avalAi.VoiceB,
            avalAi.TtsSpeed);
        var previousMetadata = await metadataStore.LoadAsync(outputPaths, cancellationToken);

        if (options.ReuseIfSameCards &&
            previousMetadata is not null &&
            string.Equals(previousMetadata.CardHash, cardHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previousMetadata.GenerationSettingsHash, generationSettingsHash, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(previousMetadata.Mp3Path))
        {
            await ReusePreviousOutputAsync(previousMetadata, outputPaths, cancellationToken);

            var reusedMetadata = CreateMetadata(
                profile,
                outputPaths,
                ankiQuery,
                orderedCards,
                cardHash,
                generationSettingsHash,
                targetMinutes,
                maxCards,
                multiSpeaker,
                previousMetadata.ScriptModel,
                previousMetadata.TtsModel,
                avalAi.VoiceA,
                avalAi.VoiceB,
                avalAi.TtsSpeed,
                stopwatch.Elapsed,
                previousMetadata.TokenUsage);

            reusedMetadata.Reused = true;
            reusedMetadata.ReusedFromMp3Path = previousMetadata.Mp3Path;

            await metadataStore.SaveAsync(outputPaths, reusedMetadata, cancellationToken);
            logger.LogInformation("Card hash unchanged. Reused MP3 from {PreviousMp3Path}", previousMetadata.Mp3Path);
            logger.LogInformation("Output file: {OutputFile}", outputPaths.Mp3Path);
            logger.LogInformation("Generation duration: {ElapsedSeconds:N2}s", stopwatch.Elapsed.TotalSeconds);

            return new PodcastGenerationResult(true, true, orderedCards.Length, outputPaths.Mp3Path, "Card set unchanged. Reused existing MP3.");
        }

        var scriptResult = await scriptGenerator.GenerateScriptAsync(orderedCards, profile, targetMinutes, cancellationToken);
        await WriteTextFileAsync(outputPaths.ScriptPath, scriptResult.Script, cancellationToken);
        logger.LogInformation("Saved script to {ScriptPath}", outputPaths.ScriptPath);

        if (scriptResult.TokenUsage is not null)
        {
            logger.LogInformation(
                "Token usage: prompt={PromptTokens}, completion={CompletionTokens}, total={TotalTokens}",
                scriptResult.TokenUsage.PromptTokens,
                scriptResult.TokenUsage.CompletionTokens,
                scriptResult.TokenUsage.TotalTokens);
        }

        var ttsResult = await GenerateAudioAsync(scriptResult.Script, multiSpeaker, outputPaths, cancellationToken);
        stopwatch.Stop();

        var metadata = CreateMetadata(
            profile,
            outputPaths,
            ankiQuery,
            orderedCards,
            cardHash,
            generationSettingsHash,
            targetMinutes,
            maxCards,
            multiSpeaker,
            scriptResult.Model,
            ttsResult.Model,
            avalAi.VoiceA,
            avalAi.VoiceB,
            avalAi.TtsSpeed,
            stopwatch.Elapsed,
            scriptResult.TokenUsage);

        await metadataStore.SaveAsync(outputPaths, metadata, cancellationToken);

        logger.LogInformation("Output file: {OutputFile}", outputPaths.Mp3Path);
        logger.LogInformation("Generation duration: {ElapsedSeconds:N2}s", stopwatch.Elapsed.TotalSeconds);

        return new PodcastGenerationResult(true, false, orderedCards.Length, outputPaths.Mp3Path, "Generated podcast MP3.");
    }

    private async Task<TextToSpeechResult> GenerateAudioAsync(
        string script,
        bool multiSpeaker,
        OutputPaths outputPaths,
        CancellationToken cancellationToken)
    {
        var avalAi = avalAiOptions.Value;

        if (!multiSpeaker)
        {
            return await textToSpeechProvider.GenerateMp3Async(script, avalAi.VoiceA, outputPaths.Mp3Path, cancellationToken);
        }

        var segments = scriptParser.Parse(script);
        if (segments.Count == 0)
        {
            return await textToSpeechProvider.GenerateMp3Async(script, avalAi.VoiceA, outputPaths.Mp3Path, cancellationToken);
        }

        return await multiSpeakerTextToSpeechProvider.GenerateMultiSpeakerMp3Async(
            segments,
            avalAi.VoiceA,
            avalAi.VoiceB,
            outputPaths.Mp3Path,
            cancellationToken);
    }

    private static IReadOnlyList<AnkiCard> OrderCardsForReview(IReadOnlyList<AnkiCard> cards) =>
        cards
            .OrderBy(GetReviewOrderBucket)
            .ThenBy(card => card.Due)
            .ThenBy(card => card.CardId)
            .ToArray();

    private static int GetReviewOrderBucket(AnkiCard card) =>
        card.Queue switch
        {
            2 => 0, // review cards; Anki's default reviewOrder=0 is due-date order.
            3 => 1, // day-learning cards.
            1 => 2, // intraday learning cards.
            0 => 3, // new cards if the query includes them.
            _ => 4
        };

    private static async Task ReusePreviousOutputAsync(
        GeneratedPodcastMetadata previousMetadata,
        OutputPaths outputPaths,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPaths.Mp3Path)!);

        if (!SamePath(previousMetadata.Mp3Path, outputPaths.Mp3Path))
        {
            File.Copy(previousMetadata.Mp3Path, outputPaths.Mp3Path, overwrite: true);
        }

        if (!string.IsNullOrWhiteSpace(previousMetadata.ScriptPath) &&
            File.Exists(previousMetadata.ScriptPath) &&
            !SamePath(previousMetadata.ScriptPath, outputPaths.ScriptPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPaths.ScriptPath)!);
            await using var source = File.OpenRead(previousMetadata.ScriptPath);
            await using var target = File.Create(outputPaths.ScriptPath);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static GeneratedPodcastMetadata CreateMetadata(
        PodcastProfile profile,
        OutputPaths outputPaths,
        string ankiQuery,
        IReadOnlyList<AnkiCard> cards,
        string cardHash,
        string generationSettingsHash,
        int targetMinutes,
        int maxCards,
        bool multiSpeaker,
        string scriptModel,
        string ttsModel,
        string voiceA,
        string voiceB,
        double ttsSpeed,
        TimeSpan elapsed,
        TokenUsage? tokenUsage)
    {
        return new GeneratedPodcastMetadata
        {
            ProfileName = profile.Name,
            ProfileSlug = outputPaths.ProfileSlug,
            AnkiQuery = ankiQuery,
            CardHash = cardHash,
            GenerationSettingsHash = generationSettingsHash,
            CardIds = cards.Select(card => card.CardId).ToList(),
            CardCount = cards.Count,
            TargetMinutes = targetMinutes,
            MaxCards = maxCards,
            MultiSpeaker = multiSpeaker,
            Reused = false,
            CardsJsonPath = outputPaths.CardsJsonPath,
            ScriptPath = outputPaths.ScriptPath,
            Mp3Path = outputPaths.Mp3Path,
            ScriptModel = scriptModel,
            TtsModel = ttsModel,
            VoiceA = voiceA,
            VoiceB = voiceB,
            TtsSpeed = ttsSpeed,
            TokenUsage = tokenUsage,
            GenerationSeconds = elapsed.TotalSeconds,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task WriteTextFileAsync(string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string ComputeGenerationSettingsHash(
        string ankiQuery,
        int targetMinutes,
        int maxCards,
        bool multiSpeaker,
        string scriptModel,
        string ttsModel,
        string ttsFallbackModel,
        string voiceA,
        string voiceB,
        double ttsSpeed)
    {
        var value = string.Join(
            "\n",
            PromptVersion,
            ankiQuery,
            targetMinutes,
            maxCards,
            multiSpeaker,
            scriptModel,
            ttsModel,
            ttsFallbackModel,
            voiceA,
            voiceB,
            ttsSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
