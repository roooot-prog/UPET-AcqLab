using Microsoft.Data.Sqlite;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace UPETAcqLab.LicenseApi;

internal sealed class LicenseDb : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LicenseDb(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        Init();
    }

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        var conn = new SqliteConnection(cs.ToString());
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void Init()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS licenses (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              key_hash TEXT NOT NULL UNIQUE,
              key_last4 TEXT NOT NULL,
              created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS activations (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              license_id INTEGER NOT NULL,
              machine_id_hash TEXT NOT NULL,
              hostname TEXT,
              remote_ip TEXT,
              first_seen_utc TEXT NOT NULL,
              last_seen_utc TEXT NOT NULL,
              UNIQUE(license_id, machine_id_hash),
              FOREIGN KEY(license_id) REFERENCES licenses(id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<(string last4, DateTime createdUtc)> CreateLicenseAsync(string keyHash, string last4, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow.ToString("O");
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO licenses (key_hash, key_last4, created_utc)
                VALUES ($h, $l, $t);
                """;
            cmd.Parameters.AddWithValue("$h", keyHash);
            cmd.Parameters.AddWithValue("$l", last4);
            cmd.Parameters.AddWithValue("$t", now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return (last4, DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HeartbeatOutcome> HeartbeatAsync(string keyHash, string machineIdHash, string? hostname, string? remoteIp, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            long licenseId;
            using (var find = conn.CreateCommand())
            {
                find.CommandText = "SELECT id FROM licenses WHERE key_hash = $h LIMIT 1;";
                find.Parameters.AddWithValue("$h", keyHash);
                var obj = await find.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (obj is null || obj is DBNull)
                    return HeartbeatOutcome.UnknownKey;
                licenseId = Convert.ToInt64(obj);
            }

            var now = DateTime.UtcNow.ToString("O");
            using var up = conn.CreateCommand();
            up.CommandText = """
                INSERT INTO activations (license_id, machine_id_hash, hostname, remote_ip, first_seen_utc, last_seen_utc)
                VALUES ($lid, $mid, $host, $ip, $now, $now)
                ON CONFLICT(license_id, machine_id_hash) DO UPDATE SET
                  hostname = excluded.hostname,
                  remote_ip = excluded.remote_ip,
                  last_seen_utc = excluded.last_seen_utc;
                """;
            up.Parameters.AddWithValue("$lid", licenseId);
            up.Parameters.AddWithValue("$mid", machineIdHash);
            up.Parameters.AddWithValue("$host", (object?)hostname ?? DBNull.Value);
            up.Parameters.AddWithValue("$ip", (object?)remoteIp ?? DBNull.Value);
            up.Parameters.AddWithValue("$now", now);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return HeartbeatOutcome.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AdminSnapshot> SnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = Open();
            int installCount;
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(DISTINCT machine_id_hash) FROM activations;";
                installCount = Convert.ToInt32(await c.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0);
            }

            var rows = new List<ActivationRow>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = """
                    SELECT l.key_last4, a.remote_ip, a.hostname, a.first_seen_utc, a.last_seen_utc
                    FROM activations a
                    JOIN licenses l ON l.id = a.license_id
                    ORDER BY a.last_seen_utc DESC;
                    """;
                using var r = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var now = DateTime.UtcNow;
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    var last4 = r.GetString(0);
                    var ip = r.IsDBNull(1) ? "" : r.GetString(1);
                    var host = r.IsDBNull(2) ? "" : r.GetString(2);
                    var first = ParseUtc(r.GetString(3));
                    var last = ParseUtc(r.GetString(4));
                    rows.Add(new ActivationRow(
                        $"UPET-APP-••••-••••-••••-{last4}",
                        last4,
                        ip,
                        host,
                        first,
                        last,
                        StatusFor(now, last)));
                }
            }

            return new AdminSnapshot(installCount, rows);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DateTime ParseUtc(string s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? (dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : DateTime.MinValue;

    private static string StatusFor(DateTime now, DateTime last)
    {
        var age = now - last;
        if (age <= TimeSpan.FromMinutes(15)) return "online";
        if (age <= TimeSpan.FromHours(24)) return "recent";
        return "offline";
    }

    public void Dispose() => _gate.Dispose();
}

internal enum HeartbeatOutcome { Ok, UnknownKey }

internal sealed record ActivationRow(
    string KeyMasked,
    string KeyLast4,
    string RemoteIp,
    string Hostname,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string Status);

internal sealed record AdminSnapshot(int InstallCount, IReadOnlyList<ActivationRow> Activations);

internal static class KeyFactory
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GeneratePlain()
    {
        var data = RandomNumberGenerator.GetBytes(10);
        var body = Encode(data, 16);
        return $"UPET-APP-{body[..4]}-{body[4..8]}-{body[8..12]}-{body[12..16]}";
    }

    public static string Normalize(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? ""
            : key.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace('_', '-');

    public static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(key)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Last4(string key)
    {
        var n = Normalize(key).Replace("-", "", StringComparison.Ordinal);
        return n.Length < 4 ? n : n[^4..];
    }

    private static string Encode(byte[] data, int charCount)
    {
        var bits = 0;
        var value = 0;
        var sb = new StringBuilder(charCount);
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5 && sb.Length < charCount)
            {
                bits -= 5;
                sb.Append(Alphabet[(value >> bits) & 31]);
            }
        }
        if (sb.Length < charCount && bits > 0)
            sb.Append(Alphabet[(value << (5 - bits)) & 31]);
        while (sb.Length < charCount) sb.Append(Alphabet[0]);
        return sb.ToString()[..charCount];
    }
}

internal static class ClientIp
{
    public static string? From(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IPv6Loopback.Equals(ip))
            return IPAddress.Loopback.ToString();
        return ip.ToString();
    }
}
