using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace Asa.App.Services;

public static class SystemIntegration
{
    public const string TaskName = "ASA AntiSpermAttack";
    private const string MenuKeyName = "ASAScan";
    private static readonly string[] MenuTargets = { @"Software\Classes\*\shell", @"Software\Classes\Directory\shell", @"Software\Classes\Drive\shell" };

    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsContextMenuInstalled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(MenuTargets[0] + "\\" + MenuKeyName);
        return key != null;
    }

    public static void InstallContextMenu()
    {
        foreach (string target in MenuTargets)
        {
            using RegistryKey menuKey = Registry.CurrentUser.CreateSubKey(target + "\\" + MenuKeyName);
            menuKey.SetValue("MUIVerb", "Проверить с помощью ASA");
            menuKey.SetValue("Icon", "\"" + ExecutablePath + "\",0");
            using RegistryKey commandKey = menuKey.CreateSubKey("command");
            commandKey.SetValue(string.Empty, "\"" + ExecutablePath + "\" --scan \"%1\"");
        }
    }

    public static void RemoveContextMenu()
    {
        foreach (string target in MenuTargets)
        {
            Registry.CurrentUser.DeleteSubKeyTree(target + "\\" + MenuKeyName, false);
        }
    }

    public static bool IsAutostartInstalled()
    {
        return RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;
    }

    public static bool InstallAutostart()
    {
        string userName = WindowsIdentity.GetCurrent().Name;
        string taskXml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>ASA: AntiSpermAttack - защита в реальном времени</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{System.Security.SecurityElement.Escape(userName)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{System.Security.SecurityElement.Escape(userName)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{System.Security.SecurityElement.Escape(ExecutablePath)}</Command>
                  <Arguments>--background</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;

        string xmlFile = Path.Combine(Path.GetTempPath(), "asa-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(xmlFile, taskXml, System.Text.Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlFile}\" /F") == 0;
        }
        finally
        {
            File.Delete(xmlFile);
        }
    }

    public static bool RemoveAutostart()
    {
        return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
    }

    public static bool StartElevatedThroughTask()
    {
        return RunSchtasks($"/Run /TN \"{TaskName}\"") == 0;
    }

    public static bool RestartAsAdministrator(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process == null)
            {
                return -1;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }
}
