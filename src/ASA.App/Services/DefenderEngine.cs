using System.Diagnostics;
using System.Management;
using Asa.Core;

namespace Asa.App.Services;

public sealed record DefenderStatus(bool Installed, bool AntivirusEnabled, bool RealTimeEnabled, IReadOnlyList<string> ExcludedPaths, IReadOnlyList<string> ExcludedProcesses, IReadOnlyList<string> ExcludedExtensions);

public sealed class DefenderEngine
{
    private readonly SemaphoreSlim parallelLimit = new(2);

    public static string? CommandLineToolPath
    {
        get
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string classic = Path.Combine(programFiles, "Windows Defender", "MpCmdRun.exe");
            if (File.Exists(classic))
            {
                return classic;
            }

            string platformFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows Defender", "Platform");
            if (!Directory.Exists(platformFolder))
            {
                return null;
            }

            return Directory.EnumerateDirectories(platformFolder)
                .OrderByDescending(folder => folder, StringComparer.OrdinalIgnoreCase)
                .Select(folder => Path.Combine(folder, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);
        }
    }

    public static DefenderStatus ReadStatus()
    {
        bool installed = CommandLineToolPath != null;
        bool antivirusEnabled = false;
        bool realTimeEnabled = false;
        var excludedPaths = new List<string>();
        var excludedProcesses = new List<string>();
        var excludedExtensions = new List<string>();

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus")))
            {
                foreach (ManagementBaseObject status in searcher.Get())
                {
                    antivirusEnabled = status["AntivirusEnabled"] is true;
                    realTimeEnabled = status["RealTimeProtectionEnabled"] is true;
                }
            }

            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT ExclusionPath, ExclusionProcess, ExclusionExtension FROM MSFT_MpPreference")))
            {
                foreach (ManagementBaseObject preference in searcher.Get())
                {
                    AddStrings(excludedPaths, preference["ExclusionPath"]);
                    AddStrings(excludedProcesses, preference["ExclusionProcess"]);
                    AddStrings(excludedExtensions, preference["ExclusionExtension"]);
                }
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TypeInitializationException or PlatformNotSupportedException)
        {
        }

        return new DefenderStatus(installed, antivirusEnabled, realTimeEnabled, excludedPaths, excludedProcesses, excludedExtensions);
    }

    public async Task<ScanResult> ScanFileAsync(string path, CancellationToken cancellationToken)
    {
        string? tool = CommandLineToolPath;
        if (tool == null)
        {
            return ScanResult.Skipped(path, "Защитник Windows не найден");
        }

        await parallelLimit.WaitAsync(cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo(tool, $"-Scan -ScanType 3 -File \"{path}\" -DisableRemediation")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("MpCmdRun не запустился");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(true);
                return ScanResult.Failed(path, "Защитник не ответил за 2 минуты");
            }

            string text = await output;
            if (process.ExitCode == 2)
            {
                return ScanResult.Infected(path, "Defender: " + (DefenderOutput.ParseThreatName(text) ?? "угроза"));
            }

            return process.ExitCode == 0 ? ScanResult.Clean(path) : ScanResult.Failed(path, "Защитник вернул код " + process.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return ScanResult.Failed(path, exception.Message);
        }
        finally
        {
            parallelLimit.Release();
        }
    }

    private static void AddStrings(List<string> target, object? value)
    {
        if (value is string[] values)
        {
            target.AddRange(values.Where(item => !string.IsNullOrWhiteSpace(item) && item != "N/A: Must be an administrator to view exclusions"));
        }
    }
}
