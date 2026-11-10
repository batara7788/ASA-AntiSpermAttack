namespace Asa.Core;

public sealed class RansomwareDetector
{
    private readonly object detectorLock = new();
    private readonly Queue<DateTime> recentModifications = new();
    private readonly HashSet<string> canaryFiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime lastAlertTime = DateTime.MinValue;

    public RansomwareDetector(int modificationThreshold = 60, TimeSpan? window = null, TimeSpan? alertCooldown = null)
    {
        ModificationThreshold = modificationThreshold;
        Window = window ?? TimeSpan.FromSeconds(10);
        AlertCooldown = alertCooldown ?? TimeSpan.FromMinutes(1);
    }

    public int ModificationThreshold { get; }

    public TimeSpan Window { get; }

    public TimeSpan AlertCooldown { get; }

    public void RegisterCanaryFile(string path)
    {
        lock (detectorLock)
        {
            canaryFiles.Add(Path.GetFullPath(path));
        }
    }

    public bool IsCanaryFile(string path)
    {
        lock (detectorLock)
        {
            return canaryFiles.Contains(Path.GetFullPath(path));
        }
    }

    public RansomwareAlert? RecordModification(string path, DateTime time)
    {
        lock (detectorLock)
        {
            if (canaryFiles.Contains(Path.GetFullPath(path)))
            {
                return CreateAlertIfAllowed(time, "Изменён файл-ловушка: " + path);
            }

            if (!LooksLikeUserDocument(path))
            {
                return null;
            }

            recentModifications.Enqueue(time);
            while (recentModifications.Count > 0 && time - recentModifications.Peek() > Window)
            {
                recentModifications.Dequeue();
            }

            if (recentModifications.Count >= ModificationThreshold)
            {
                int count = recentModifications.Count;
                recentModifications.Clear();
                return CreateAlertIfAllowed(time, $"За {Window.TotalSeconds:0} секунд изменено {count} документов");
            }

            return null;
        }
    }

    private RansomwareAlert? CreateAlertIfAllowed(DateTime time, string reason)
    {
        if (time - lastAlertTime < AlertCooldown)
        {
            return null;
        }

        lastAlertTime = time;
        return new RansomwareAlert(time, reason);
    }

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".rtf", ".odt", ".ods",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".psd", ".mp3", ".mp4", ".mov", ".avi",
        ".zip", ".rar", ".7z", ".csv", ".json", ".xml", ".sql", ".db", ".cs", ".py", ".js", ".html"
    };

    private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".locked", ".encrypted", ".crypt", ".crypted", ".enc", ".lock", ".locky", ".wncry", ".cerber"
    };

    public static bool LooksLikeUserDocument(string path)
    {
        string extension = Path.GetExtension(path);
        return DocumentExtensions.Contains(extension) || SuspiciousExtensions.Contains(extension);
    }
}

public sealed record RansomwareAlert(DateTime Time, string Reason);
