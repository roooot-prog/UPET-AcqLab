using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using UPETAcqLab.LicenseApi;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var localDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "UPETAcqLab.LicenseApi");
Directory.CreateDirectory(localDir);
var localSettingsPath = Path.Combine(localDir, "settings.json");
EnsureLocalSettingsTemplate(localSettingsPath);

builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: true);

var listenUrl = FirstNonEmpty(
    Environment.GetEnvironmentVariable("UPET_LICENSE_LISTEN_URL"),
    builder.Configuration["ListenUrl"],
    builder.Configuration["Urls"],
    "http://127.0.0.1:5088")!;

builder.WebHost.UseUrls(listenUrl);

var dbPath = FirstNonEmpty(
    Environment.GetEnvironmentVariable("UPET_LICENSE_DB"),
    builder.Configuration["DatabasePath"],
    Path.Combine(localDir, "licenses.db"))!;

var db = new LicenseDb(dbPath);
builder.Services.AddSingleton(db);

var app = builder.Build();

var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwarded.KnownNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "UPETAcqLab.LicenseApi" }));

app.MapPost("/api/v1/heartbeat", async (HeartbeatBody body, HttpContext http, LicenseDb store, CancellationToken ct) =>
{
    var keyHash = (body.KeyHash ?? "").Trim().ToLowerInvariant();
    var machine = (body.MachineIdHash ?? "").Trim().ToLowerInvariant();
    if (keyHash.Length != 64 || machine.Length < 16)
        return Results.Json(new { error = "Cerere invalidă." }, statusCode: 400);

    var host = Truncate((body.Hostname ?? "").Trim(), 128);
    var ip = ClientIp.From(http);
    var outcome = await store.HeartbeatAsync(keyHash, machine, host, ip, ct).ConfigureAwait(false);
    if (outcome == HeartbeatOutcome.UnknownKey)
        return Results.Json(new { error = "Cheie necunoscută." }, statusCode: 404);
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/admin/keys", async (HttpContext http, LicenseDb store, IConfiguration config, CancellationToken ct) =>
{
    if (!AdminOk(http, config, out var err))
        return err;

    var plain = KeyFactory.GeneratePlain();
    var hash = KeyFactory.HashKey(plain);
    var last4 = KeyFactory.Last4(plain);
    await store.CreateLicenseAsync(hash, last4, ct).ConfigureAwait(false);
    // Raw key is returned once in the JSON body. It is not written to logs or SQLite.
    return Results.Ok(new
    {
        key = plain,
        keyMasked = $"UPET-APP-••••-••••-••••-{last4}",
        shownOnce = true
    });
});

app.MapGet("/api/admin/activations", async (HttpContext http, LicenseDb store, IConfiguration config, CancellationToken ct) =>
{
    if (!AdminOk(http, config, out var err))
        return err;
    var snap = await store.SnapshotAsync(ct).ConfigureAwait(false);
    return Results.Ok(new
    {
        installCount = snap.InstallCount,
        activations = snap.Activations.Select(a => new
        {
            keyMasked = a.KeyMasked,
            keyLast4 = a.KeyLast4,
            remoteIp = a.RemoteIp,
            hostname = a.Hostname,
            firstSeenUtc = a.FirstSeenUtc,
            lastSeenUtc = a.LastSeenUtc,
            status = a.Status
        })
    });
});

app.MapGet("/api/admin/ping", (HttpContext http, IConfiguration config) =>
{
    if (!AdminOk(http, config, out var err))
        return err;
    return Results.Ok(new { ok = true });
});

app.Logger.LogInformation("UPETAcqLab.LicenseApi ascultă pe {Url}", listenUrl);
app.Logger.LogInformation("SQLite: {Db}", dbPath);
app.Logger.LogInformation("Parola admin (local, nu GitHub): {Path}", localSettingsPath);

app.Run();

static bool AdminOk(HttpContext http, IConfiguration config, out IResult error)
{
    var expected = FirstNonEmpty(
        Environment.GetEnvironmentVariable("UPET_LICENSE_ADMIN_PASSWORD"),
        config["AdminPassword"]) ?? "";

    if (string.IsNullOrWhiteSpace(expected))
    {
        error = Results.Json(new
        {
            error = "Parola de administrare nu este setată. Completați AdminPassword în fișierul local de setări (nu pe GitHub)."
        }, statusCode: 503);
        return false;
    }

    http.Request.Headers.TryGetValue("X-Admin-Password", out var offered);
    var offeredStr = offered.ToString() ?? "";
    if (!FixedEquals(expected, offeredStr))
    {
        error = Results.Json(new { error = "Parolă de administrare invalidă." }, statusCode: 401);
        return false;
    }

    error = Results.Empty;
    return true;
}

static bool FixedEquals(string a, string b)
{
    var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
    var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
    return CryptographicOperations.FixedTimeEquals(ha, hb);
}

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var v in values)
    {
        if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
    }
    return null;
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max];

static void EnsureLocalSettingsTemplate(string path)
{
    if (File.Exists(path)) return;
    var json = """
        {
          "AdminPassword": "",
          "ListenUrl": "http://127.0.0.1:5088"
        }
        """;
    File.WriteAllText(path, json + Environment.NewLine);
}

internal sealed class HeartbeatBody
{
    public string? KeyHash { get; set; }
    public string? MachineIdHash { get; set; }
    public string? Hostname { get; set; }
}
