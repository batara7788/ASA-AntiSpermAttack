using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Asa.App.Services;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "Global\\ASA-AntiSpermAttack-Instance";
    private const string PipeName = "ASA-AntiSpermAttack-Commands";
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stopSource = new();

    private SingleInstance(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public event Action<string>? CommandReceived;

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    public static bool IsAnotherInstanceRunning()
    {
        try
        {
            return Mutex.TryOpenExisting(MutexName, out Mutex? existing) && DisposeAndReturnTrue(existing);
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool SendCommand(string command, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            using var writer = new StreamWriter(client);
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartListening()
    {
        Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        stopSource.Cancel();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private static bool DisposeAndReturnTrue(Mutex mutex)
    {
        mutex.Dispose();
        return true;
    }

    private async Task ListenAsync()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));

        while (!stopSource.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await server.WaitForConnectionAsync(stopSource.Token);
                using var reader = new StreamReader(server);
                string? line = await reader.ReadLineAsync(stopSource.Token);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    CommandReceived?.Invoke(line);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(500);
            }
        }
    }
}
