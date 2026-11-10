namespace Asa.Core;

public enum FindingSeverity
{
    Info,
    Warning,
    Danger
}

public sealed class SecurityFinding
{
    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Details { get; init; }

    public FindingSeverity Severity { get; init; } = FindingSeverity.Warning;

    public string? FilePath { get; init; }

    public string? FixDescription { get; init; }

    public Func<string>? Fix { get; init; }

    public string TrustKey { get; init; } = string.Empty;
}

public static class HostsAudit
{
    private static readonly string[] ProtectedDomains =
    {
        "google.", "microsoft.", "windowsupdate.", "live.com", "office.", "apple.", "facebook.", "vk.com",
        "yandex.", "mail.ru", "sberbank.", "tinkoff.", "paypal.", "github.", "steam", "kaspersky.",
        "eset.", "avast.", "malwarebytes.", "virustotal.", "drweb.", "clamav.", "bing.", "outlook."
    };

    public static IReadOnlyList<(string Address, string Domain)> FindSuspiciousRedirects(string hostsText)
    {
        var results = new List<(string Address, string Domain)>();
        string withoutAsaBlock = HostsBlocker.RemoveBlockList(hostsText);
        foreach (string rawLine in withoutAsaBlock.Split('\n'))
        {
            string line = rawLine.Trim();
            int comment = line.IndexOf('#');
            if (comment >= 0)
            {
                line = line.Substring(0, comment).Trim();
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string address = parts[0];
            foreach (string domain in parts.Skip(1))
            {
                string lowerDomain = domain.ToLowerInvariant();
                if (lowerDomain is "localhost" or "localhost.localdomain")
                {
                    continue;
                }

                bool protectedDomain = ProtectedDomains.Any(lowerDomain.Contains);
                bool redirectsSomewhere = address is not ("0.0.0.0" or "127.0.0.1" or "::1" or "::");
                if (protectedDomain || redirectsSomewhere)
                {
                    results.Add((address, lowerDomain));
                }
            }
        }

        return results;
    }

    public static string RemoveEntries(string hostsText, IEnumerable<string> domains)
    {
        var toRemove = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        foreach (string rawLine in hostsText.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            int comment = line.IndexOf('#');
            string data = comment >= 0 ? line.Substring(0, comment) : line;
            string[] parts = data.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts.Skip(1).Any(toRemove.Contains))
            {
                continue;
            }

            output.Add(rawLine);
        }

        return string.Join("\r\n", output).TrimEnd() + "\r\n";
    }
}
