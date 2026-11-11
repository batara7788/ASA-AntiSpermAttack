using System.Text;

namespace Asa.Core;

public static class HostsBlocker
{
    public const string BeginMarker = "# ASA-WEB-SHIELD-BEGIN";
    public const string EndMarker = "# ASA-WEB-SHIELD-END";
    public const string TrackerBeginMarker = "# ASA-TRACKER-BLOCK-BEGIN";
    public const string TrackerEndMarker = "# ASA-TRACKER-BLOCK-END";

    public static IReadOnlyList<string> ParseDomainList(string listText)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in listText.Split('\n'))
        {
            string line = rawLine.Trim();
            int commentStart = line.IndexOf('#');
            if (commentStart >= 0)
            {
                line = line.Substring(0, commentStart).Trim();
            }

            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string candidate = parts.Length >= 2 && IsRedirectAddress(parts[0]) ? parts[1] : parts[0];
            if (IsValidDomain(candidate))
            {
                domains.Add(candidate.ToLowerInvariant());
            }
        }

        return domains.OrderBy(domain => domain, StringComparer.Ordinal).ToList();
    }

    public static string ApplyBlockList(string existingHostsText, IEnumerable<string> domains, string beginMarker = BeginMarker, string endMarker = EndMarker)
    {
        string withoutOldBlock = RemoveBlockList(existingHostsText, beginMarker, endMarker).TrimEnd();
        var builder = new StringBuilder();
        builder.Append(withoutOldBlock);
        builder.Append("\r\n\r\n");
        builder.Append(beginMarker).Append("\r\n");
        foreach (string domain in domains)
        {
            builder.Append("0.0.0.0 ").Append(domain).Append("\r\n");
        }

        builder.Append(endMarker).Append("\r\n");
        return builder.ToString();
    }

    public static string RemoveBlockList(string existingHostsText, string beginMarker = BeginMarker, string endMarker = EndMarker)
    {
        var builder = new StringBuilder();
        bool insideBlock = false;
        foreach (string rawLine in existingHostsText.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line == beginMarker)
            {
                insideBlock = true;
                continue;
            }

            if (line == endMarker)
            {
                insideBlock = false;
                continue;
            }

            if (!insideBlock)
            {
                builder.Append(rawLine).Append("\r\n");
            }
        }

        return builder.ToString().TrimEnd() + "\r\n";
    }

    public static int CountBlockedDomains(string existingHostsText, string beginMarker = BeginMarker, string endMarker = EndMarker)
    {
        int count = 0;
        bool insideBlock = false;
        foreach (string rawLine in existingHostsText.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line == beginMarker)
            {
                insideBlock = true;
            }
            else if (line == endMarker)
            {
                insideBlock = false;
            }
            else if (insideBlock && line.Length > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsRedirectAddress(string text)
    {
        return text is "0.0.0.0" or "127.0.0.1" or "::" or "::1";
    }

    private static bool IsValidDomain(string text)
    {
        if (text.Length is < 3 or > 253 || !text.Contains('.'))
        {
            return false;
        }

        if (text is "localhost" or "localhost.localdomain" or "broadcasthost")
        {
            return false;
        }

        foreach (char character in text)
        {
            bool allowed = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '.' || character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return !text.StartsWith('.') && !text.EndsWith('.');
    }
}
