using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class DeckProvider : IDeckProvider
{
    private readonly IReadOnlyList<PodcastDeck> _decks;

    public DeckProvider(DecksOptions options)
    {
        _decks = options.Decks;
        ValidateDecks(_decks);
    }

    public PodcastDeck GetRequiredDeck(string deckName)
    {
        var deck = _decks.FirstOrDefault(deck =>
            string.Equals(deck.DeckName, deckName, StringComparison.OrdinalIgnoreCase));

        if (deck is null)
        {
            var available = _decks.Count == 0
                ? "(none configured)"
                : string.Join(", ", _decks.Select(deck => deck.DeckName));
            throw new InvalidOperationException($"Unknown deck '{deckName}'. Available decks: {available}");
        }

        return deck;
    }

    public IReadOnlyList<PodcastDeck> GetAllDecks() => _decks;

    private static void ValidateDecks(IReadOnlyList<PodcastDeck> decks)
    {
        foreach (var deck in decks)
        {
            if (string.IsNullOrWhiteSpace(deck.DeckName))
            {
                throw new InvalidOperationException("Each configured deck must set DeckName.");
            }

            if (deck.MaxCards <= 0)
            {
                throw new InvalidOperationException($"Deck '{deck.DeckName}' must set MaxCards to a positive integer.");
            }

            if (deck.CardsPerPodcast is <= 0)
            {
                throw new InvalidOperationException($"Deck '{deck.DeckName}' must set CardsPerPodcast to a positive integer when provided.");
            }

            if (deck.TargetMinutes is <= 0)
            {
                throw new InvalidOperationException($"Deck '{deck.DeckName}' must set TargetMinutes to a positive integer when provided.");
            }
        }

        var duplicateDeck = decks
            .GroupBy(deck => deck.DeckName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateDeck is not null)
        {
            throw new InvalidOperationException($"Deck '{duplicateDeck.Key}' is configured more than once.");
        }
    }
}
