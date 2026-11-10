using Asa.Core;

namespace Asa.App.Services;

public sealed record SmartScanProgress(string Stage, long Done, long Total, string CurrentFile, long Threats, long Suspicious);

public sealed record SmartScanResult(IReadOnlyList<ThreatEvent> Threats, IReadOnlyList<SecurityFinding> Findings, long FilesChecked, TimeSpan Duration);

public sealed class SmartScanService
{
    private const int MaximumCandidates = 40000;
    private const int MaximumDefenderChecks = 150;
    private const long MaximumInspectedSize = 300L * 1024 * 1024;

    private readonly ScanService scanner;
    private readonly DefenderEngine defender;
    private readonly Quarantine quarantine;
    private readonly EventJournal journal;
    private readonly Func<AsaSettings> getSettings;

    public SmartScanService(ScanService scanner, DefenderEngine defender, Quarantine quarantine, EventJournal journal, Func<AsaSettings> getSettings)
    {
        this.scanner = scanner;
        this.defender = defender;
        this.quarantine = quarantine;
        this.journal = journal;
        this.getSettings = getSettings;
    }

    public async Task<SmartScanResult> RunAsync(bool deep, string title, IProgress<SmartScanProgress> progress, CancellationToken cancellationToken)
    {
        DateTime started = DateTime.Now;
        var threats = new List<ThreatEvent>();
        var findings = new List<SecurityFinding>();
        long threatCount = 0;
        long suspiciousCount = 0;

        void OnThreat(ThreatEvent threat)
        {
            if (threat.Source != title)
            {
                return;
            }

            lock (threats)
            {
                threats.Add(threat);
            }

            Interlocked.Increment(ref threatCount);
        }

        scanner.ThreatDetected += OnThreat;
        try
        {
            progress.Report(new SmartScanProgress("Ищу места, где прячутся вирусы", 0, 0, string.Empty, 0, 0));
            List<string> candidates = await Task.Run(() => CollectCandidates(cancellationToken), cancellationToken);

            progress.Report(new SmartScanProgress("Оцениваю подозрительность файлов", 0, candidates.Count, string.Empty, 0, 0));
            List<FileRisk> ranked = await Task.Run(() => RankCandidates(candidates, progress, cancellationToken), cancellationToken);
            int checkedCount = 0;

            var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(ranked, options, async (risk, token) =>
            {
                await scanner.ScanAndHandleAsync(risk.Path, title, token);
                int done = Interlocked.Increment(ref checkedCount);
                if (done % 5 == 0 || risk.Level >= RiskLevel.Medium)
                {
                    progress.Report(new SmartScanProgress("Проверяю движком, от опасных к безопасным", done, ranked.Count, risk.Path, Interlocked.Read(ref threatCount), Interlocked.Read(ref suspiciousCount)));
                }
            });

            HashSet<string> alreadyCaught;
            lock (threats)
            {
                alreadyCaught = new HashSet<string>(threats.Select(threat => threat.Result.Path), StringComparer.OrdinalIgnoreCase);
            }

            List<FileRisk> suspicious = ranked.Where(risk => risk.Level >= RiskLevel.Medium && !alreadyCaught.Contains(risk.Path) && File.Exists(risk.Path)).ToList();
            if (DefenderEngine.CommandLineToolPath != null && suspicious.Count > 0)
            {
                int defenderDone = 0;
                List<FileRisk> forDefender = suspicious.Take(MaximumDefenderChecks).ToList();
                foreach (FileRisk risk in forDefender)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report(new SmartScanProgress("Второе мнение: Защитник Windows", ++defenderDone, forDefender.Count, risk.Path, Interlocked.Read(ref threatCount), Interlocked.Read(ref suspiciousCount)));
                    ScanResult result = await defender.ScanFileAsync(risk.Path, cancellationToken);
                    if (result.Verdict == ScanVerdict.Infected)
                    {
                        HandleDefenderDetection(result, title, threats);
                        Interlocked.Increment(ref threatCount);
                        alreadyCaught.Add(risk.Path);
                    }
                }
            }

            foreach (FileRisk risk in suspicious.Where(risk => !alreadyCaught.Contains(risk.Path)))
            {
                FileRisk capturedRisk = risk;
                findings.Add(new SecurityFinding
                {
                    Category = "Подозрительный файл",
                    Title = Path.GetFileName(risk.Path) + $" — подозрительность {risk.Score}/100",
                    Details = risk.Path + ". " + risk.Summary + ". Антивирусы вирус не нашли, но файл ведёт себя как вредоносный.",
                    Severity = risk.Level == RiskLevel.High ? FindingSeverity.Danger : FindingSeverity.Warning,
                    FilePath = risk.Path,
                    FixDescription = "Убрать в карантин",
                    Fix = () =>
                    {
                        quarantine.Add(capturedRisk.Path, "Подозрительный файл (оценка " + capturedRisk.Score + ")");
                        return "Файл в карантине. Если это ошибка, его можно восстановить.";
                    },
                    TrustKey = "file|" + risk.Path
                });
                Interlocked.Increment(ref suspiciousCount);
            }

            if (deep)
            {
                var alreadyScanned = new HashSet<string>(ranked.Select(risk => risk.Path), StringComparer.OrdinalIgnoreCase);
                long restDone = 0;
                var restProgress = new Progress<ScanProgress>(value =>
                {
                    restDone = value.FilesScanned;
                    progress.Report(new SmartScanProgress("Глубокая проверка остальных файлов", restDone, 0, value.CurrentFile, Interlocked.Read(ref threatCount), Interlocked.Read(ref suspiciousCount)));
                });

                List<string> drives = ProtectionHost.GetFullScanLocations();
                await scanner.ScanLocationsAsync(drives, title, restProgress, _ => { }, cancellationToken, path => !alreadyScanned.Contains(path));
                checkedCount += (int)restDone;
            }

            List<ThreatEvent> finalThreats;
            lock (threats)
            {
                finalThreats = threats.ToList();
            }

            HashSet<string> trusted = new(getSettings().TrustedFindings, StringComparer.OrdinalIgnoreCase);
            List<SecurityFinding> visibleFindings = findings.Where(finding => !trusted.Contains(finding.TrustKey)).ToList();
            journal.Information($"{title}: проверено {checkedCount:N0}, угроз {finalThreats.Count}, подозрительных {visibleFindings.Count}");
            return new SmartScanResult(finalThreats, visibleFindings, checkedCount, DateTime.Now - started);
        }
        finally
        {
            scanner.ThreatDetected -= OnThreat;
        }
    }

    private void HandleDefenderDetection(ScanResult result, string title, List<ThreatEvent> threats)
    {
        string action;
        bool quarantined;
        try
        {
            quarantine.Add(result.Path, result.Details);
            action = "Файл перемещён в карантин.";
            quarantined = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            action = "Не удалось переместить в карантин: " + exception.Message;
            quarantined = false;
        }

        journal.Threat($"[{title}] {result.Details} в {result.Path}. {action}");
        lock (threats)
        {
            threats.Add(new ThreatEvent(result, title, quarantined, action));
        }
    }

    private List<FileRisk> RankCandidates(List<string> candidates, IProgress<SmartScanProgress> progress, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        var risks = new List<FileRisk>(candidates.Count);
        int done = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancellationToken };
        var bag = new System.Collections.Concurrent.ConcurrentBag<FileRisk>();
        Parallel.ForEach(candidates, options, path =>
        {
            try
            {
                FileFacts facts = FileRiskAnalyzer.ReadFacts(path, SignatureVerifier.HasValidSignature);
                bag.Add(FileRiskAnalyzer.Analyze(facts, now));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            int value = Interlocked.Increment(ref done);
            if (value % 200 == 0)
            {
                progress.Report(new SmartScanProgress("Оцениваю подозрительность файлов", value, candidates.Count, path, 0, 0));
            }
        });

        risks.AddRange(bag);
        return risks.OrderByDescending(risk => risk.Score).ThenBy(risk => risk.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> CollectCandidates(CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filter = new PathFilter(getSettings().ExcludedPaths);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        void AddFile(string path)
        {
            if (candidates.Count < MaximumCandidates && filter.ShouldScan(path))
            {
                candidates.Add(path);
            }
        }

        void AddFolder(string folder, bool everything, int depth)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = depth > 0,
                MaxRecursionDepth = Math.Max(0, depth),
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
            };

            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidates.Count >= MaximumCandidates)
                    {
                        return;
                    }

                    if (everything || FileRiskAnalyzer.IsExecutableLike(file) || LooksDisguised(file))
                    {
                        AddFile(file);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        foreach (string path in ProcessMonitor.GetRunningExecutables())
        {
            AddFile(path);
        }

        foreach (AutorunEntry entry in AutorunMonitor.ReadAllEntries())
        {
            string? executable = AutorunSnapshot.ExtractExecutablePath(entry.Command);
            if (executable != null && File.Exists(executable))
            {
                AddFile(executable);
            }

            if (File.Exists(entry.Command))
            {
                AddFile(entry.Command);
            }
        }

        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), true, 2);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true, 2);
        AddFolder(Path.Combine(profile, "Downloads"), true, 3);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), true, 2);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), true, 1);
        AddFolder(Path.GetTempPath(), true, 4);
        AddFolder(Path.Combine(windows, "Temp"), true, 3);
        AddFolder(Path.Combine(profile, "..", "Public"), false, 4);
        AddFolder(roaming, false, 3);
        AddFolder(local, false, 2);
        AddFolder(Path.Combine(local, "Programs"), false, 3);
        AddFolder(commonData, false, 3);
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false, 3);
        AddFolder(Path.Combine(windows, "Tasks"), true, 1);

        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            AddFolder(drive.RootDirectory.FullName, false, 0);
            AddFolder(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"), false, 3);
        }

        return candidates.ToList();
    }

    private static readonly HashSet<string> DisguiseCheckExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".txt", ".mp3", ".mp4", ".zip", ".rar", ".docm", ".xlsm"
    };

    private static bool LooksDisguised(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension is ".docm" or ".xlsm" or ".pptm" or ".DOCM" or ".XLSM")
        {
            return true;
        }

        if (!DisguiseCheckExtensions.Contains(extension))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumInspectedSize)
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
