using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Asa.Core;

public sealed record VirusTotalReport(bool Known, int Malicious, int Suspicious, int Harmless, int Undetected, string Permalink, string PopularThreatName);

public sealed class VirusTotalClient
{
    private const string ApiBase = "https://www.virustotal.com/api/v3/";
    private readonly HttpClient httpClient;
    private readonly string apiKey;

    public VirusTotalClient(HttpClient httpClient, string apiKey)
    {
        this.httpClient = httpClient;
        this.apiKey = apiKey;
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public async Task<VirusTotalReport> LookupHashAsync(string sha256, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + "files/" + sha256);
        request.Headers.Add("x-apikey", apiKey);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new VirusTotalReport(false, 0, 0, 0, 0, "https://www.virustotal.com/gui/file/" + sha256, string.Empty);
        }

        await EnsureSuccessAsync(response, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseFileReport(json, sha256);
    }

    public async Task<string> UploadFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + "files") { Content = form };
        request.Headers.Add("x-apikey", apiKey);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("data").GetProperty("id").GetString() ?? string.Empty;
    }

    public static VirusTotalReport ParseFileReport(string json, string sha256)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement attributes = document.RootElement.GetProperty("data").GetProperty("attributes");
        JsonElement stats = attributes.GetProperty("last_analysis_stats");

        string threatName = string.Empty;
        if (attributes.TryGetProperty("popular_threat_classification", out JsonElement classification)
            && classification.TryGetProperty("suggested_threat_label", out JsonElement label))
        {
            threatName = label.GetString() ?? string.Empty;
        }

        return new VirusTotalReport(
            true,
            ReadInt(stats, "malicious"),
            ReadInt(stats, "suspicious"),
            ReadInt(stats, "harmless"),
            ReadInt(stats, "undetected"),
            "https://www.virustotal.com/gui/file/" + sha256,
            threatName);
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Неверный ключ VirusTotal",
            HttpStatusCode.Forbidden => "Ключ VirusTotal не даёт доступа к этой операции",
            HttpStatusCode.TooManyRequests => "Превышен лимит запросов VirusTotal (бесплатный ключ: 4 в минуту)",
            _ => "VirusTotal ответил ошибкой " + (int)response.StatusCode
        };

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(message + (body.Length > 0 && body.Length < 300 ? ": " + body : string.Empty));
    }
}
