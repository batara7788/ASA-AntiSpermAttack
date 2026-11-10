using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Asa.Core;

public sealed class QuarantineItem
{
    public string Id { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public string ThreatName { get; set; } = string.Empty;

    public DateTime QuarantinedAt { get; set; }

    public long OriginalSize { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class Quarantine
{
    private static readonly byte[] EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes("ASA AntiSpermAttack quarantine key"));
    private readonly string quarantineFolder;
    private readonly object quarantineLock = new();

    public Quarantine(string quarantineFolder)
    {
        this.quarantineFolder = quarantineFolder;
        Directory.CreateDirectory(quarantineFolder);
    }

    public event Action? Changed;

    public QuarantineItem Add(string filePath, string threatName)
    {
        QuarantineItem item;
        lock (quarantineLock)
        {
            byte[] content = ReadWithRetries(filePath);
            item = new QuarantineItem
            {
                Id = Guid.NewGuid().ToString("N"),
                OriginalPath = filePath,
                ThreatName = threatName,
                QuarantinedAt = DateTime.Now,
                OriginalSize = content.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
            };

            File.WriteAllBytes(GetDataPath(item.Id), Encrypt(content));
            File.WriteAllText(GetMetadataPath(item.Id), JsonSerializer.Serialize(item));

            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.Delete(GetDataPath(item.Id));
                File.Delete(GetMetadataPath(item.Id));
                throw;
            }
        }

        Changed?.Invoke();
        return item;
    }

    public IReadOnlyList<QuarantineItem> GetItems()
    {
        lock (quarantineLock)
        {
            var items = new List<QuarantineItem>();
            foreach (string metadataFile in Directory.EnumerateFiles(quarantineFolder, "*.json"))
            {
                try
                {
                    QuarantineItem? item = JsonSerializer.Deserialize<QuarantineItem>(File.ReadAllText(metadataFile));
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception exception) when (exception is JsonException or IOException)
                {
                }
            }

            return items.OrderByDescending(item => item.QuarantinedAt).ToList();
        }
    }

    public string Restore(string id, string? targetPath = null)
    {
        string restoredPath;
        lock (quarantineLock)
        {
            QuarantineItem item = LoadItem(id);
            restoredPath = targetPath ?? item.OriginalPath;
            if (File.Exists(restoredPath))
            {
                restoredPath = MakeUniquePath(restoredPath);
            }

            string? folder = Path.GetDirectoryName(restoredPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllBytes(restoredPath, Decrypt(File.ReadAllBytes(GetDataPath(id))));
            DeleteFiles(id);
        }

        Changed?.Invoke();
        return restoredPath;
    }

    public void Delete(string id)
    {
        lock (quarantineLock)
        {
            DeleteFiles(id);
        }

        Changed?.Invoke();
    }

    public static byte[] Encrypt(byte[] content)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.GenerateIV();
        byte[] encrypted = aes.EncryptCbc(content, aes.IV);
        return aes.IV.Concat(encrypted).ToArray();
    }

    public static byte[] Decrypt(byte[] encryptedWithIv)
    {
        using Aes aes = Aes.Create();
        aes.Key = EncryptionKey;
        byte[] iv = encryptedWithIv.Take(16).ToArray();
        byte[] encrypted = encryptedWithIv.Skip(16).ToArray();
        return aes.DecryptCbc(encrypted, iv);
    }

    private QuarantineItem LoadItem(string id)
    {
        string metadataPath = GetMetadataPath(id);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("В карантине нет такой записи", metadataPath);
        }

        return JsonSerializer.Deserialize<QuarantineItem>(File.ReadAllText(metadataPath))
            ?? throw new InvalidDataException("Повреждена запись карантина");
    }

    private void DeleteFiles(string id)
    {
        File.Delete(GetDataPath(id));
        File.Delete(GetMetadataPath(id));
    }

    private static byte[] ReadWithRetries(string filePath)
    {
        const int attempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(300 * attempt);
            }
        }
    }

    private static string MakeUniquePath(string path)
    {
        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int number = 1; ; number++)
        {
            string candidate = Path.Combine(folder, $"{name} (восстановлен {number}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private string GetDataPath(string id) => Path.Combine(quarantineFolder, id + ".asaq");

    private string GetMetadataPath(string id) => Path.Combine(quarantineFolder, id + ".json");
}
