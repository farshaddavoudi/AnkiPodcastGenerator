using System.Text.RegularExpressions;
using AnkiPodcastGenerator.Core.Interfaces;

namespace AnkiPodcastGenerator.Core.Services;

public sealed class PodcastTtsTextNormalizer : IPodcastTtsTextNormalizer
{
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var value = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        value = Regex.Replace(value, @"\*\*(.+?)\*\*", "$1", RegexOptions.Singleline);
        value = Regex.Replace(value, @"__(.+?)__", "$1", RegexOptions.Singleline);
        value = Regex.Replace(value, @"`([^`]+)`", "$1", RegexOptions.Singleline);
        value = value.Replace("**", string.Empty, StringComparison.Ordinal);

        value = ApplyPronunciations(value);
        value = Regex.Replace(value, @"[ \t]{2,}", " ");
        value = Regex.Replace(value, @"\n{3,}", "\n\n");

        return value.Trim();
    }

    private static string ApplyPronunciations(string value)
    {
        value = Regex.Replace(value, @"\bkubectl\b", "kube C T L", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bKube-Proxy\b", "kube proxy", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bKubeadm\b", "kube admin", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bMinikube\b", "mini kube", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\betcd\b", "et C D", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bcontainer-d\b", "container D", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bContainerd\b", "container D", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bCRI-O\b", "C R I O", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bPODs\b", "pods");
        value = Regex.Replace(value, @"\bPOD\b", "pod");
        value = Regex.Replace(value, @"\bRAM\b", "ram");
        value = Regex.Replace(value, @"\bCPU\b", "C P U");

        return value;
    }
}
