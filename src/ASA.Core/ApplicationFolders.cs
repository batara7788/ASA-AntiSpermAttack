namespace Asa.Core;

public sealed class ApplicationFolders
{
    public ApplicationFolders(string rootFolder)
    {
        RootFolder = rootFolder;
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(QuarantineFolder);
        Directory.CreateDirectory(ClamAvFolder);
        Directory.CreateDirectory(ClamAvDatabaseFolder);
        Directory.CreateDirectory(LogsFolder);
    }

    public string RootFolder { get; }

    public string QuarantineFolder => Path.Combine(RootFolder, "quarantine");

    public string ClamAvFolder => Path.Combine(RootFolder, "clamav");

    public string ClamAvDatabaseFolder => Path.Combine(ClamAvFolder, "database");

    public string LogsFolder => Path.Combine(RootFolder, "logs");

    public string SettingsFile => Path.Combine(RootFolder, "settings.json");

    public string StatisticsFile => Path.Combine(RootFolder, "statistics.json");

    public string AutorunSnapshotFile => Path.Combine(RootFolder, "autorun-snapshot.json");

    public string JournalFile => Path.Combine(LogsFolder, "journal.log");

    public static ApplicationFolders CreateDefault()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return new ApplicationFolders(Path.Combine(programData, "ASA"));
    }
}
