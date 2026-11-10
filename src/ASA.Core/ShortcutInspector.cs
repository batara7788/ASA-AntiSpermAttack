using System.Text;

namespace Asa.Core;

public static class ShortcutInspector
{
    private static readonly string[] DangerousMarkers =
    {
        "powershell", "pwsh", "cmd.exe", "cmd /c", "mshta", "wscript", "cscript", "rundll32", "regsvr32",
        "certutil", "bitsadmin", "-encodedcommand", "-enc ", "frombase64string", "downloadstring",
        "invoke-webrequest", "iwr ", "curl ", "-windowstyle hidden", "-w hidden", "http://", "https://"
    };

    public static IReadOnlyList<string> ExtractStrings(byte[] content, int minimumLength = 5)
    {
        var results = new List<string>();
        var current = new StringBuilder();

        for (int index = 0; index + 1 < content.Length; index += 2)
        {
            char character = (char)(content[index] | (content[index + 1] << 8));
            if (character >= 0x20 && character < 0xD800 && !char.IsControl(character))
            {
                current.Append(character);
            }
            else
            {
                Flush();
            }
        }

        Flush();

        current.Clear();
        foreach (byte value in content)
        {
            if (value >= 0x20 && value < 0x7F)
            {
                current.Append((char)value);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return results;

        void Flush()
        {
            if (current.Length >= minimumLength)
            {
                results.Add(current.ToString());
            }

            current.Clear();
        }
    }

    public static IReadOnlyList<string> FindDangerousMarkers(string text)
    {
        string lower = text.ToLowerInvariant();
        return DangerousMarkers.Where(marker => lower.Contains(marker)).Distinct().ToList();
    }

    public static IReadOnlyList<string> InspectShortcut(byte[] content)
    {
        string joined = string.Join(" ", ExtractStrings(content));
        return FindDangerousMarkers(joined);
    }
}
