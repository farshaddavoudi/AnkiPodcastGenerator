using AnkiPodcastGenerator.Core.Interfaces;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class CommandLineApp(
    IPodcastGeneratorService podcastGenerator,
    IProfileProvider profileProvider)
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
            var result = await podcastGenerator.GenerateAsync(args[1], cancellationTokenSource.Token);
            Console.WriteLine(result.Message);

            if (!string.IsNullOrWhiteSpace(result.Mp3Path))
            {
                Console.WriteLine(result.Mp3Path);
            }

            return result.Success ? 0 : 1;
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
        Console.WriteLine("  AnkiPodcastGenerator preview <profile> [count]");
        Console.WriteLine("  AnkiPodcastGenerator generate <profile>");
        Console.WriteLine();
        Console.WriteLine("Profiles:");

        foreach (var profile in profileProvider.GetAllProfiles())
        {
            Console.WriteLine($"  {profile.Name}");
        }
    }

    private static bool Is(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
