namespace Asa.Core;

public enum JournalLevel
{
    Information,
    Warning,
    Threat
}

public sealed record JournalEntry(DateTime Time, JournalLevel Level, string Message);

public sealed class EventJournal
{
    private const int MaximumEntriesInMemory = 2000;
    private readonly string journalFile;
    private readonly object entriesLock = new();
    private readonly List<JournalEntry> entries = new();

    public EventJournal(string journalFile)
    {
        this.journalFile = journalFile;
        LoadExistingEntries();
    }

    public event Action<JournalEntry>? EntryAdded;

    public IReadOnlyList<JournalEntry> GetEntries()
    {
        lock (entriesLock)
        {
            return entries.ToList();
        }
    }

    public void Information(string message) => Add(JournalLevel.Information, message);

    public void Warning(string message) => Add(JournalLevel.Warning, message);

    public void Threat(string message) => Add(JournalLevel.Threat, message);

    public void Add(JournalLevel level, string message)
    {
        var entry = new JournalEntry(DateTime.Now, level, message.Replace('\n', ' ').Replace('\r', ' '));
        lock (entriesLock)
        {
            entries.Add(entry);
            if (entries.Count > MaximumEntriesInMemory)
            {
                entries.RemoveAt(0);
            }

            try
            {
                File.AppendAllText(journalFile, FormatLine(entry) + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }

        EntryAdded?.Invoke(entry);
    }

    private static string FormatLine(JournalEntry entry)
    {
        return $"{entry.Time:yyyy-MM-dd HH:mm:ss}\t{entry.Level}\t{entry.Message}";
    }

    private void LoadExistingEntries()
    {
        if (!File.Exists(journalFile))
        {
            return;
        }

        foreach (string line in File.ReadLines(journalFile).TakeLast(MaximumEntriesInMemory))
        {
            string[] parts = line.Split('\t', 3);
            if (parts.Length == 3
                && DateTime.TryParse(parts[0], out DateTime time)
                && Enum.TryParse(parts[1], out JournalLevel level))
            {
                entries.Add(new JournalEntry(time, level, parts[2]));
            }
        }
    }
}
