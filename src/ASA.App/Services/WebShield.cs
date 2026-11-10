using System.Diagnostics;
using System.Text;
using Asa.Core;

namespace Asa.App.Services;

public sealed class WebShield
{
    private static readonly string[] BlockListUrls =
    {
        "https://urlhaus.abuse.ch/downloads/hostfile/"
    };

    private readonly HttpClient httpClient;
    private readonly EventJournal journal;

    public WebShield(HttpClient httpClient, EventJournal journal)
    {
        this.httpClient = httpClient;
        this.journal = journal;
    }

    public static string HostsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public int GetBlockedDomainCount()
    {
        try
        {
            return HostsBlocker.CountBlockedDomains(File.ReadAllText(HostsFilePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public async Task<int> UpdateAsync(CancellationToken cancellationToken)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string url in BlockListUrls)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ASA-Antivirus/1.0");
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (string domain in HostsBlocker.ParseDomainList(text))
            {
                domains.Add(domain);
            }
        }

        if (domains.Count == 0)
        {
            throw new InvalidOperationException("Список опасных сайтов пустой, hosts не изменён");
        }

        string current = File.ReadAllText(HostsFilePath);
        string updated = HostsBlocker.ApplyBlockList(current, domains.OrderBy(domain => domain, StringComparer.Ordinal));
        WriteHosts(updated);
        FlushDnsCache();
        journal.Information("Веб-щит обновлён: заблокировано сайтов " + domains.Count);
        return domains.Count;
    }

    public void Disable()
    {
        string current = File.ReadAllText(HostsFilePath);
        WriteHosts(HostsBlocker.RemoveBlockList(current));
        FlushDnsCache();
        journal.Information("Веб-щит выключен, блок-лист убран из hosts");
    }

    private static void WriteHosts(string content)
    {
        string backup = HostsFilePath + ".asa-backup";
        if (!File.Exists(backup))
        {
            File.Copy(HostsFilePath, backup);
        }

        FileAttributes attributes = File.GetAttributes(HostsFilePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(HostsFilePath, attributes & ~FileAttributes.ReadOnly);
        }

        File.WriteAllText(HostsFilePath, content, new UTF8Encoding(false));
    }

    private static void FlushDnsCache()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false });
            process?.WaitForExit(10000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
