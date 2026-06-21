using System.Text.Json;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public static class PodcastScriptPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildSystemPrompt() =>
        """
        You generate educational podcast scripts from Anki cards.
        Output only the script, using this exact speaker marker format:
        [A]
        text...

        [B]
        text...

        You may also use this exact silent pause marker between topic sections:
        [PAUSE:5]

        Host A is a senior engineer. Host B is an interviewer.
        The tone is friendly, practical, educational, and optimized for recall.
        Output plain spoken text only. Do not use Markdown formatting: no asterisks, bold, italics, headings, bullets, or backticks.
        Keep a calm, slower teaching pace with short sentences.
        Keep the final script close to the requested target duration.
        Use conversational turns, but do not over-dialogue. Every turn should teach or check recall.
        Group related selected cards into coherent topic sections even if the incoming due-card order is mixed.
        You may reorder the selected cards within the podcast to improve coherence, but cover every selected card exactly once.
        When two selected cards are related, explicitly connect them with a short note.
        Do not use cards or facts that are not present in the provided JSON.
        Do not hallucinate facts that are not in the cards.
        Cover every card.
        Insert [PAUSE:5] on its own line between unrelated topic sections.
        Do not skip commands, warnings, caveats, examples, paths, flags, or error messages.
        Preserve commands, code, paths, variable names, flags, and syntax exactly when they appear in the cards.
        Write commands as plain text, not Markdown inline code.
        Spend more time on complex cards and less time on simple cards.
        """;

    public static string BuildUserPrompt(IReadOnlyList<AnkiCard> cards, PodcastDeck deck, int targetMinutes)
    {
        var compactCards = cards.Select((card, index) => new
        {
            originalDueOrder = index + 1,
            cardId = card.CardId,
            deck = card.DeckName,
            front = card.Front,
            back = card.Back,
            tags = card.Tags
        });

        var cardsJson = JsonSerializer.Serialize(compactCards, JsonOptions);

        return $$"""
        Create a two-host podcast script for Anki deck "{{deck.DeckName}}".
        Target duration: about {{targetMinutes}} minutes.

        Requirements:
        - Use [A] and [B] speaker markers exactly.
        - Host A explains as a senior engineer.
        - Host B asks practical interviewer questions and checks understanding.
        - Make it sound like a real conversation, not an article being read aloud.
        - Output plain spoken text only.
        - Do not use Markdown formatting: no asterisks, bold, italics, headings, bullets, or backticks.
        - First identify natural topic groups among the selected cards.
        - Reorder selected cards within the podcast when it improves coherence.
        - Preserve the selected set: cover every provided card once, and do not add unprovided cards.
        - When moving across unrelated topics, insert [PAUSE:5] on its own line.
        - When two selected cards are related, add a short bridging note explaining the connection.
        - Use a slower, focused pace with short sentences.
        - Keep the length close to the target duration.
        - Avoid filler, greetings, recaps that add no recall value, or excessive back-and-forth.
        - Avoid rushing through dense material. Pause between ideas by starting a new speaker block.
        - Make the script useful for active recall.
        - Cover all cards in the provided JSON.
        - Do not skip commands, warnings, caveats, examples, paths, flags, or error messages.
        - Quote exact commands, code, paths, variable names, flags, and syntax before explaining them, but write them as plain text.
        - If a card is simple, cover it briefly.
        - If a card is complex, slow down and explain it carefully.
        - Do not invent missing details. Say the card does not specify if needed.

        Cards JSON:
        {{cardsJson}}
        """;
    }

    public static int CalculateMaxCompletionTokens(int targetMinutes)
    {
        var normalizedTarget = Math.Max(1, targetMinutes);
        return Math.Clamp(normalizedTarget * 320, 440, 12_000);
    }
}
