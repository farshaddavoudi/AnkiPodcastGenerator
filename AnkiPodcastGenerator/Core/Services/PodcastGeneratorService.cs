using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class PodcastGeneratorService(
    IDeckProvider deckProvider,
    IAnkiConnectClient ankiConnectClient,
    ICardSnapshotStore cardSnapshotStore,
    ICardHashService cardHashService,
    IMetadataStore metadataStore,
    IOutputPathService outputPathService,
    IPodcastScriptGenerator scriptGenerator,
    ITextToSpeechProvider textToSpeechProvider,
    IMultiSpeakerTextToSpeechProvider multiSpeakerTextToSpeechProvider,
    IPodcastScriptParser scriptParser,
    IPodcastTtsTextNormalizer ttsTextNormalizer,
    ITtsScriptNormalizer ttsScriptNormalizer,
    IAudioCombiner audioCombiner,
    IOptions<AnkiOptions> ankiOptions,
    IOptions<PodcastOptions> podcastOptions,
    IOptions<AvalAiOptions> avalAiOptions,
    IOptions<KokoroOptions> kokoroOptions,
    IOptions<TtsNormalizerOptions> ttsNormalizerOptions,
    ActiveGenerationProfile activeGenerationProfile,
    ILogger<PodcastGeneratorService> logger)
    : IPodcastGeneratorService
{
    private const string PromptVersion = "topic-grouped-conversation-v5";
    private const string TtsTextNormalizationVersion = "plain-spoken-v1";
    private const string TtsScriptNormalizationVersion = "static-llm-v1";
    private static readonly JsonSerializerOptions PronunciationMapJsonOptions = new()
    {
        WriteIndented = true
    };

    public Task<int> TestAnkiConnectivityAsync(CancellationToken cancellationToken) =>
        ankiConnectClient.GetVersionAsync(cancellationToken);

    public async Task<IReadOnlyList<AnkiCard>> PreviewCardsAsync(
        string deckName,
        int? maxCards,
        CancellationToken cancellationToken)
    {
        var deck = deckProvider.GetRequiredDeck(deckName);
        if (deck.MaxCards == 0 && maxCards is null)
        {
            return [];
        }

        var ankiQuery = BuildDueCardsQuery(deck);
        var effectiveMaxCards = maxCards ?? deck.MaxCards;

        await SyncBeforeQueryIfConfiguredAsync(cancellationToken);

        var cardIds = await ankiConnectClient.FindCardsAsync(ankiQuery, cancellationToken);
        var cards = await ankiConnectClient.CardsInfoAsync(cardIds, cancellationToken);

        return OrderCardsForReview(cards)
            .Take(effectiveMaxCards)
            .ToArray();
    }

    public async Task<PodcastGenerationResult> GenerateAsync(string deckName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var deck = deckProvider.GetRequiredDeck(deckName);
        if (deck.MaxCards == 0)
        {
            stopwatch.Stop();
            logger.LogInformation("Deck {DeckName} is disabled because MaxCards=0. Skipping generation.", deck.DeckName);
            return new PodcastGenerationResult(
                true,
                false,
                0,
                null,
                $"Deck '{deck.DeckName}' is disabled (MaxCards=0). No MP3 was generated.");
        }

        var options = podcastOptions.Value;
        var avalAi = avalAiOptions.Value;
        var kokoro = kokoroOptions.Value;
        var ttsNormalizer = ttsNormalizerOptions.Value;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var outputPaths = outputPathService.GetPaths(deck, today);

        var targetMinutes = deck.TargetMinutes ?? options.TargetMinutes;
        var maxCards = deck.MaxCards;
        var multiSpeaker = deck.MultiSpeaker ?? options.MultiSpeaker;
        var ankiQuery = BuildDueCardsQuery(deck);

        Directory.CreateDirectory(outputPaths.OutputFolder);

        logger.LogInformation(
            "Starting podcast generation for deck {DeckName}. Query={AnkiQuery}, GenerationProfile={GenerationProfile}, ScriptProvider={ScriptProvider}, TtsProvider={TtsProvider}, TargetMinutes={TargetMinutes}, MaxCards={MaxCards}, CardsPerPodcast={CardsPerPodcast}, MultiSpeaker={MultiSpeaker}",
            deck.DeckName,
            ankiQuery,
            string.IsNullOrWhiteSpace(options.GenerationProfile) ? "(none)" : options.GenerationProfile,
            options.ScriptProvider,
            options.TextToSpeechProvider,
            targetMinutes,
            maxCards,
            deck.CardsPerPodcast,
            multiSpeaker);

        var ankiVersion = await ankiConnectClient.GetVersionAsync(cancellationToken);
        logger.LogInformation("AnkiConnect connectivity OK. Version={AnkiConnectVersion}", ankiVersion);

        await SyncBeforeQueryIfConfiguredAsync(cancellationToken);

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

        var snapshot = new CardSnapshot(deck.DeckName, ankiQuery, DateTimeOffset.UtcNow, orderedCards);
        await cardSnapshotStore.SaveAsync(snapshot, outputPaths.CardsJsonPath, cancellationToken);
        logger.LogInformation("Saved cards JSON to {CardsJsonPath}", outputPaths.CardsJsonPath);

        if (orderedCards.Length == 0)
        {
            return new PodcastGenerationResult(true, false, 0, null, "No cards matched the deck query. No MP3 was generated.");
        }

        var generationSettingsHash = ComputeGenerationSettingsHash(
            ankiQuery,
            targetMinutes,
            maxCards,
            deck.CardsPerPodcast,
            multiSpeaker,
            options.ScriptProvider,
            options.TextToSpeechProvider,
            BuildTextToSpeechProviderSettings(options.TextToSpeechProvider, kokoro),
            BuildTtsNormalizerSettings(ttsNormalizer, avalAi.ScriptModel),
            avalAi.ScriptModel,
            avalAi.TtsModel,
            avalAi.TtsFallbackModel,
            avalAi.VoiceA,
            avalAi.VoiceB,
            avalAi.TtsSpeed);

        var cardsPerPodcast = deck.CardsPerPodcast > 0 ? deck.CardsPerPodcast.Value : orderedCards.Length;
        var totalBundles = Math.Max(1, (int)Math.Ceiling((double)orderedCards.Length / cardsPerPodcast));

        if (totalBundles > 1)
        {
            logger.LogInformation(
                "Splitting {TotalCards} cards into {TotalBundles} bundles of up to {CardsPerPodcast} cards each.",
                orderedCards.Length,
                totalBundles,
                cardsPerPodcast);
        }

        var generatedPaths = new List<string>();
        var anySuccess = false;
        var anyReused = false;
        var lastMessage = string.Empty;
        var totalCards = orderedCards.Length;

        for (var bundleIndex = 0; bundleIndex < totalBundles; bundleIndex++)
        {
            var bundleCards = orderedCards
                .Skip(bundleIndex * cardsPerPodcast)
                .Take(cardsPerPodcast)
                .ToArray();

            var bundleOutputPaths = outputPathService.GetPaths(deck, today, bundleIndex + 1, totalBundles);

            var bundleTargetMinutes = totalBundles > 1
                ? Math.Max(1, (int)Math.Round(targetMinutes * (double)bundleCards.Length / orderedCards.Length))
                : targetMinutes;

            var bundleResult = await GenerateFromCardsAsync(
                deck,
                bundleOutputPaths,
                ankiQuery,
                bundleCards,
                bundleTargetMinutes,
                multiSpeaker,
                generationSettingsHash,
                stopwatch,
                cancellationToken);

            if (bundleResult.Success) anySuccess = true;
            if (bundleResult.Reused) anyReused = true;
            if (bundleResult.Mp3Path is not null) generatedPaths.Add(bundleResult.Mp3Path);
            lastMessage = bundleResult.Message;

            logger.LogInformation("Output file: {OutputFile}", bundleOutputPaths.Mp3Path);
        }

        stopwatch.Stop();
        logger.LogInformation("Generation duration: {ElapsedSeconds:N2}s", stopwatch.Elapsed.TotalSeconds);

        return new PodcastGenerationResult(
            anySuccess,
            anyReused,
            totalCards,
            generatedPaths.FirstOrDefault(),
            lastMessage,
            generatedPaths.Count > 1 ? generatedPaths : null);
    }

    private async Task<PodcastGenerationResult> GenerateFromCardsAsync(
        PodcastDeck deck,
        OutputPaths outputPaths,
        string ankiQuery,
        IReadOnlyList<AnkiCard> bundleCards,
        int targetMinutes,
        bool multiSpeaker,
        string generationSettingsHash,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var avalAi = avalAiOptions.Value;
        var options = podcastOptions.Value;

        if (bundleCards.Count == 0)
        {
            return new PodcastGenerationResult(true, false, 0, null, "No cards in this bundle.");
        }

        var snapshot = new CardSnapshot(deck.DeckName, ankiQuery, DateTimeOffset.UtcNow, bundleCards);
        await cardSnapshotStore.SaveAsync(snapshot, outputPaths.CardsJsonPath, cancellationToken);
        logger.LogInformation("Saved cards JSON to {CardsJsonPath}", outputPaths.CardsJsonPath);

        var cardHash = cardHashService.ComputeHash(bundleCards);

        GeneratedPodcastMetadata? previousMetadata = null;
        if (options.ReuseIfSameCards)
        {
            previousMetadata = await metadataStore.FindLatestAsync(
                outputPaths,
                generationSettingsHash,
                cancellationToken);
        }

        if (previousMetadata is not null &&
            string.Equals(previousMetadata.CardHash, cardHash, StringComparison.OrdinalIgnoreCase))
        {
            await ReusePreviousOutputAsync(previousMetadata, outputPaths, cancellationToken);

            var reusedMetadata = CreateMetadata(
                deck,
                outputPaths,
                ankiQuery,
                bundleCards,
                cardHash,
                generationSettingsHash,
                targetMinutes,
                bundleCards.Count,
                multiSpeaker,
                activeGenerationProfile.IsConfigured ? activeGenerationProfile.Name : null,
                activeGenerationProfile.Slug,
                options.ScriptProvider,
                options.TextToSpeechProvider,
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
            logger.LogInformation(
                "Card set and generation settings unchanged. Reused MP3 from {PreviousMp3Path}. Original generation at {GeneratedAtUtc:u}",
                previousMetadata.Mp3Path,
                previousMetadata.GeneratedAtUtc);
            logger.LogInformation(
                "Generation cost: $0.00 USD / 0 toman (reused cached MP3; no new API calls)");

            return new PodcastGenerationResult(true, true, bundleCards.Count, outputPaths.Mp3Path, "Card set unchanged. Reused existing MP3.");
        }

        if (options.ReuseIfSameCards && previousMetadata is not null)
        {
            var incrementalCards = await TryGetIncrementalCardsAsync(
                bundleCards,
                previousMetadata,
                cancellationToken);

            if (incrementalCards is { Count: > 0 })
            {
                return await ExtendPreviousPodcastAsync(
                    deck,
                    outputPaths,
                    ankiQuery,
                    bundleCards,
                    cardHash,
                    generationSettingsHash,
                    targetMinutes,
                    bundleCards.Count,
                    multiSpeaker,
                    previousMetadata,
                    incrementalCards,
                    stopwatch,
                    cancellationToken);
            }
        }

        if (options.ReuseIfSameCards)
        {
            logger.LogInformation(
                "No reusable podcast found for deck {DeckName} bundle {BundleIndex} of {TotalBundles}. Selected {SelectedCardCount} due cards with hash {CardHash}. Generating new audio.",
                deck.DeckName,
                outputPaths.BundleIndex,
                outputPaths.TotalBundles,
                bundleCards.Count,
                cardHash);
        }

        var scriptResult = await scriptGenerator.GenerateScriptAsync(bundleCards, deck, targetMinutes, cancellationToken);
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

        var normalizationResult = await NormalizeAndSaveTtsArtifactsAsync(
            scriptResult.Script,
            outputPaths,
            cancellationToken);

        var ttsResult = await GenerateAudioAsync(normalizationResult.TtsScript, multiSpeaker, outputPaths.Mp3Path, cancellationToken);

        var totalCost = ApiCostEstimate.TryCombine(scriptResult.EstimatedCost, ttsResult.EstimatedCost);
        logger.LogInformation(
            "Generation cost: {CostSummary}",
            GenerationCostFormatter.FormatSummary(scriptResult.EstimatedCost, ttsResult.EstimatedCost, totalCost));

        var metadata = CreateMetadata(
            deck,
            outputPaths,
            ankiQuery,
            bundleCards,
            cardHash,
            generationSettingsHash,
            targetMinutes,
            bundleCards.Count,
            multiSpeaker,
            activeGenerationProfile.IsConfigured ? activeGenerationProfile.Name : null,
            activeGenerationProfile.Slug,
            options.ScriptProvider,
            options.TextToSpeechProvider,
            scriptResult.Model,
            ttsResult.Model,
            avalAi.VoiceA,
            avalAi.VoiceB,
            avalAi.TtsSpeed,
            stopwatch.Elapsed,
            scriptResult.TokenUsage,
            scriptResult.EstimatedCost,
            ttsResult.EstimatedCost);

        await metadataStore.SaveAsync(outputPaths, metadata, cancellationToken);

        return new PodcastGenerationResult(true, false, bundleCards.Count, outputPaths.Mp3Path, "Generated podcast MP3.");
    }

    private async Task<PodcastGenerationResult> ExtendPreviousPodcastAsync(
        PodcastDeck deck,
        OutputPaths outputPaths,
        string ankiQuery,
        IReadOnlyList<AnkiCard> orderedCards,
        string cardHash,
        string generationSettingsHash,
        int targetMinutes,
        int maxCards,
        bool multiSpeaker,
        GeneratedPodcastMetadata previousMetadata,
        IReadOnlyList<AnkiCard> incrementalCards,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var avalAi = avalAiOptions.Value;
        var options = podcastOptions.Value;

        logger.LogInformation(
            "Extending podcast for deck {DeckName}. Reusing audio for {ReusedCardCount} previously generated cards and generating {NewCardCount} new cards.",
            deck.DeckName,
            previousMetadata.CardIds.Count,
            incrementalCards.Count);

        var incrementalTargetMinutes = Math.Max(
            1,
            (int)Math.Round(targetMinutes * (double)incrementalCards.Count / orderedCards.Count));

        var scriptResult = await scriptGenerator.GenerateScriptAsync(
            incrementalCards,
            deck,
            incrementalTargetMinutes,
            cancellationToken);

        var combinedScript = await BuildCombinedScriptAsync(previousMetadata, scriptResult.Script, cancellationToken);
        await WriteTextFileAsync(outputPaths.ScriptPath, combinedScript, cancellationToken);
        logger.LogInformation("Saved combined script to {ScriptPath}", outputPaths.ScriptPath);

        if (scriptResult.TokenUsage is not null)
        {
            logger.LogInformation(
                "Token usage: prompt={PromptTokens}, completion={CompletionTokens}, total={TotalTokens}",
                scriptResult.TokenUsage.PromptTokens,
                scriptResult.TokenUsage.CompletionTokens,
                scriptResult.TokenUsage.TotalTokens);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "AnkiPodcastGenerator");
        Directory.CreateDirectory(tempDirectory);
        var incrementalMp3Path = Path.Combine(tempDirectory, $"increment-{Guid.NewGuid():N}.mp3");

        try
        {
            var normalizationResult = await ttsScriptNormalizer.NormalizeScriptAsync(scriptResult.Script, cancellationToken);
            var combinedTtsScript = await BuildCombinedTtsScriptAsync(
                previousMetadata,
                normalizationResult.TtsScript,
                cancellationToken);
            var combinedPronunciationMap = await BuildCombinedPronunciationMapAsync(
                previousMetadata,
                normalizationResult.PronunciationMap,
                cancellationToken);
            await SaveTtsArtifactsAsync(
                outputPaths,
                combinedTtsScript,
                combinedPronunciationMap,
                cancellationToken);

            var ttsResult = await GenerateAudioAsync(
                normalizationResult.TtsScript,
                multiSpeaker,
                incrementalMp3Path,
                cancellationToken);

            await audioCombiner.CombineMp3Async(
                [previousMetadata.Mp3Path, incrementalMp3Path],
                outputPaths.Mp3Path,
                cancellationToken);

            stopwatch.Stop();

            var totalCost = ApiCostEstimate.TryCombine(scriptResult.EstimatedCost, ttsResult.EstimatedCost);
            logger.LogInformation(
                "Incremental generation cost: {CostSummary}",
                GenerationCostFormatter.FormatSummary(scriptResult.EstimatedCost, ttsResult.EstimatedCost, totalCost));

            var metadata = CreateMetadata(
                deck,
                outputPaths,
                ankiQuery,
                orderedCards,
                cardHash,
                generationSettingsHash,
                targetMinutes,
                maxCards,
                multiSpeaker,
                activeGenerationProfile.IsConfigured ? activeGenerationProfile.Name : null,
                activeGenerationProfile.Slug,
                options.ScriptProvider,
                options.TextToSpeechProvider,
                scriptResult.Model,
                ttsResult.Model,
                avalAi.VoiceA,
                avalAi.VoiceB,
                avalAi.TtsSpeed,
                stopwatch.Elapsed,
                scriptResult.TokenUsage,
                scriptResult.EstimatedCost,
                ttsResult.EstimatedCost);

            metadata.ReusedFromMp3Path = previousMetadata.Mp3Path;

            await metadataStore.SaveAsync(outputPaths, metadata, cancellationToken);

            logger.LogInformation(
                "Extended MP3 from {PreviousMp3Path} with {NewCardCount} new cards.",
                previousMetadata.Mp3Path,
                incrementalCards.Count);
            logger.LogInformation("Output file: {OutputFile}", outputPaths.Mp3Path);
            logger.LogInformation("Generation duration: {ElapsedSeconds:N2}s", stopwatch.Elapsed.TotalSeconds);

            return new PodcastGenerationResult(
                true,
                false,
                orderedCards.Count,
                outputPaths.Mp3Path,
                $"Extended podcast with {incrementalCards.Count} new card(s).");
        }
        finally
        {
            TryDeleteFile(incrementalMp3Path);
        }
    }

    private async Task<TextToSpeechResult> GenerateAudioAsync(
        string script,
        bool multiSpeaker,
        string outputMp3Path,
        CancellationToken cancellationToken)
    {
        var avalAi = avalAiOptions.Value;

        if (!multiSpeaker)
        {
            var normalizedScript = ttsTextNormalizer.Normalize(script);
            return await textToSpeechProvider.GenerateMp3Async(normalizedScript, avalAi.VoiceA, outputMp3Path, cancellationToken);
        }

        var segments = scriptParser.Parse(script);
        if (segments.Count == 0)
        {
            var normalizedScript = ttsTextNormalizer.Normalize(script);
            return await textToSpeechProvider.GenerateMp3Async(normalizedScript, avalAi.VoiceA, outputMp3Path, cancellationToken);
        }

        var normalizedSegments = segments
            .Select(segment => segment.PauseAfterSeconds > 0
                ? segment
                : segment with { Text = ttsTextNormalizer.Normalize(segment.Text) })
            .Where(segment => segment.PauseAfterSeconds > 0 || !string.IsNullOrWhiteSpace(segment.Text))
            .ToArray();

        return await multiSpeakerTextToSpeechProvider.GenerateMultiSpeakerMp3Async(
            normalizedSegments,
            avalAi.VoiceA,
            avalAi.VoiceB,
            outputMp3Path,
            cancellationToken);
    }

    private async Task<TtsNormalizationResult> NormalizeAndSaveTtsArtifactsAsync(
        string script,
        OutputPaths outputPaths,
        CancellationToken cancellationToken)
    {
        var normalizationResult = await ttsScriptNormalizer.NormalizeScriptAsync(script, cancellationToken);
        await SaveTtsArtifactsAsync(
            outputPaths,
            normalizationResult.TtsScript,
            normalizationResult.PronunciationMap,
            cancellationToken);

        return normalizationResult;
    }

    private async Task SaveTtsArtifactsAsync(
        OutputPaths outputPaths,
        string ttsScript,
        IReadOnlyList<PronunciationMapItem> pronunciationMap,
        CancellationToken cancellationToken)
    {
        await WriteTextFileAsync(outputPaths.TtsScriptPath, ttsScript, cancellationToken);
        await WriteTextFileAsync(
            outputPaths.PronunciationMapPath,
            JsonSerializer.Serialize(pronunciationMap, PronunciationMapJsonOptions),
            cancellationToken);
        logger.LogInformation("Saved TTS-normalized script to {TtsScriptPath}", outputPaths.TtsScriptPath);
        logger.LogInformation("Saved pronunciation map to {PronunciationMapPath}", outputPaths.PronunciationMapPath);
    }

    private async Task<IReadOnlyList<AnkiCard>?> TryGetIncrementalCardsAsync(
        IReadOnlyList<AnkiCard> orderedCards,
        GeneratedPodcastMetadata previousMetadata,
        CancellationToken cancellationToken)
    {
        if (previousMetadata.CardIds.Count == 0 ||
            previousMetadata.CardIds.Count >= orderedCards.Count)
        {
            return null;
        }

        var currentIds = orderedCards.Select(card => card.CardId).ToArray();
        for (var index = 0; index < previousMetadata.CardIds.Count; index++)
        {
            if (currentIds[index] != previousMetadata.CardIds[index])
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(previousMetadata.CardsJsonPath) ||
            !File.Exists(previousMetadata.CardsJsonPath))
        {
            return null;
        }

        var previousSnapshot = await cardSnapshotStore.LoadAsync(previousMetadata.CardsJsonPath, cancellationToken);
        if (previousSnapshot is null || previousSnapshot.Cards.Count != previousMetadata.CardIds.Count)
        {
            return null;
        }

        for (var index = 0; index < previousSnapshot.Cards.Count; index++)
        {
            var previousCard = previousSnapshot.Cards[index];
            var currentCard = orderedCards[index];

            if (previousCard.CardId != currentCard.CardId ||
                !string.Equals(previousCard.Front, currentCard.Front, StringComparison.Ordinal) ||
                !string.Equals(previousCard.Back, currentCard.Back, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return orderedCards
            .Skip(previousMetadata.CardIds.Count)
            .ToArray();
    }

    private static async Task<string> BuildCombinedScriptAsync(
        GeneratedPodcastMetadata previousMetadata,
        string newScript,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previousMetadata.ScriptPath) || !File.Exists(previousMetadata.ScriptPath))
        {
            return newScript;
        }

        var previousScript = await File.ReadAllTextAsync(previousMetadata.ScriptPath, cancellationToken);
        return $"{previousScript.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{newScript.TrimStart()}";
    }

    private static Task<string> BuildCombinedTtsScriptAsync(
        GeneratedPodcastMetadata previousMetadata,
        string newTtsScript,
        CancellationToken cancellationToken) =>
        BuildCombinedTextAsync(
            previousMetadata.TtsScriptPath,
            previousMetadata.ScriptPath,
            newTtsScript,
            cancellationToken);

    private static async Task<string> BuildCombinedTextAsync(
        string? preferredPreviousPath,
        string? fallbackPreviousPath,
        string newText,
        CancellationToken cancellationToken)
    {
        foreach (var previousPath in new[] { preferredPreviousPath, fallbackPreviousPath })
        {
            if (string.IsNullOrWhiteSpace(previousPath) || !File.Exists(previousPath))
            {
                continue;
            }

            var previousText = await File.ReadAllTextAsync(previousPath, cancellationToken);
            return $"{previousText.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{newText.TrimStart()}";
        }

        return newText;
    }

    private static async Task<IReadOnlyList<PronunciationMapItem>> BuildCombinedPronunciationMapAsync(
        GeneratedPodcastMetadata previousMetadata,
        IReadOnlyList<PronunciationMapItem> newPronunciationMap,
        CancellationToken cancellationToken)
    {
        var combinedMap = new List<PronunciationMapItem>();

        if (!string.IsNullOrWhiteSpace(previousMetadata.PronunciationMapPath) &&
            File.Exists(previousMetadata.PronunciationMapPath))
        {
            try
            {
                var previousMapJson = await File.ReadAllTextAsync(previousMetadata.PronunciationMapPath, cancellationToken);
                var previousMap = JsonSerializer.Deserialize<List<PronunciationMapItem>>(
                    previousMapJson,
                    PronunciationMapJsonOptions);

                if (previousMap is not null)
                {
                    combinedMap.AddRange(previousMap);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }
        }

        combinedMap.AddRange(newPronunciationMap);
        return combinedMap;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task SyncBeforeQueryIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (!ankiOptions.Value.SyncBeforeQuery)
        {
            logger.LogDebug("Skipping Anki sync before card query because Anki:SyncBeforeQuery is disabled");
            return;
        }

        logger.LogInformation("Synchronizing Anki before querying cards");
        await ankiConnectClient.SyncAsync(cancellationToken);
        logger.LogInformation("Anki sync completed");
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

        if (!string.IsNullOrWhiteSpace(previousMetadata.TtsScriptPath) &&
            File.Exists(previousMetadata.TtsScriptPath) &&
            !SamePath(previousMetadata.TtsScriptPath, outputPaths.TtsScriptPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPaths.TtsScriptPath)!);
            await using var source = File.OpenRead(previousMetadata.TtsScriptPath);
            await using var target = File.Create(outputPaths.TtsScriptPath);
            await source.CopyToAsync(target, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(previousMetadata.PronunciationMapPath) &&
            File.Exists(previousMetadata.PronunciationMapPath) &&
            !SamePath(previousMetadata.PronunciationMapPath, outputPaths.PronunciationMapPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPaths.PronunciationMapPath)!);
            await using var source = File.OpenRead(previousMetadata.PronunciationMapPath);
            await using var target = File.Create(outputPaths.PronunciationMapPath);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static GeneratedPodcastMetadata CreateMetadata(
        PodcastDeck deck,
        OutputPaths outputPaths,
        string ankiQuery,
        IReadOnlyList<AnkiCard> cards,
        string cardHash,
        string generationSettingsHash,
        int targetMinutes,
        int maxCards,
        bool multiSpeaker,
        string? profileName,
        string? profileSlug,
        string scriptProvider,
        string textToSpeechProvider,
        string scriptModel,
        string ttsModel,
        string voiceA,
        string voiceB,
        double ttsSpeed,
        TimeSpan elapsed,
        TokenUsage? tokenUsage,
        ApiCostEstimate? scriptCost = null,
        ApiCostEstimate? ttsCost = null)
    {
        var metadata = new GeneratedPodcastMetadata
        {
            DeckName = deck.DeckName,
            DeckSlug = outputPaths.DeckSlug,
            ProfileName = profileName,
            ProfileSlug = profileSlug,
            AnkiQuery = ankiQuery,
            CardHash = cardHash,
            GenerationSettingsHash = generationSettingsHash,
            CardIds = cards.Select(card => card.CardId).ToList(),
            CardCount = cards.Count,
            TargetMinutes = targetMinutes,
            MaxCards = maxCards,
            CardsPerPodcast = deck.CardsPerPodcast,
            BundleIndex = outputPaths.BundleIndex,
            TotalBundles = outputPaths.TotalBundles,
            MultiSpeaker = multiSpeaker,
            Reused = false,
            CardsJsonPath = outputPaths.CardsJsonPath,
            ScriptPath = outputPaths.ScriptPath,
            TtsScriptPath = outputPaths.TtsScriptPath,
            PronunciationMapPath = outputPaths.PronunciationMapPath,
            Mp3Path = outputPaths.Mp3Path,
            ScriptProvider = scriptProvider,
            TextToSpeechProvider = textToSpeechProvider,
            ScriptModel = scriptModel,
            TtsModel = ttsModel,
            VoiceA = voiceA,
            VoiceB = voiceB,
            TtsSpeed = ttsSpeed,
            TokenUsage = tokenUsage,
            GenerationSeconds = elapsed.TotalSeconds,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

        ApplyCost(metadata, scriptCost, ttsCost);
        return metadata;
    }

    private static void ApplyCost(
        GeneratedPodcastMetadata metadata,
        ApiCostEstimate? scriptCost,
        ApiCostEstimate? ttsCost)
    {
        metadata.ScriptCostUsd = scriptCost?.Usd;
        metadata.ScriptCostIrt = scriptCost?.Irt;
        metadata.TtsCostUsd = ttsCost?.Usd;
        metadata.TtsCostIrt = ttsCost?.Irt;

        var totalCost = ApiCostEstimate.TryCombine(scriptCost, ttsCost);
        metadata.TotalCostUsd = totalCost?.Usd;
        metadata.TotalCostIrt = totalCost?.Irt;
        metadata.CostSource = totalCost?.Source;
    }

    private static async Task WriteTextFileAsync(string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string BuildDueCardsQuery(PodcastDeck deck)
    {
        var escapedDeckName = deck.DeckName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return $"deck:\"{escapedDeckName}\" is:due";
    }

    private static string ComputeGenerationSettingsHash(
        string ankiQuery,
        int targetMinutes,
        int maxCards,
        int? cardsPerPodcast,
        bool multiSpeaker,
        string scriptProvider,
        string textToSpeechProvider,
        string textToSpeechProviderSettings,
        string ttsNormalizerSettings,
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
            TtsTextNormalizationVersion,
            TtsScriptNormalizationVersion,
            ankiQuery,
            targetMinutes,
            maxCards,
            cardsPerPodcast?.ToString() ?? string.Empty,
            multiSpeaker,
            scriptProvider,
            textToSpeechProvider,
            textToSpeechProviderSettings,
            ttsNormalizerSettings,
            scriptModel,
            ttsModel,
            ttsFallbackModel,
            voiceA,
            voiceB,
            ttsSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string BuildTextToSpeechProviderSettings(string textToSpeechProvider, KokoroOptions kokoroOptions)
    {
        if (!string.Equals(textToSpeechProvider, "Kokoro", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(textToSpeechProvider, "LocalKokoro", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            kokoroOptions.ModelName,
            kokoroOptions.Language);
    }

    private static string BuildTtsNormalizerSettings(
        TtsNormalizerOptions normalizerOptions,
        string defaultLlmModel)
    {
        var mode = string.IsNullOrWhiteSpace(normalizerOptions.Mode)
            ? string.Empty
            : normalizerOptions.Mode.Trim();
        var llmModel = string.IsNullOrWhiteSpace(normalizerOptions.LlmModel)
            ? defaultLlmModel
            : normalizerOptions.LlmModel.Trim();

        return string.Join(
            "\n",
            normalizerOptions.Enabled,
            mode,
            llmModel);
    }
}
