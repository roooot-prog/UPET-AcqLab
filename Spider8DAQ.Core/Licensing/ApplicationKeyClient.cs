using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spider8DAQ.Core.Journal;

namespace Spider8DAQ.Core.Licensing;

public enum ApplicationKeyHeartbeatResult
{
    Ok,
    Invalid,
    Unreachable,
    Expired,
    Revoked
}

public enum ApplicationKeyGateReason
{
    Pending,
    Expired,
    Revoked
}

public sealed class ApplicationKeyHeartbeatDetail
{
    public ApplicationKeyHeartbeatResult Result { get; init; } = ApplicationKeyHeartbeatResult.Unreachable;
    public bool UpdateRequested { get; init; }
    public string? TargetVersion { get; init; }
    public bool JournalRequested { get; init; }
    public string? AdminMessage { get; init; }
    public bool StopRecRequested { get; init; }
    public bool ZeroRequested { get; init; }

    public static ApplicationKeyHeartbeatDetail From(ApplicationKeyHeartbeatResult result) =>
        new() { Result = result };

    /// <summary>API sends both <c>pendingMessage</c> and <c>adminMessage</c>; either is enough.</summary>
    public static string? CoalescePendingMessage(string? pendingMessage, string? adminMessage)
    {
        var pending = (pendingMessage ?? "").Trim();
        if (pending.Length > 0) return pending;
        var admin = (adminMessage ?? "").Trim();
        return admin.Length > 0 ? admin : null;
    }
}

public sealed class ApplicationKeyLicenseStatus
{
    public string Status { get; set; } = "unknown";
    public string? Key { get; set; }
    public string? KeyLast4 { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}

public static class ApplicationKeyClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ApplicationKeyHeartbeatResult> HeartbeatAsync(
        string baseUrl,
        string keyHash,
        string machineIdHash,
        string hostname,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(keyHash))
            return ApplicationKeyHeartbeatResult.Invalid;

