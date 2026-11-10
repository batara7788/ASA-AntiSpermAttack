using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Asa.Core;

namespace Asa.App.Services;

public enum EngineState
{
    NotStarted,
    Installing,
    DownloadingDatabase,
    Starting,
    Ready,
    Failed
}

public sealed class ClamAvEngine : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Cisco-Talos/clamav/releases/latest";
    private readonly ApplicationFolders folders;
    private readonly EventJournal journal;
    private readonly HttpClient httpClient;
    private Process? daemonProcess;

    public ClamAvEngine(ApplicationFolders folders, EventJournal journal, HttpClient httpClient)
    {
        this.folders = folders;
        this.journal = journal;
        this.httpClient = httpClient;
        Client = new ClamDaemonClient("127.0.0.1", ClamAvConfiguration.DaemonPort);
    }

    public event Action? StateChanged;

    public ClamDaemonClient Client { get; }

    public EngineState State { get; private set; } = EngineState.NotStarted;

    public string StatusText { get; private set; } = "Движок не запущен";

    public string Version { get; private set; } = string.Empty;

    private string BinaryFolder => Path.Combine(folders.ClamAvFolder, "bin");

    private string DaemonConfigFile => Path.Combine(folders.ClamAvFolder, "clamd.conf");

    private string UpdaterConfigFile => Path.Combine(folders.ClamAvFolder, "freshclam.conf");

    public async Task StartAsync(long maximumFileSizeMegabytes, CancellationToken cancellationToken)
    {
        try
        {
            string daemonPath = await EnsureInstalledAsync(cancellationToken);
            WriteConfigurationFiles(maximumFileSizeMegabytes);

            if (!HasDatabase())
            {
                SetState(EngineState.DownloadingDatabase, "Скачиваю вирусные базы (первый запуск, это несколько минут)");
                await RunUpdaterAsync(cancellationToken);
                if (!HasDatabase())
                {
                    throw new InvalidOperationException("Не удалось скачать вирусные базы. Проверьте интернет.");
                }
            }

            await StartDaemonAsync(daemonPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetState(EngineState.Failed, "Ошибка движка: " + exception.Message);
            journal.Warning("Движок ClamAV не запустился: " + exception.Message);
        }
    }

    public async Task<bool> UpdateDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(UpdaterConfigFile))
        {
            return false;
        }

        journal.Information("Обновление вирусных баз");
        bool updated = await RunUpdaterAsync(cancellationToken);
        if (State == EngineState.Ready)
        {
            try
            {
                await Client.ReloadDatabaseAsync(cancellationToken);
                Version = await Client.GetVersionAsync(cancellationToken);
                StateChanged?.Invoke();
            }
            catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
            {
                journal.Warning("Не удалось перезагрузить базы: " + exception.Message);
            }
        }

        return updated;
    }

    public void Dispose()
    {
        StopDaemon();
    }

    private async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        string? existing = FindExecutable("clamd.exe");
        if (existing != null && FindExecutable("freshclam.exe") != null)
        {
            return existing;
        }

        SetState(EngineState.Installing, "Скачиваю движок ClamAV (первый запуск)");
        journal.Information("Установка движка ClamAV");

        string downloadUrl = await FindWindowsPackageUrlAsync(cancellationToken);
        string temporaryZip = Path.Combine(Path.GetTempPath(), "asa-clamav-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (HttpResponseMessage response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using FileStream output = File.Create(temporaryZip);
                await response.Content.CopyToAsync(output, cancellationToken);
            }

            if (Directory.Exists(BinaryFolder))
            {
                Directory.Delete(BinaryFolder, true);
            }

            ZipFile.ExtractToDirectory(temporaryZip, BinaryFolder);
        }
        finally
        {
            File.Delete(temporaryZip);
        }

        return FindExecutable("clamd.exe") ?? throw new FileNotFoundException("В архиве ClamAV нет clamd.exe");
    }

    private async Task<string> FindWindowsPackageUrlAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd("ASA-Antivirus/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.EndsWith(".win.x64.zip", StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Не найден пакет ClamAV для Windows в последнем выпуске");
    }

    private void WriteConfigurationFiles(long maximumFileSizeMegabytes)
    {
        string daemonLog = Path.Combine(folders.LogsFolder, "clamd.log");
        string updaterLog = Path.Combine(folders.LogsFolder, "freshclam.log");
        File.WriteAllText(DaemonConfigFile, ClamAvConfiguration.BuildDaemonConfig(folders.ClamAvDatabaseFolder, daemonLog, maximumFileSizeMegabytes));
        File.WriteAllText(UpdaterConfigFile, ClamAvConfiguration.BuildUpdaterConfig(folders.ClamAvDatabaseFolder, updaterLog));
    }

    private bool HasDatabase()
    {
        return Directory.EnumerateFiles(folders.ClamAvDatabaseFolder)
            .Any(file => file.EndsWith(".cvd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cld", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> RunUpdaterAsync(CancellationToken cancellationToken)
    {
        string? updaterPath = FindExecutable("freshclam.exe");
        if (updaterPath == null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(updaterPath, $"--config-file=\"{UpdaterConfigFile}\" --stdout")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(updaterPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить freshclam");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string output = await outputTask + await errorTask;

        if (process.ExitCode == 0)
        {
            journal.Information("Вирусные базы обновлены");
            return true;
        }

        string lastLines = string.Join(" ", output.Split('\n').Where(line => line.Trim().Length > 0).TakeLast(2));
        journal.Warning("Обновление баз завершилось с кодом " + process.ExitCode + ": " + lastLines);
        return false;
    }

    private async Task StartDaemonAsync(string daemonPath, CancellationToken cancellationToken)
    {
        if (await Client.PingAsync(cancellationToken))
        {
            await MarkReadyAsync(cancellationToken);
            return;
        }

        SetState(EngineState.Starting, "Загружаю вирусные базы в память");
        StopDaemon();

        var startInfo = new ProcessStartInfo(daemonPath, $"--config-file=\"{DaemonConfigFile}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(daemonPath)!
        };
        daemonProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить clamd");

        DateTime deadline = DateTime.Now.AddMinutes(4);
        while (DateTime.Now < deadline)
        {
            await Task.Delay(1500, cancellationToken);
            if (daemonProcess.HasExited)
            {
                throw new InvalidOperationException("clamd завершился с кодом " + daemonProcess.ExitCode + ". Подробности в logs\\clamd.log");
            }

            if (await Client.PingAsync(cancellationToken))
            {
                await MarkReadyAsync(cancellationToken);
                return;
            }
        }

        throw new TimeoutException("clamd не ответил за 4 минуты");
    }

    private async Task MarkReadyAsync(CancellationToken cancellationToken)
    {
        Version = await Client.GetVersionAsync(cancellationToken);
        SetState(EngineState.Ready, "Движок готов");
        journal.Information("Движок запущен: " + Version);
    }

    private void StopDaemon()
    {
        if (daemonProcess != null && !daemonProcess.HasExited)
        {
            try
            {
                daemonProcess.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        daemonProcess?.Dispose();
        daemonProcess = null;
    }

    private string? FindExecutable(string fileName)
    {
        if (!Directory.Exists(BinaryFolder))
        {
            return null;
        }

        return Directory.EnumerateFiles(BinaryFolder, fileName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private void SetState(EngineState state, string statusText)
    {
        State = state;
        StatusText = statusText;
        StateChanged?.Invoke();
    }
}
