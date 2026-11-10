using System.Text;

namespace Asa.Core;

public static class ClamAvConfiguration
{
    public const int DaemonPort = 3310;

    public static string BuildDaemonConfig(string databaseFolder, string logFile, long maximumFileSizeMegabytes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TCPSocket " + DaemonPort);
        builder.AppendLine("TCPAddr 127.0.0.1");
        builder.AppendLine("Foreground yes");
        builder.AppendLine("DatabaseDirectory " + databaseFolder);
        builder.AppendLine("LogFile " + logFile);
        builder.AppendLine("LogFileMaxSize 5M");
        builder.AppendLine("LogTime yes");
        builder.AppendLine("MaxThreads 4");
        builder.AppendLine("StreamMaxLength " + maximumFileSizeMegabytes + "M");
        builder.AppendLine("MaxFileSize " + maximumFileSizeMegabytes + "M");
        builder.AppendLine("MaxScanSize " + (maximumFileSizeMegabytes * 4) + "M");
        builder.AppendLine("ScanPE yes");
        builder.AppendLine("ScanOLE2 yes");
        builder.AppendLine("ScanPDF yes");
        builder.AppendLine("ScanHTML yes");
        builder.AppendLine("ScanArchive yes");
        builder.AppendLine("AlertBrokenExecutables no");
        builder.AppendLine("HeuristicAlerts yes");
        builder.AppendLine("SelfCheck 3600");
        return builder.ToString();
    }

    public static string BuildUpdaterConfig(string databaseFolder, string logFile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DatabaseDirectory " + databaseFolder);
        builder.AppendLine("UpdateLogFile " + logFile);
        builder.AppendLine("LogTime yes");
        builder.AppendLine("DatabaseMirror database.clamav.net");
        builder.AppendLine("Checks 1");
        builder.AppendLine("ConnectTimeout 30");
        builder.AppendLine("ReceiveTimeout 300");
        return builder.ToString();
    }
}
