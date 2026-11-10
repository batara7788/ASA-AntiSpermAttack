using System.Xml.Linq;

namespace Asa.Core;

public sealed record ScheduledTaskAction(string TaskName, string Command, string Arguments, bool Hidden, string Author);

public static class ScheduledTaskInspector
{
    public static IReadOnlyList<ScheduledTaskAction> ParseTaskXml(string taskName, string xml)
    {
        var results = new List<ScheduledTaskAction>();
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return results;
        }

        XNamespace taskNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
        bool hidden = string.Equals(document.Descendants(taskNamespace + "Hidden").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase);
        string author = document.Descendants(taskNamespace + "Author").FirstOrDefault()?.Value ?? string.Empty;

        foreach (XElement exec in document.Descendants(taskNamespace + "Exec"))
        {
            string command = exec.Element(taskNamespace + "Command")?.Value ?? string.Empty;
            string arguments = exec.Element(taskNamespace + "Arguments")?.Value ?? string.Empty;
            results.Add(new ScheduledTaskAction(taskName, command.Trim().Trim('"'), arguments.Trim(), hidden, author));
        }

        return results;
    }

    public static IReadOnlyList<RiskReason> Assess(ScheduledTaskAction action)
    {
        var reasons = new List<RiskReason>();
        string command = Environment.ExpandEnvironmentVariables(action.Command);
        string full = command + " " + action.Arguments;
        string lowerCommand = command.ToLowerInvariant().Replace('/', '\\');

        IReadOnlyList<string> markers = ShortcutInspector.FindDangerousMarkers(full);
        bool trustedVendorFolder = lowerCommand.Contains("\\program files") || lowerCommand.Contains("\\windows\\system32\\") && markers.Count == 0;
        if (markers.Count > 0)
        {
            reasons.Add(new RiskReason(40, "Запускает команды: " + string.Join(", ", markers.Take(4))));
        }

        if (lowerCommand.Contains("\\appdata\\") || lowerCommand.Contains("\\temp\\") || lowerCommand.Contains("\\users\\public\\") || lowerCommand.Contains("\\programdata\\") && !lowerCommand.Contains("\\microsoft\\"))
        {
            reasons.Add(new RiskReason(30, "Запускает программу из пользовательской или временной папки"));
        }

        if (action.Hidden && !trustedVendorFolder)
        {
            reasons.Add(new RiskReason(15, "Задача скрыта"));
        }

        if (command.Length > 0 && !command.Contains('\\') && !command.Contains('%') && markers.Count == 0 && !trustedVendorFolder)
        {
            reasons.Add(new RiskReason(5, "Команда без полного пути"));
        }

        return reasons;
    }
}

public static class DefenderOutput
{
    public static string? ParseThreatName(string output)
    {
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            const string marker = "Threat";
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
            {
                string name = line.Substring(line.IndexOf(':') + 1).Trim();
                if (name.Length > 0)
                {
                    return name;
                }
            }
        }

        return null;
    }
}

public static class SmartScanPlanner
{
    public static IReadOnlyList<FileRisk> RankFiles(IEnumerable<FileFacts> files, DateTime nowUtc)
    {
        return files
            .Select(facts => FileRiskAnalyzer.Analyze(facts, nowUtc))
            .OrderByDescending(risk => risk.Score)
            .ThenBy(risk => risk.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
