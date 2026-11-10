using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Asa.Core;

public enum ScanVerdict
{
    Clean,
    Infected,
    Skipped,
    Error
}

public sealed record ScanResult(string Path, ScanVerdict Verdict, string Details)
{
    public static ScanResult Clean(string path) => new(path, ScanVerdict.Clean, string.Empty);

    public static ScanResult Infected(string path, string threatName) => new(path, ScanVerdict.Infected, threatName);

    public static ScanResult Skipped(string path, string reason) => new(path, ScanVerdict.Skipped, reason);

    public static ScanResult Failed(string path, string error) => new(path, ScanVerdict.Error, error);
}

public sealed class ClamDaemonClient
{
    private const int ChunkSize = 64 * 1024;
    private readonly string host;
    private readonly int port;

    public ClamDaemonClient(string host = "127.0.0.1", int port = 3310)
    {
        this.host = host;
        this.port = port;
    }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string answer = await SendSimpleCommandAsync("zPING", cancellationToken);
            return answer == "PONG";
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return SendSimpleCommandAsync("zVERSION", cancellationToken);
    }

    public Task<string> ReloadDatabaseAsync(CancellationToken cancellationToken = default)
    {
        return SendSimpleCommandAsync("zRELOAD", cancellationToken);
    }

    public async Task<ScanResult> ScanFileAsync(string path, long maximumFileSizeBytes, CancellationToken cancellationToken = default)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return ScanResult.Skipped(path, "Файл не найден");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ScanResult.Failed(path, exception.Message);
        }

        if (fileInfo.Length > maximumFileSizeBytes)
        {
            return ScanResult.Skipped(path, "Файл слишком большой");
        }

        try
        {
            await using FileStream fileStream = await OpenSharedAsync(path, cancellationToken);
            return await ScanStreamAsync(path, fileStream, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ScanResult.Failed(path, exception.Message);
        }
    }

    public async Task<ScanResult> ScanStreamAsync(string displayName, Stream content, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutSource.Token);
            await using NetworkStream network = client.GetStream();

            await network.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), timeoutSource.Token);

            byte[] buffer = new byte[ChunkSize];
            byte[] lengthPrefix = new byte[4];
            int bytesRead;
            while ((bytesRead = await content.ReadAsync(buffer, timeoutSource.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)bytesRead);
                await network.WriteAsync(lengthPrefix, timeoutSource.Token);
                await network.WriteAsync(buffer.AsMemory(0, bytesRead), timeoutSource.Token);
            }

            BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, 0);
            await network.WriteAsync(lengthPrefix, timeoutSource.Token);

            string answer = await ReadAnswerAsync(network, timeoutSource.Token);
            return ParseScanAnswer(displayName, answer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ScanResult.Failed(displayName, "Проверка заняла слишком много времени");
        }
        catch (SocketException exception)
        {
            return ScanResult.Failed(displayName, "Движок ClamAV недоступен: " + exception.Message);
        }
    }

    public static ScanResult ParseScanAnswer(string path, string answer)
    {
        string text = answer.Trim();
        if (text.StartsWith("stream:", StringComparison.Ordinal))
        {
            text = text.Substring("stream:".Length).Trim();
        }

        if (text == "OK")
        {
            return ScanResult.Clean(path);
        }

        if (text.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            string threatName = text.Substring(0, text.Length - " FOUND".Length).Trim();
            return ScanResult.Infected(path, threatName);
        }

        if (text.Contains("size limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return ScanResult.Skipped(path, "Файл больше лимита движка");
        }

        return ScanResult.Failed(path, text);
    }

    private static async Task<FileStream> OpenSharedAsync(string path, CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ChunkSize, true);
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(300 * attempt, cancellationToken);
            }
        }
    }

    private async Task<string> SendSimpleCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeoutSource.Token);
        await using NetworkStream network = client.GetStream();
        await network.WriteAsync(Encoding.ASCII.GetBytes(command + "\0"), timeoutSource.Token);
        string answer = await ReadAnswerAsync(network, timeoutSource.Token);
        return answer.Trim();
    }

    private static async Task<string> ReadAnswerAsync(NetworkStream network, CancellationToken cancellationToken)
    {
        using var answer = new MemoryStream();
        byte[] buffer = new byte[1024];
        int bytesRead;
        while ((bytesRead = await network.ReadAsync(buffer, cancellationToken)) > 0)
        {
            int terminatorIndex = Array.IndexOf(buffer, (byte)0, 0, bytesRead);
            if (terminatorIndex >= 0)
            {
                answer.Write(buffer, 0, terminatorIndex);
                break;
            }

            answer.Write(buffer, 0, bytesRead);
        }

        return Encoding.UTF8.GetString(answer.ToArray());
    }
}