        var detail = await HeartbeatDetailAsync(
                baseUrl, keyHash, machineIdHash, hostname, timeout,
                updateAck: false, includeScreenshot: true, forceScreenshot: false,
                messageAck: false, stopAck: false, zeroAck: false, cancellationToken)
            .ConfigureAwait(false);
        return detail.Result;
    }

    public static async Task<ApplicationKeyHeartbeatDetail> HeartbeatCachedDetailAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        bool updateAck = false,
        bool forceScreenshot = false,
        bool messageAck = false,
        bool stopAck = false,
        bool zeroAck = false)
    {
        var rec = ApplicationKeyStore.Load();
        if (rec is null || string.IsNullOrWhiteSpace(rec.KeyHash))
            return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Invalid);

        var locallyExpired = ApplicationKeyValidity.IsExpired(rec.ValidUntilUtc);
        var machine = string.IsNullOrWhiteSpace(rec.MachineIdHash)
            ? ApplicationKeyCrypto.HashedMachineId()
            : rec.MachineIdHash;
        var host = string.IsNullOrWhiteSpace(rec.Hostname) ? Environment.MachineName : rec.Hostname;
        var ackOnly = messageAck || stopAck || zeroAck;
        var detail = await HeartbeatDetailAsync(
                baseUrl, rec.KeyHash, machine, host, timeout,
                updateAck: updateAck,
                includeScreenshot: forceScreenshot || (!updateAck && !ackOnly),
                forceScreenshot: forceScreenshot,
                messageAck: messageAck,
                stopAck: stopAck,
                zeroAck: zeroAck,
                cancellationToken)
            .ConfigureAwait(false);

        if (detail.Result == ApplicationKeyHeartbeatResult.Ok)
            return detail;

        if (detail.Result is ApplicationKeyHeartbeatResult.Expired
            or ApplicationKeyHeartbeatResult.Invalid
            or ApplicationKeyHeartbeatResult.Revoked)
        {
            var refreshed = await RefreshFromStatusAsync(baseUrl, timeout, detail.Result, cancellationToken)
                .ConfigureAwait(false);
            return ApplicationKeyHeartbeatDetail.From(refreshed);
        }

        if (locallyExpired)
        {
            var refreshed = await RefreshFromStatusAsync(
                    baseUrl, timeout, ApplicationKeyHeartbeatResult.Expired, cancellationToken)
                .ConfigureAwait(false);
            return ApplicationKeyHeartbeatDetail.From(refreshed);
        }

        return detail;
    }

    public static async Task<bool> LiveCachedAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var rec = ApplicationKeyStore.Load();
        if (rec is null || string.IsNullOrWhiteSpace(rec.KeyHash) || string.IsNullOrWhiteSpace(baseUrl))
            return false;

        try
        {
            using var cts = LinkedTimeout(timeout, cancellationToken);
            var machine = string.IsNullOrWhiteSpace(rec.MachineIdHash)
                ? ApplicationKeyCrypto.HashedMachineId()
                : rec.MachineIdHash;
            var host = string.IsNullOrWhiteSpace(rec.Hostname) ? Environment.MachineName : rec.Hostname;
            var live = CollectLiveOnly();
            var json = JsonSerializer.Serialize(new
            {
                keyHash = rec.KeyHash,
                machineIdHash = machine,
                hostname = host,
                channels = live.Channels,
                rec = live.Session.Rec,
                recSeconds = live.Session.RecSeconds,
                lastFile = live.Session.LastFile,
                alarmCount = live.Session.AlarmCount,
                conn = live.Session.Conn
            }, JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await SharedHttp.PostAsync(
                    baseUrl.TrimEnd('/') + "/api/v1/live", content, cts.Token)
                .ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static long _lastLiveFallbackMs;

    private static CollectedTelemetry CollectLiveOnly()
    {
        var tel = new CollectedTelemetry();
        try
        {
            if (ApplicationKeyLiveCache.TryGet(out var cachedChannels, out var cachedSession))
            {
                tel.Channels = cachedChannels;
                tel.Session = cachedSession;
                return tel;
            }

            var now = Environment.TickCount64;
            if (now - Volatile.Read(ref _lastLiveFallbackMs) < 200)
                return tel;
            Volatile.Write(ref _lastLiveFallbackMs, now);

            var provider = ApplicationKeyLiveSnapshot.Provider;
            if (provider is null) return tel;
            tel.Channels = provider.GetChannels()
                .Where(c => c.On)
                .Take(32)
                .ToArray();
            tel.Session = provider.GetSession() ?? new ApplicationKeySessionInfo();
        }
        catch
        {
            /* never block live */
        }
        return tel;
    }

    public static Task<ApplicationKeyHeartbeatDetail> AckRemoteUpdateAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        HeartbeatCachedDetailAsync(baseUrl, timeout, cancellationToken, updateAck: true);

    private static async Task<ApplicationKeyHeartbeatDetail> HeartbeatDetailAsync(
        string baseUrl,
        string keyHash,
        string machineIdHash,
        string hostname,
        TimeSpan timeout,
        bool updateAck,
        bool includeScreenshot,
        bool forceScreenshot,
        bool messageAck,
        bool stopAck,
        bool zeroAck,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(keyHash))
            return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Invalid);

        try
        {
            using var cts = LinkedTimeout(timeout, cancellationToken);
            var url = baseUrl.TrimEnd('/') + "/api/v1/heartbeat";
            var json = JsonSerializer.Serialize(
                BuildHeartbeatRequest(
                    keyHash, machineIdHash, hostname, includeScreenshot, updateAck, forceScreenshot,
                    messageAck, stopAck, zeroAck),
                JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await SharedHttp.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            var dto = await TryReadHeartbeatBodyAsync(resp, cancellationToken).ConfigureAwait(false);
            var until = CoalesceValidUntil(dto);

            if (IsRevokedStatus(dto))
            {
                PersistValidUntil(until);
                return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Revoked);
            }

            if (IsExpiredStatus(dto) || (resp.IsSuccessStatusCode && ApplicationKeyValidity.IsExpired(until)))
            {
                PersistValidUntil(until);
                return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Expired);
            }

            if (resp.IsSuccessStatusCode)
            {
                if (until is null)
                    return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Expired);
                PersistValidUntil(until);
                return new ApplicationKeyHeartbeatDetail
                {
                    Result = ApplicationKeyHeartbeatResult.Ok,
                    UpdateRequested = dto?.UpdateRequested == true,
                    TargetVersion = string.IsNullOrWhiteSpace(dto?.TargetVersion) ? null : dto!.TargetVersion.Trim(),
                    JournalRequested = dto?.JournalRequested == true,
                    AdminMessage = ApplicationKeyHeartbeatDetail.CoalescePendingMessage(
                        dto?.PendingMessage, dto?.AdminMessage),
                    StopRecRequested = dto?.StopRecRequested == true,
                    ZeroRequested = dto?.ZeroRequested == true
                };
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                if (IsRevokedStatus(dto))
                    return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Revoked);
                return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Invalid);
            }
            return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Unreachable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Unreachable);
        }
        catch
        {
            return ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Unreachable);
        }
    }

    public static async Task<bool> RegisterPendingAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        try
        {
            using var cts = LinkedTimeout(timeout, cancellationToken);
            var url = baseUrl.TrimEnd('/') + "/api/v1/pending";
            var json = JsonSerializer.Serialize(
                BuildPendingRequest(includeScreenshot: false),
                JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await SharedHttp.PostAsync(url, content, cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>POST pending to the first URL that answers (Cloudflare and/or LAN IP).</summary>
    public static async Task<(bool Ok, string? UsedUrl)> RegisterPendingFirstAsync(
        IReadOnlyList<string> baseUrls,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (baseUrls is null || baseUrls.Count == 0)
            return (false, null);

        foreach (var raw in baseUrls)
        {
            var url = (raw ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) continue;
            if (await RegisterPendingAsync(url, timeout, cancellationToken).ConfigureAwait(false))
                return (true, url);
        }

        return (false, null);
    }

    public static async Task<ApplicationKeyLicenseStatus> GetStatusAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new ApplicationKeyLicenseStatus { Status = "unreachable" };

        try
        {
            using var cts = LinkedTimeout(timeout, cancellationToken);
            var machine = Uri.EscapeDataString(ApplicationKeyCrypto.HashedMachineId());
            var url = baseUrl.TrimEnd('/') + "/api/v1/status?machineId=" + machine;
            using var resp = await SharedHttp.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new ApplicationKeyLicenseStatus { Status = "unreachable" };

            var dto = await JsonSerializer.DeserializeAsync<StatusDto>(
                await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                JsonOpts,
                cancellationToken).ConfigureAwait(false);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Status))
                return new ApplicationKeyLicenseStatus { Status = "unknown" };

            var until = dto.ValidUntilUtc ?? dto.ValidUntil;
            var status = dto.Status.Trim().ToLowerInvariant();
            if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase)
                && ApplicationKeyValidity.IsExpired(until))
                status = "expired";

            return new ApplicationKeyLicenseStatus
            {
                Status = status,
                Key = dto.Key,
                KeyLast4 = dto.KeyLast4,
                ValidUntilUtc = until
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApplicationKeyLicenseStatus { Status = "unreachable" };
        }
        catch
        {
            return new ApplicationKeyLicenseStatus { Status = "unreachable" };
        }
    }

    public static async Task<(ApplicationKeyLicenseStatus Status, string? UsedUrl)> GetStatusFirstAsync(
        IReadOnlyList<string> baseUrls,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var unreachable = new ApplicationKeyLicenseStatus { Status = "unreachable" };
        if (baseUrls is null || baseUrls.Count == 0)
            return (unreachable, null);

        foreach (var raw in baseUrls)
        {
            var url = (raw ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) continue;
            var status = await GetStatusAsync(url, timeout, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(status.Status, "unreachable", StringComparison.OrdinalIgnoreCase))
                return (status, url);
        }

        return (unreachable, null);
    }

    public static bool TryAcceptApprovedKey(ApplicationKeyLicenseStatus status)
    {
        if (status is null) return false;
        if (!string.Equals(status.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return false;
        if (ApplicationKeyValidity.IsExpired(status.ValidUntilUtc))
            return false;
        var key = ApplicationKeyCrypto.NormalizeKey(status.Key);
        if (!ApplicationKeyCrypto.LooksLikeApplicationKey(key))
            return false;

        var hash = ApplicationKeyCrypto.HashKey(key);
        var last4 = string.IsNullOrWhiteSpace(status.KeyLast4)
            ? ApplicationKeyCrypto.KeyLast4(key)
            : status.KeyLast4.Trim();
        ApplicationKeyStore.SaveAccepted(
            hash,
            last4,
            ApplicationKeyCrypto.HashedMachineId(),
            Environment.MachineName,
            key,
            ApplicationKeyValidity.ToUtc(status.ValidUntilUtc!.Value));
        return true;
    }

    public static async Task<ApplicationKeyHeartbeatResult> ActivateRawKeyAsync(
        string baseUrl,
        string rawKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var key = ApplicationKeyCrypto.NormalizeKey(rawKey);
        if (!ApplicationKeyCrypto.LooksLikeApplicationKey(key))
            return ApplicationKeyHeartbeatResult.Invalid;

        var hash = ApplicationKeyCrypto.HashKey(key);
        var machine = ApplicationKeyCrypto.HashedMachineId();
        var host = Environment.MachineName;
        var result = await HeartbeatAsync(baseUrl, hash, machine, host, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (result == ApplicationKeyHeartbeatResult.Ok)
            ApplicationKeyStore.SaveAccepted(hash, ApplicationKeyCrypto.KeyLast4(key), machine, host, key);
        return result;
    }

    public static async Task<ApplicationKeyHeartbeatResult> HeartbeatCachedAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var detail = await HeartbeatCachedDetailAsync(baseUrl, timeout, cancellationToken)
            .ConfigureAwait(false);
        return detail.Result;
    }

    private static async Task<ApplicationKeyHeartbeatResult> RefreshFromStatusAsync(
        string baseUrl,
        TimeSpan timeout,
        ApplicationKeyHeartbeatResult fallback,
        CancellationToken cancellationToken)
    {
        var status = await GetStatusAsync(baseUrl, timeout, cancellationToken).ConfigureAwait(false);
        if (TryAcceptApprovedKey(status))
            return ApplicationKeyHeartbeatResult.Ok;
        if (string.Equals(status.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            return ApplicationKeyHeartbeatResult.Revoked;
        if (string.Equals(status.Status, "expired", StringComparison.OrdinalIgnoreCase))
            return ApplicationKeyHeartbeatResult.Expired;
        return fallback;
    }

    /// <summary>
    /// Shared client + pooled sockets so 1 ms live POSTs reuse one TCP connection.
    /// every tick. Per-call CancelAfter still bounds DNS / hung tunnels.
    /// </summary>
    private static readonly HttpClient SharedHttp = CreateSharedHttp();

    private static HttpClient CreateSharedHttp()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 8
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "UPETAcqLab");
        return http;
    }

    private static CancellationTokenSource LinkedTimeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static bool IsExpiredStatus(HeartbeatResponseDto? body) =>
        body is not null && string.Equals(body.Status, "expired", StringComparison.OrdinalIgnoreCase);

    private static bool IsRevokedStatus(HeartbeatResponseDto? body) =>
        body is not null && string.Equals(body.Status, "revoked", StringComparison.OrdinalIgnoreCase);

    private static DateTime? CoalesceValidUntil(HeartbeatResponseDto? dto) =>
        dto?.ValidUntilUtc ?? dto?.ValidUntil;

    private static void PersistValidUntil(DateTime? validUntilUtc)
    {
        if (validUntilUtc is null) return;
        var rec = ApplicationKeyStore.Load();
        if (rec is null || string.IsNullOrWhiteSpace(rec.KeyHash)) return;
        ApplicationKeyStore.SaveAccepted(
            rec.KeyHash,
            rec.KeyLast4,
            rec.MachineIdHash,
            rec.Hostname,
            rawKey: null,
            validUntilUtc: ApplicationKeyValidity.ToUtc(validUntilUtc.Value));
    }

    private static async Task<HeartbeatResponseDto?> TryReadHeartbeatBodyAsync(
        HttpResponseMessage resp,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<HeartbeatResponseDto>(stream, JsonOpts, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime _lastScreenshotUtc = DateTime.MinValue;
    private static readonly TimeSpan ScreenshotMinInterval = TimeSpan.FromMinutes(5);

    private static bool TakeScreenshotNow(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastScreenshotUtc < ScreenshotMinInterval)
            return false;
        _lastScreenshotUtc = now;
        return true;
    }

    private static HeartbeatRequest BuildHeartbeatRequest(
        string keyHash, string machineIdHash, string hostname, bool includeScreenshot, bool updateAck,
        bool forceScreenshot = false, bool messageAck = false, bool stopAck = false, bool zeroAck = false)
    {
        var tel = CollectTelemetry(includeScreenshot && TakeScreenshotNow(forceScreenshot));
        return new HeartbeatRequest
        {
            KeyHash = keyHash,
            MachineIdHash = machineIdHash,
            Hostname = hostname,
            AppVersion = tel.AppVersion,
            MacAddresses = tel.MacAddresses,
            Channels = tel.Channels,
            ScreenshotJpeg = tel.ScreenshotJpeg,
            ScreenshotThumb = tel.ScreenshotThumb,
            Journal = tel.Journal,
            LastError = tel.LastError,
            LastErrorLevel = tel.LastErrorLevel,
            Rec = tel.Session.Rec,
            RecSeconds = tel.Session.RecSeconds,
            LastFile = tel.Session.LastFile,
            AlarmCount = tel.Session.AlarmCount,
            Conn = tel.Session.Conn,
            UpdateAck = updateAck,
            MessageAck = messageAck,
            StopAck = stopAck,
            ZeroAck = zeroAck
        };
    }

    private static PendingRequest BuildPendingRequest(bool includeScreenshot)
    {
        var tel = CollectTelemetry(includeScreenshot);
        return new PendingRequest
        {
            MachineId = ApplicationKeyCrypto.HashedMachineId(),
            Hostname = Environment.MachineName,
            AppVersion = tel.AppVersion,
            MacAddresses = tel.MacAddresses,
            Channels = tel.Channels,
            ScreenshotJpeg = tel.ScreenshotJpeg,
            ScreenshotThumb = tel.ScreenshotThumb,
            Journal = tel.Journal,
            LastError = tel.LastError,
            LastErrorLevel = tel.LastErrorLevel
        };
    }

    private static DateTime _lastJournalUtc = DateTime.MinValue;
    private static DateTime _lastIdentityUtc = DateTime.MinValue;
    private static string _cachedAppVersion = "";
    private static string[]? _cachedMac;
    private static string? _cachedLastError;
    private static string _cachedLastLevel = "Info";
    private static readonly TimeSpan JournalMinInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan IdentityMinInterval = TimeSpan.FromSeconds(5);

    private static CollectedTelemetry CollectTelemetry(bool includeScreenshot)
    {
        var now = DateTime.UtcNow;
        if (now - _lastIdentityUtc >= IdentityMinInterval || _cachedAppVersion.Length == 0)
        {
            _cachedAppVersion = ApplicationKeyIdentity.LocalFileVersion();
            _cachedMac = ApplicationKeyIdentity.LocalMacAddresses().ToArray();
            _lastIdentityUtc = now;
        }

        var tel = new CollectedTelemetry
        {
            AppVersion = _cachedAppVersion,
            MacAddresses = _cachedMac
        };

        try
        {
            var provider = ApplicationKeyLiveSnapshot.Provider;
            IReadOnlyList<ApplicationKeyJournalLine> journal = Array.Empty<ApplicationKeyJournalLine>();
            string? lastError = null;
            string lastLevel = "Info";
            var pullJournal = includeScreenshot || now - _lastJournalUtc >= JournalMinInterval;
            if (provider is not null)
            {
                tel.Channels = provider.GetChannels()
                    .Where(c => c.On)
                    .Take(32)
                    .ToArray();
                tel.Session = provider.GetSession() ?? new ApplicationKeySessionInfo();
                if (pullJournal)
                {
                    journal = provider.GetJournal(ApplicationKeyLiveSnapshot.JournalLines);
                    (lastError, lastLevel) = provider.GetLastError();
                    _lastJournalUtc = now;
                }
                var shot = provider.TryCapture(
                    ApplicationKeyLiveSnapshot.ScreenshotMaxBytes, 12_000, includeScreenshot);
                tel.ScreenshotJpeg = shot.jpeg;
                tel.ScreenshotThumb = shot.thumb;
            }
            else if (pullJournal)
            {
                journal = FromLogEntries(AppJournal.ReadTailFromFile(AppPaths.AppLog, ApplicationKeyLiveSnapshot.JournalLines));
                _lastJournalUtc = now;
            }

            if (pullJournal)
            {
                if (journal.Count == 0)
                    journal = FromLogEntries(AppJournal.ReadTailFromFile(AppPaths.AppLog, ApplicationKeyLiveSnapshot.JournalLines));
                tel.Journal = journal.Count == 0 ? null : journal.ToArray();
                if (string.IsNullOrWhiteSpace(lastError) && tel.Journal is { Length: > 0 })
                {
                    var err = tel.Journal.LastOrDefault(j =>
                        string.Equals(j.Level, "Error", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(j.Level, "Warn", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(j.Level, "Warning", StringComparison.OrdinalIgnoreCase));
                    lastError = err?.Message ?? tel.Journal[^1].Message;
                    lastLevel = err?.Level ?? tel.Journal[^1].Level;
                }
                _cachedLastError = string.IsNullOrWhiteSpace(lastError) ? null : Truncate(lastError, 400);
                _cachedLastLevel = NormalizeLevel(lastLevel);
            }

            tel.LastError = _cachedLastError;
            tel.LastErrorLevel = _cachedLastLevel;
        }
        catch
        {
            /* never block heartbeat */
        }

        return tel;
    }

    private static IReadOnlyList<ApplicationKeyJournalLine> FromLogEntries(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0) return Array.Empty<ApplicationKeyJournalLine>();
        return entries.Select(e => new ApplicationKeyJournalLine
        {
            Ts = e.Timestamp.ToString("O"),
            Level = NormalizeLevel(e.Level.ToString()),
            Message = e.Message ?? ""
        }).ToList();
    }

    private static string NormalizeLevel(string? level)
    {
        if (string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "Warn", StringComparison.OrdinalIgnoreCase))
            return "Warn";
        if (string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase))
            return "Error";
        if (string.Equals(level, "Setup", StringComparison.OrdinalIgnoreCase))
            return "Info";
        return "Info";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private sealed class CollectedTelemetry
    {
        public string AppVersion { get; set; } = "";
        public string[]? MacAddresses { get; set; }
        public ApplicationKeyChannelInfo[]? Channels { get; set; }
        public byte[]? ScreenshotJpeg { get; set; }
        public byte[]? ScreenshotThumb { get; set; }
        public ApplicationKeyJournalLine[]? Journal { get; set; }
        public string? LastError { get; set; }
        public string? LastErrorLevel { get; set; }
        public ApplicationKeySessionInfo Session { get; set; } = new();
    }

    private sealed class HeartbeatRequest
    {
        [JsonPropertyName("keyHash")]
        public string KeyHash { get; set; } = "";

        [JsonPropertyName("machineIdHash")]
        public string MachineIdHash { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";

        [JsonPropertyName("appVersion")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("macAddresses")]
        public string[]? MacAddresses { get; set; }

        [JsonPropertyName("channels")]
        public ApplicationKeyChannelInfo[]? Channels { get; set; }

        [JsonPropertyName("screenshotJpeg")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[]? ScreenshotJpeg { get; set; }

        [JsonPropertyName("screenshotThumb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[]? ScreenshotThumb { get; set; }

        [JsonPropertyName("journal")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ApplicationKeyJournalLine[]? Journal { get; set; }

        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        [JsonPropertyName("lastErrorLevel")]
        public string? LastErrorLevel { get; set; }

        [JsonPropertyName("updateAck")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool UpdateAck { get; set; }

        [JsonPropertyName("messageAck")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool MessageAck { get; set; }

        [JsonPropertyName("rec")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Rec { get; set; }

        [JsonPropertyName("recSeconds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int RecSeconds { get; set; }

        [JsonPropertyName("lastFile")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastFile { get; set; }

        [JsonPropertyName("alarmCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int AlarmCount { get; set; }

        [JsonPropertyName("conn")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Conn { get; set; }

        [JsonPropertyName("stopAck")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool StopAck { get; set; }

        [JsonPropertyName("zeroAck")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool ZeroAck { get; set; }
    }

    private sealed class PendingRequest
    {
        [JsonPropertyName("machineId")]
        public string MachineId { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";

        [JsonPropertyName("appVersion")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("macAddresses")]
        public string[]? MacAddresses { get; set; }

        [JsonPropertyName("channels")]
        public ApplicationKeyChannelInfo[]? Channels { get; set; }

        [JsonPropertyName("screenshotJpeg")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[]? ScreenshotJpeg { get; set; }

        [JsonPropertyName("screenshotThumb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public byte[]? ScreenshotThumb { get; set; }

        [JsonPropertyName("journal")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ApplicationKeyJournalLine[]? Journal { get; set; }

        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        [JsonPropertyName("lastErrorLevel")]
        public string? LastErrorLevel { get; set; }
    }

    private sealed class StatusDto
    {
        public string? Status { get; set; }
        public string? Key { get; set; }
        public string? KeyLast4 { get; set; }

        [JsonPropertyName("validUntil")]
        public DateTime? ValidUntil { get; set; }

        public DateTime? ValidUntilUtc { get; set; }
    }

    private sealed class HeartbeatResponseDto
    {
        public bool Ok { get; set; }
        public string? Status { get; set; }

        [JsonPropertyName("validUntil")]
        public DateTime? ValidUntil { get; set; }

        public DateTime? ValidUntilUtc { get; set; }

        public bool UpdateRequested { get; set; }

        public string? TargetVersion { get; set; }

        public bool JournalRequested { get; set; }

        public string? AdminMessage { get; set; }

        public string? PendingMessage { get; set; }

        public bool StopRecRequested { get; set; }

        public bool ZeroRequested { get; set; }
    }
}
