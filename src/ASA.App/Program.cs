using Asa.App.Interface;
using Asa.App.Services;

namespace Asa.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        if (arguments.Contains("--uninstall"))
        {
            Uninstaller.Run();
            return;
        }

        string command = BuildCommand(arguments);
        bool startInBackground = arguments.Contains("--background");

        if (SingleInstance.IsAnotherInstanceRunning())
        {
            SingleInstance.SendCommand(command, TimeSpan.FromSeconds(5));
            return;
        }

        if (!SystemIntegration.IsAdministrator())
        {
            if (SystemIntegration.StartElevatedThroughTask())
            {
                WaitAndForward(command);
                return;
            }

            string forwardedArguments = string.Join(" ", arguments.Select(argument => "\"" + argument + "\""));
            if (!SystemIntegration.RestartAsAdministrator(forwardedArguments))
            {
                MessageBox.Show("ASA нужны права администратора, чтобы защищать компьютер.", "ASA: AntiSpermAttack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return;
        }

        using SingleInstance? instance = SingleInstance.TryAcquire();
        if (instance == null)
        {
            SingleInstance.SendCommand(command, TimeSpan.FromSeconds(5));
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var host = new ProtectionHost();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => host.Journal.Warning("Внутренняя ошибка: " + eventArgs.Exception.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => host.Journal.Warning("Критическая ошибка: " + eventArgs.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            host.Journal.Warning("Ошибка фоновой задачи: " + eventArgs.Exception.GetBaseException().Message);
            eventArgs.SetObserved();
        };
        using var mainForm = new MainForm(host, startInBackground);
        instance.CommandReceived += received => mainForm.HandleExternalCommand(received);
        instance.StartListening();

        if (command.StartsWith("SCAN\t", StringComparison.Ordinal))
        {
            mainForm.HandleExternalCommand(command);
        }

        Application.Run(mainForm);
    }

    private static string BuildCommand(string[] arguments)
    {
        int scanIndex = Array.IndexOf(arguments, "--scan");
        if (scanIndex >= 0 && scanIndex + 1 < arguments.Length)
        {
            return "SCAN\t" + arguments[scanIndex + 1];
        }

        return arguments.Contains("--background") ? "NONE" : "SHOW";
    }

    private static void WaitAndForward(string command)
    {
        if (command == "NONE")
        {
            return;
        }

        DateTime deadline = DateTime.Now.AddSeconds(20);
        while (DateTime.Now < deadline)
        {
            if (SingleInstance.SendCommand(command, TimeSpan.FromSeconds(1)))
            {
                return;
            }

            Thread.Sleep(500);
        }
    }
}
