using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using Xunit;

namespace AnkiPodcastGenerator.Tests;

public sealed class DeckProviderTests
{
    [Fact]
    public void Constructor_AllowsZeroMaxCardsForDisabledDeck()
    {
        var provider = new DeckProvider(new DecksOptions
        {
            Decks =
            [
                new PodcastDeck { DeckName = "Disabled", MaxCards = 0 },
                new PodcastDeck { DeckName = "Enabled", MaxCards = 10 }
            ]
        });

        Assert.Equal(0, provider.GetRequiredDeck("Disabled").MaxCards);
        Assert.Contains(provider.GetAllDecks(), deck => deck.DeckName == "Disabled");
    }

    [Fact]
    public void Constructor_RejectsNegativeMaxCards()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new DeckProvider(new DecksOptions
            {
                Decks = [new PodcastDeck { DeckName = "Invalid", MaxCards = -1 }]
            }));

        Assert.Contains("MaxCards", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
