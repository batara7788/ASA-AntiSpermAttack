using System.Text.Json;

namespace Asa.Core;

public sealed record AutorunEntry(string Location, string Name, string Command)
{
    public string Key => Location + "|" + Name;
}

public static class AutorunSnapshot
{
    public static IReadOnlyList<AutorunEntry> FindNewOrChanged(IEnumerable<AutorunEntry> previous, IEnumerable<AutorunEntry> current)
    {
        var previousByKey = new Dictionary<string, AutorunEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (AutorunEntry entry in previous)
        {
            previousByKey[entry.Key] = entry;
        }

        var result = new List<AutorunEntry>();
        foreach (AutorunEntry entry in current)
        {
            if (!previousByKey.TryGetValue(entry.Key, out AutorunEntry? old)
                || !string.Equals(old.Command, entry.Command, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public static IReadOnlyList<AutorunEntry> Load(string file)
    {
        if (!File.Exists(file))
        {
            return Array.Empty<AutorunEntry>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<AutorunEntry>>(File.ReadAllText(file)) ?? new List<AutorunEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<AutorunEntry>();
        }
    }

    public static void Save(string file, IEnumerable<AutorunEntry> entries)
    {
        File.WriteAllText(file, JsonSerializer.Serialize(entries.ToList()));
    }

    public static string? ExtractExecutablePath(string command)
    {
        string text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.Length == 0)
        {
            return null;
        }

        if (text.StartsWith('"'))
        {
            int closingQuote = text.IndexOf('"', 1);
            return closingQuote > 1 ? text.Substring(1, closingQuote - 1) : null;
        }

        int executableEnd = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (executableEnd > 0)
        {
            return text.Substring(0, executableEnd + 4);
        }

        int firstSpace = text.IndexOf(' ');
        return firstSpace > 0 ? text.Substring(0, firstSpace) : text;
    }
}
