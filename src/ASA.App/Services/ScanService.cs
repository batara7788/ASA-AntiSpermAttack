using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Asa.Core;

namespace Asa.App.Services;

public sealed record ThreatEvent(ScanResult Result, string Source, bool Quarantined, string ActionText);

public sealed record ScanProgress(long FilesScanned, long ThreatsFound, string CurrentFile);

public sealed class ScanService : IDisposable
{
    private const int RealTimeWorkers = 3;
    private readonly ClamAvEngine engine;
    private readonly Quarantine quarantine;
    private readonly ScanStatistics statistics;
    private readonly EventJournal journal;
    private readonly Notifier notifier;
    private readonly Func<AsaSettings> getSettings;
    private readonly Channel<(string Path, string Source)> realTimeQueue = Channel.CreateUnbounded<(string Path, string Source)>();
    private readonly ConcurrentDictionary<string, DateTime> recentlyScanned = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource stopSource = new();
    private readonly string ownDataFolder;

    public ScanService(ClamAvEngine engine, Quarantine quarantine, ScanStatistics statistics, EventJournal journal, Notifier notifier, Func<AsaSettings> getSettings, string ownDataFolder)
    {
        this.ownDataFolder = ownDataFolder;
        this.engine = engine;
        this.quarantine = quarantine;
        this.statistics = statistics;
        this.journal = journal;
        this.notifier = notifier;
        this.getSettings = getSettings;

        for (int worker = 0; worker < RealTimeWorkers; worker++)
        {
            Task.Run(ProcessRealTimeQueueAsync);
        }
    }

    public event Action<ThreatEvent>? ThreatDetected;

    public bool EngineReady => engine.State == EngineState.Ready;

    public void Enqueue(string path, string source)
    {
        realTimeQueue.Writer.TryWrite((path, source));
    }

    public async Task<ScanResult> ScanAndHandleAsync(string path, string source, CancellationToken cancellationToken)
    {
        AsaSettings settings = getSettings();
        var filter = new PathFilter(settings.ExcludedPaths.Append(ownDataFolder));
        if (!filter.ShouldScan(path))
        {
            return ScanResult.Skipped(path, "Исключено настройками");
        }

        if (!EngineReady)
        {
            return ScanResult.Skipped(path, "Движок ещё не готов");
        }

        ScanResult result = await engine.Client.ScanFileAsync(path, settings.MaximumFileSizeMegabytes * 1024 * 1024, cancellationToken);
        if (result.Verdict is ScanVerdict.Clean or ScanVerdict.Infected)
        {
            statistics.RecordScannedFile(result.Verdict == ScanVerdict.Infected);
        }

        if (result.Verdict == ScanVerdict.Infected)
        {
            HandleThreat(result, source);
        }

        return result;
    }

    public async Task ScanLocationsAsync(IEnumerable<string> locations, string source, IProgress<ScanProgress> progress, Action<ThreatEvent> onThreat, CancellationToken cancellationToken)
    {
        long scanned = 0;
        long threats = 0;
        void LocalThreatHandler(ThreatEvent threat)
        {
            if (threat.Source == source)
            {
                Interlocked.Increment(ref threats);
                onThreat(threat);
            }
        }

        ThreatDetected += LocalThreatHandler;
        try
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(EnumerateFiles(locations, cancellationToken), options, async (file, token) =>
            {
                await ScanAndHandleAsync(file, source, token);
                long count = Interlocked.Increment(ref scanned);
                if (count % 7 == 0)
                {
                    progress.Report(new ScanProgress(count, Interlocked.Read(ref threats), file));
                }
            });
        }
        finally
        {
            ThreatDetected -= LocalThreatHandler;
            progress.Report(new ScanProgress(Interlocked.Read(ref scanned), Interlocked.Read(ref threats), string.Empty));
            statistics.Save();
        }
    }

    public void Dispose()
    {
        stopSource.Cancel();
        realTimeQueue.Writer.TryComplete();
    }

    private void HandleThreat(ScanResult result, string source)
    {
        bool quarantined = TryQuarantine(result.Path, result.Details, out string actionText);
        string fileName = Path.GetFileName(result.Path);
        journal.Threat($"[{source}] {result.Details} в {result.Path}. {actionText}");
        notifier.Show("Угроза обезврежена: " + result.Details, fileName + Environment.NewLine + actionText, quarantined ? ToolTipIcon.Warning : ToolTipIcon.Error);
        ThreatDetected?.Invoke(new ThreatEvent(result, source, quarantined, actionText));
    }

    private bool TryQuarantine(string path, string threatName, out string actionText)
    {
        try
        {
            quarantine.Add(path, threatName);
            actionText = "Файл перемещён в карантин.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            int killed = KillProcessesRunningFile(path);
            if (killed > 0)
            {
                try
                {
                    Thread.Sleep(500);
                    quarantine.Add(path, threatName);
                    actionText = $"Остановлено процессов: {killed}. Файл перемещён в карантин.";
                    return true;
                }
                catch (Exception retryException) when (retryException is IOException or UnauthorizedAccessException)
                {
                    actionText = "Не удалось переместить в карантин: " + retryException.Message;
                    return false;
                }
            }

            actionText = "Не удалось переместить в карантин: " + exception.Message;
            return false;
        }
    }

    public static int KillProcessesRunningFile(string path)
    {
        int killed = 0;
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? processPath = NativeMethods.GetProcessPath(process.Id);
                if (processPath != null && string.Equals(processPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(true);
                    killed++;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return killed;
    }

    private async Task ProcessRealTimeQueueAsync()
    {
        try
        {
            await foreach ((string path, string source) in realTimeQueue.Reader.ReadAllAsync(stopSource.Token))
            {
                if (!ShouldScanNow(path))
                {
                    continue;
                }

                try
                {
                    await Task.Delay(400, stopSource.Token);
                    await ScanAndHandleAsync(path, source, stopSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    journal.Warning("Ошибка проверки " + path + ": " + exception.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool ShouldScanNow(string path)
    {
        DateTime lastWrite;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            lastWrite = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (recentlyScanned.TryGetValue(path, out DateTime previousWrite) && previousWrite == lastWrite)
        {
            return false;
        }

        recentlyScanned[path] = lastWrite;
        if (recentlyScanned.Count > 20000)
        {
            recentlyScanned.Clear();
        }

        return true;
    }

    private static IEnumerable<string> EnumerateFiles(IEnumerable<string> locations, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
        };

        foreach (string location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(location))
            {
                yield return location;
                continue;
            }

            if (!Directory.Exists(location))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(location, "*", options);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            using IEnumerator<string> enumerator = files.GetEnumerator();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
