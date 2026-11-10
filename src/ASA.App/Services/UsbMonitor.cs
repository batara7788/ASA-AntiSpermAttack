using System.Management;
using Asa.Core;

namespace Asa.App.Services;

public sealed class UsbMonitor : IDisposable
{
    private const string Source = "Проверка флешки";
    private readonly ScanService scanService;
    private readonly EventJournal journal;
    private readonly Notifier notifier;
    private ManagementEventWatcher? watcher;

    public UsbMonitor(ScanService scanService, EventJournal journal, Notifier notifier)
    {
        this.scanService = scanService;
        this.journal = journal;
        this.notifier = notifier;
    }

    public bool IsRunning => watcher != null;

    public void Start()
    {
        Stop();
        try
        {
            watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
            watcher.EventArrived += OnVolumeArrived;
            watcher.Start();
        }
        catch (Exception exception)
        {
            journal.Warning("Слежение за флешками не запустилось: " + exception.Message);
            watcher = null;
        }
    }

    public void Stop()
    {
        if (watcher == null)
        {
            return;
        }

        try
        {
            watcher.Stop();
        }
        catch (ManagementException)
        {
        }

        watcher.Dispose();
        watcher = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnVolumeArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        string? driveName = eventArgs.NewEvent.Properties["DriveName"]?.Value?.ToString();
        if (string.IsNullOrEmpty(driveName))
        {
            return;
        }

        string root = driveName.EndsWith('\\') ? driveName : driveName + "\\";
        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.Removable)
        {
            return;
        }

        Task.Run(() => ScanDriveAsync(root, drive));
    }

    private async Task ScanDriveAsync(string root, DriveInfo drive)
    {
        string label = SafeLabel(drive);
        journal.Information("Подключена флешка " + root + " " + label + ", начинаю проверку");
        notifier.Show("Флешка подключена", $"Проверяю {root} {label}");

        long threats = 0;
        long scanned = 0;
        var progress = new Progress<ScanProgress>(value =>
        {
            scanned = value.FilesScanned;
            threats = value.ThreatsFound;
        });

        try
        {
            await scanService.ScanLocationsAsync(new[] { root }, Source, progress, _ => { }, CancellationToken.None);
            await Task.Delay(200);
            string summary = threats == 0 ? "Угроз нет" : "Найдено угроз: " + threats;
            journal.Information($"Флешка {root} проверена: файлов {scanned}. {summary}");
            notifier.Show("Флешка проверена", $"{root} {label}: {summary}", threats == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch (Exception exception)
        {
            journal.Warning("Ошибка проверки флешки " + root + ": " + exception.Message);
        }
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.VolumeLabel : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
