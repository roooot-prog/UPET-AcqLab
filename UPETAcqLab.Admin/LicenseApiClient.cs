using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UPETAcqLab.Admin;

internal sealed class LicenseApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl { get; }
    public string Password { get; }

    public LicenseApiClient(string baseUrl, string password)
    {
        BaseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        Password = password ?? "";
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        using var http = Create();
        using var resp = await http.GetAsync(BaseUrl + "/api/admin/ping", ct).ConfigureAwait(false);
        await ThrowIfFailed(resp).ConfigureAwait(false);
    }

    public async Task<string> GenerateKeyAsync(CancellationToken ct = default)
    {
        using var http = Create();
        using var resp = await http.PostAsync(BaseUrl + "/api/admin/keys", content: null, ct).ConfigureAwait(false);
        await ThrowIfFailed(resp).ConfigureAwait(false);
        var dto = await resp.Content.ReadFromJsonAsync<CreatedKeyDto>(JsonOpts, ct).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Key))
            throw new InvalidOperationException("Serverul nu a returnat cheia.");
        return dto.Key;
    }

    public async Task<ActivationsDto> ListAsync(CancellationToken ct = default)
    {
        using var http = Create();
        using var resp = await http.GetAsync(BaseUrl + "/api/admin/activations", ct).ConfigureAwait(false);
        await ThrowIfFailed(resp).ConfigureAwait(false);
        var dto = await resp.Content.ReadFromJsonAsync<ActivationsDto>(JsonOpts, ct).ConfigureAwait(false);
        return dto ?? new ActivationsDto();
    }

    private HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "UPETAcqLab.Admin");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Admin-Password", Password);
        return http;
    }

    private static async Task ThrowIfFailed(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        string msg;
        try
        {
            using var doc = JsonDocument.Parse(text);
            msg = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? text : text;
        }
        catch
        {
            msg = string.IsNullOrWhiteSpace(text) ? resp.ReasonPhrase ?? resp.StatusCode.ToString() : text;
        }
        throw new InvalidOperationException(msg);
    }

    private sealed class CreatedKeyDto
    {
        public string? Key { get; set; }
    }
}

internal sealed class ActivationsDto
{
    public int InstallCount { get; set; }
    public List<ActivationDto> Activations { get; set; } = new();
}

internal sealed class ActivationDto
{
    public string KeyMasked { get; set; } = "";
    public string KeyLast4 { get; set; } = "";
    public string RemoteIp { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string Status { get; set; } = "";

    [JsonIgnore]
    public string FirstSeenLocal => FormatLocal(FirstSeenUtc);

    [JsonIgnore]
    public string LastSeenLocal => FormatLocal(LastSeenUtc);

    [JsonIgnore]
    public string StatusRo => Status switch
    {
        "online" => "Online",
        "recent" => "Recent",
        _ => "Offline"
    };

    private static string FormatLocal(DateTime utc)
    {
        if (utc.Year < 2000) return "—";
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("yyyy-MM-dd HH:mm");
    }
}
