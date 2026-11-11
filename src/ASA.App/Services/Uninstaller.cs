using System.Diagnostics;
using System.Text;
using Asa.Core;

namespace Asa.App.Services;

public static class Uninstaller
{
    public static void Run()
    {
        StopRunningCopies();
        TryRun(() => SystemIntegration.RemoveAutostart());
        TryRun(SystemIntegration.RemoveContextMenu);
        TryRun(RemoveWebShieldBlock);
    }

    private static void StopRunningCopies()
    {
        int ownProcessId = Environment.ProcessId;
        foreach (string name in new[] { "ASA", "clamd", "freshclam" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    string? path = NativeMethods.GetProcessPath(process.Id);
                    bool belongsToAsa = name == "ASA" || (path != null && path.Contains("\\ASA\\clamav\\", StringComparison.OrdinalIgnoreCase));
                    if (process.Id != ownProcessId && belongsToAsa)
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
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
        }
    }

    private static void RemoveWebShieldBlock()
    {
        string hostsPath = WebShield.HostsFilePath;
        string current = File.ReadAllText(hostsPath);
        if (HostsBlocker.CountBlockedDomains(current) > 0 || current.Contains(HostsBlocker.BeginMarker))
        {
            current = HostsBlocker.RemoveBlockList(current);
            File.WriteAllText(hostsPath, current, new UTF8Encoding(false));
        }

        if (current.Contains(HostsBlocker.TrackerBeginMarker))
        {
            File.WriteAllText(hostsPath, HostsBlocker.RemoveBlockList(current, HostsBlocker.TrackerBeginMarker, HostsBlocker.TrackerEndMarker), new UTF8Encoding(false));
        }
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
