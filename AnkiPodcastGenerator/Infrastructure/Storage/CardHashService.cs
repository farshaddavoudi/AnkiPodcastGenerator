using System.Security.Cryptography;
using System.Text;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Infrastructure.Storage;

public sealed class CardHashService : ICardHashService
{
    public string ComputeHash(IEnumerable<AnkiCard> cards)
    {
        var builder = new StringBuilder();

        foreach (var card in cards.OrderBy(card => card.CardId))
        {
            AppendField(builder, card.CardId.ToString());
            AppendField(builder, card.Front);
            AppendField(builder, card.Back);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
