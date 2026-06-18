using AnkiPodcastGenerator.Core.Interfaces;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class CommandLineApp(
    IPodcastGeneratorService podcastGenerator,
    IDeckProvider deckProvider)
{
    public async Task<int> RunAsync(string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        if (args.Length == 1 && Is(args[0], "test-anki"))
        {
            var version = await podcastGenerator.TestAnkiConnectivityAsync(cancellationTokenSource.Token);
            Console.WriteLine($"AnkiConnect version: {version}");
            return 0;
        }

        if (args.Length == 2 && Is(args[0], "generate"))
        {
            return await GenerateDeckAsync(args[1], cancellationTokenSource.Token);
        }

        if (args.Length == 1 && Is(args[0], "generate-all"))
        {
            return await GenerateAllDecksAsync(cancellationTokenSource.Token);
        }

        if ((args.Length == 2 || args.Length == 3) && Is(args[0], "preview"))
        {
            int? maxCards = null;
            if (args.Length == 3)
            {
                if (!int.TryParse(args[2], out var parsedMaxCards) || parsedMaxCards <= 0)
                {
                    Console.Error.WriteLine("Preview count must be a positive integer.");
                    return 2;
                }

                maxCards = parsedMaxCards;
            }

            var cards = await podcastGenerator.PreviewCardsAsync(args[1], maxCards, cancellationTokenSource.Token);
            foreach (var card in cards)
            {
                Console.WriteLine($"{card.CardId}\tqueue={card.Queue}\ttype={card.Type}\tdue={card.Due}\t{Truncate(card.Front, 100)}");
            }

            return 0;
        }

        PrintUsage();
        return 2;
    }

    private void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  AnkiPodcastGenerator test-anki");
        Console.WriteLine("  AnkiPodcastGenerator preview <deck-name> [count]");
        Console.WriteLine("  AnkiPodcastGenerator generate <deck-name>");
        Console.WriteLine("  AnkiPodcastGenerator generate-all");
        Console.WriteLine();
        Console.WriteLine("Configured decks:");

        foreach (var deck in deckProvider.GetAllDecks())
        {
            Console.WriteLine($"  {deck.DeckName} (MaxCards={deck.MaxCards})");
        }
    }

    private async Task<int> GenerateDeckAsync(string deckName, CancellationToken cancellationToken)
    {
        var result = await podcastGenerator.GenerateAsync(deckName, cancellationToken);
        Console.WriteLine(result.Message);

        if (!string.IsNullOrWhiteSpace(result.Mp3Path))
        {
            Console.WriteLine(result.Mp3Path);
        }

        return result.Success ? 0 : 1;
    }

    private async Task<int> GenerateAllDecksAsync(CancellationToken cancellationToken)
    {
        var decks = deckProvider.GetAllDecks();
        if (decks.Count == 0)
        {
            Console.Error.WriteLine("No decks are configured. Add entries to the Decks list in appsettings.json.");
            return 2;
        }

        var exitCode = 0;
        foreach (var deck in decks)
        {
            Console.WriteLine($"Generating deck: {deck.DeckName}");

            try
            {
                var result = await podcastGenerator.GenerateAsync(deck.DeckName, cancellationToken);
                Console.WriteLine(result.Message);

                if (!string.IsNullOrWhiteSpace(result.Mp3Path))
                {
                    Console.WriteLine(result.Mp3Path);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Console.Error.WriteLine($"Failed deck '{deck.DeckName}': {ex.Message}");
            }
        }

        return exitCode;
    }

    private static bool Is(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
