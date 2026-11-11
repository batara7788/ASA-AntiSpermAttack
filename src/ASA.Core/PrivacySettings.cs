using System.Text.Json;

namespace Asa.Core;

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine
}

public interface IRegistryStore
{
    object? Read(RegistryRoot root, string path, string name);

    void Write(RegistryRoot root, string path, string name, object value);

    void Delete(RegistryRoot root, string path, string name);
}

public sealed record RegistryChange(RegistryRoot Root, string Path, string Name, object Value)
{
    public string Key => Root + "|" + Path + "|" + Name;
}

public sealed class PrivacySetting
{
    public required string Id { get; init; }

    public required string Group { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public bool Recommended { get; init; } = true;

    public required IReadOnlyList<RegistryChange> Changes { get; init; }
}

public sealed class RegistryBackupEntry
{
    public string Key { get; set; } = string.Empty;

    public bool Existed { get; set; }

    public bool IsNumber { get; set; }

    public long Number { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class PrivacyManager
{
    private readonly IRegistryStore registry;
    private readonly string backupFile;
    private readonly object backupLock = new();

    public PrivacyManager(IRegistryStore registry, string backupFile)
    {
        this.registry = registry;
        this.backupFile = backupFile;
    }

    public bool IsApplied(PrivacySetting setting)
    {
        foreach (RegistryChange change in setting.Changes)
        {
            object? current = registry.Read(change.Root, change.Path, change.Name);
            if (!ValuesEqual(current, change.Value))
            {
                return false;
            }
        }

        return true;
    }

    public void Apply(PrivacySetting setting)
    {
        lock (backupLock)
        {
            Dictionary<string, RegistryBackupEntry> backup = LoadBackup();
            foreach (RegistryChange change in setting.Changes)
            {
                if (!backup.ContainsKey(change.Key))
                {
                    backup[change.Key] = CreateBackupEntry(change);
                }
            }

            SaveBackup(backup);
        }

        foreach (RegistryChange change in setting.Changes)
        {
            registry.Write(change.Root, change.Path, change.Name, change.Value);
        }
    }

    public void Revert(PrivacySetting setting)
    {
        lock (backupLock)
        {
            Dictionary<string, RegistryBackupEntry> backup = LoadBackup();
            foreach (RegistryChange change in setting.Changes)
            {
                if (backup.TryGetValue(change.Key, out RegistryBackupEntry? entry) && entry.Existed)
                {
                    object original = entry.IsNumber ? (object)(int)entry.Number : entry.Text;
                    registry.Write(change.Root, change.Path, change.Name, original);
                }
                else
                {
                    registry.Delete(change.Root, change.Path, change.Name);
                }

                backup.Remove(change.Key);
            }

            SaveBackup(backup);
        }
    }

    public static int CalculateScore(int applied, int total, bool trackersBlocked, bool dnsEncrypted)
    {
        double settingsPart = total == 0 ? 0 : 60.0 * applied / total;
        double score = settingsPart + (trackersBlocked ? 20 : 0) + (dnsEncrypted ? 20 : 0);
        return (int)Math.Round(score);
    }

    private RegistryBackupEntry CreateBackupEntry(RegistryChange change)
    {
        object? current = registry.Read(change.Root, change.Path, change.Name);
        var entry = new RegistryBackupEntry { Key = change.Key, Existed = current != null };
        if (current is int or long or uint)
        {
            entry.IsNumber = true;
            entry.Number = Convert.ToInt64(current);
        }
        else if (current != null)
        {
            entry.Text = current.ToString() ?? string.Empty;
        }

        return entry;
    }

    private static bool ValuesEqual(object? current, object expected)
    {
        if (current == null)
        {
            return false;
        }

        if (expected is int expectedNumber)
        {
            return current is int or long or uint && Convert.ToInt64(current) == expectedNumber;
        }

        return string.Equals(current.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, RegistryBackupEntry> LoadBackup()
    {
        if (!File.Exists(backupFile))
        {
            return new Dictionary<string, RegistryBackupEntry>();
        }

        try
        {
            List<RegistryBackupEntry> entries = JsonSerializer.Deserialize<List<RegistryBackupEntry>>(File.ReadAllText(backupFile)) ?? new List<RegistryBackupEntry>();
            return entries.ToDictionary(entry => entry.Key);
        }
        catch (JsonException)
        {
            return new Dictionary<string, RegistryBackupEntry>();
        }
    }

    private void SaveBackup(Dictionary<string, RegistryBackupEntry> backup)
    {
        File.WriteAllText(backupFile, JsonSerializer.Serialize(backup.Values.ToList()));
    }
}
