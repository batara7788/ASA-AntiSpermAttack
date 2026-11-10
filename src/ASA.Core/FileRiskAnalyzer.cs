namespace Asa.Core;

public sealed record RiskReason(int Points, string Description);

public sealed record FileRisk(string Path, int Score, IReadOnlyList<RiskReason> Reasons)
{
    public RiskLevel Level => Score switch
    {
        >= 60 => RiskLevel.High,
        >= 30 => RiskLevel.Medium,
        >= 10 => RiskLevel.Low,
        _ => RiskLevel.None
    };

    public string Summary => string.Join("; ", Reasons.OrderByDescending(reason => reason.Points).Select(reason => reason.Description));
}

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High
}

public sealed class FileFacts
{
    public required string Path { get; init; }

    public long Size { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime ModifiedUtc { get; init; }

    public bool Hidden { get; init; }

    public bool System { get; init; }

    public byte[] Header { get; init; } = Array.Empty<byte>();

    public double Entropy { get; init; }

    public bool? HasValidSignature { get; init; }
}

public static class FileRiskAnalyzer
{
    public static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".sys", ".msi", ".bat", ".cmd", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".jar", ".reg", ".msc", ".iso", ".img", ".vhd"
    };

    public static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".jpg", ".jpeg", ".png",
        ".gif", ".bmp", ".mp3", ".mp4", ".avi", ".mkv", ".zip", ".rar", ".7z", ".csv", ".odt"
    };

    private static readonly HashSet<string> MacroDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docm", ".xlsm", ".pptm", ".dotm", ".xltm", ".xlam", ".ppam"
    };

    private static readonly string[] RiskyFolderMarkers =
    {
        "\\downloads\\", "\\temp\\", "\\tmp\\", "\\appdata\\local\\temp\\", "\\appdata\\roaming\\",
        "\\users\\public\\", "\\programdata\\", "\\$recycle.bin\\", "\\windows\\tasks\\", "\\start menu\\programs\\startup\\"
    };

    private static readonly string[] ProgramFolderMarkers =
    {
        "\\program files\\", "\\program files (x86)\\", "\\windows\\system32\\", "\\windows\\syswow64\\", "\\windows\\winsxs\\"
    };

    private static readonly char[] DeceptiveCharacters = { '‮', '‭', '‎', '‏', '؜' };

    public static bool IsExecutableLike(string path) => ExecutableExtensions.Contains(Path.GetExtension(path));

    public static FileRisk Analyze(FileFacts facts, DateTime nowUtc)
    {
        var reasons = new List<RiskReason>();
        string path = facts.Path;
        string lowerPath = path.ToLowerInvariant().Replace('/', '\\');
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path);
        bool executableExtension = ExecutableExtensions.Contains(extension);
        bool portableExecutable = facts.Header.Length >= 2 && facts.Header[0] == (byte)'M' && facts.Header[1] == (byte)'Z';
        bool inProgramFolder = ProgramFolderMarkers.Any(lowerPath.Contains);
        bool inRiskyFolder = RiskyFolderMarkers.Any(lowerPath.Contains);

        if (fileName.IndexOfAny(DeceptiveCharacters) >= 0)
        {
            reasons.Add(new RiskReason(70, "В имени спрятан символ, переворачивающий расширение"));
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        string innerExtension = Path.GetExtension(withoutExtension);
        if (executableExtension && DocumentExtensions.Contains(innerExtension))
        {
            reasons.Add(new RiskReason(60, $"Двойное расширение: притворяется «{innerExtension}», а на деле «{extension}»"));
        }

        if (portableExecutable && !executableExtension && extension.Length > 0)
        {
            reasons.Add(new RiskReason(55, $"Внутри программа, хотя расширение «{extension}»"));
        }

        if (fileName.Contains("          ", StringComparison.Ordinal))
        {
            reasons.Add(new RiskReason(30, "Длинная цепочка пробелов в имени, чтобы скрыть расширение"));
        }

        if (executableExtension || portableExecutable)
        {
            if (inRiskyFolder && !inProgramFolder)
            {
                reasons.Add(new RiskReason(20, "Программа лежит там, где программам жить незачем"));
            }

            bool atDriveRoot = Path.GetDirectoryName(path) is string folder && Path.GetPathRoot(folder) == folder;
            if (atDriveRoot)
            {
                reasons.Add(new RiskReason(20, "Программа в корне диска"));
            }

            if (facts.HasValidSignature == false && !ScriptExtensions.Contains(extension) && extension.ToLowerInvariant() != ".lnk")
            {
                reasons.Add(new RiskReason(inProgramFolder ? 5 : 20, "Нет цифровой подписи"));
            }

            if (facts.Entropy >= 7.4 && facts.Size > 50 * 1024 && portableExecutable)
            {
                reasons.Add(new RiskReason(15, "Содержимое сжато или зашифровано упаковщиком"));
            }

            if (nowUtc - facts.CreatedUtc < TimeSpan.FromDays(3) || nowUtc - facts.ModifiedUtc < TimeSpan.FromDays(3))
            {
                reasons.Add(new RiskReason(10, "Появился или изменился за последние 3 дня"));
            }

            if (facts.Hidden)
            {
                reasons.Add(new RiskReason(facts.System ? 25 : 20, facts.System ? "Скрытый системный атрибут у программы вне Windows" : "Скрытый файл"));
            }

            if (ScriptExtensions.Contains(extension) && inRiskyFolder)
            {
                reasons.Add(new RiskReason(10, "Скрипт во временной или пользовательской папке"));
            }

            if (portableExecutable && facts.Size is > 0 and < 12 * 1024)
            {
                reasons.Add(new RiskReason(5, "Подозрительно маленькая программа"));
            }
        }

        if (MacroDocumentExtensions.Contains(extension) && inRiskyFolder)
        {
            reasons.Add(new RiskReason(15, "Документ с макросами из интернета"));
        }

        int score = Math.Min(100, reasons.Sum(reason => reason.Points));
        return new FileRisk(path, score, reasons);
    }

    public static double CalculateEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[256];
        foreach (byte value in data)
        {
            counts[value]++;
        }

        double entropy = 0;
        foreach (int count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            double probability = count / (double)data.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    public static FileFacts ReadFacts(string path, Func<string, bool?>? signatureCheck = null)
    {
        var info = new FileInfo(path);
        byte[] header = Array.Empty<byte>();
        double entropy = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int sampleSize = (int)Math.Min(stream.Length, 256 * 1024);
            byte[] sample = new byte[sampleSize];
            int read = stream.ReadAtLeast(sample, sampleSize, false);
            header = sample.AsSpan(0, Math.Min(read, 64)).ToArray();
            entropy = CalculateEntropy(sample.AsSpan(0, read));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        bool? signature = null;
        bool looksExecutable = IsExecutableLike(path) || (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z');
        if (signatureCheck != null && looksExecutable)
        {
            signature = signatureCheck(path);
        }

        return new FileFacts
        {
            Path = path,
            Size = info.Exists ? info.Length : 0,
            CreatedUtc = info.Exists ? info.CreationTimeUtc : DateTime.MinValue,
            ModifiedUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue,
            Hidden = info.Exists && (info.Attributes & FileAttributes.Hidden) != 0,
            System = info.Exists && (info.Attributes & FileAttributes.System) != 0,
            Header = header,
            Entropy = entropy,
            HasValidSignature = signature
        };
    }
}
