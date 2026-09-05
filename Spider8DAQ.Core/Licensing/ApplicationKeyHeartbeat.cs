using System.Diagnostics;

namespace Spider8DAQ.Core.Licensing;

/// <summary>License heartbeat ~2s (commands) + live twin ~1 ms (latest DAQ sample, 1 in-flight HTTP).</summary>
public static class ApplicationKeyHeartbeat
{
    public static readonly TimeSpan LicensedInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LiveInterval = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LiveTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan UrgentMinInterval = TimeSpan.FromSeconds(45);
    private static CancellationTokenSource? _cts;
    private static readonly object Gate = new();
    private static Action<ApplicationKeyGateReason>? _onBlocked;
    private static Action<string?>? _onRemoteUpdate;
    private static Action<string>? _onAdminMessage;
    private static Action? _onStopRec;
    private static Action? _onZero;
    private static DateTime _lastUrgentUtc = DateTime.MinValue;
    private static bool _timerHeld;

    /// <summary>Admin requested GitHub update (target tag, or null = latest).</summary>
    public static event Action<string?>? RemoteUpdateRequested
    {
        add { lock (Gate) _onRemoteUpdate += value; }
        remove { lock (Gate) _onRemoteUpdate -= value; }
    }

    /// <summary>Short Romanian string from Admin (MessageBox / banner on the lab PC).</summary>
    public static event Action<string>? AdminMessageReceived
    {
        add { lock (Gate) _onAdminMessage += value; }
        remove { lock (Gate) _onAdminMessage -= value; }
    }

    /// <summary>Admin STOP SALĂ — stop Rec without the local confirm dialog.</summary>
    public static event Action? StopRecRequested
    {
        add { lock (Gate) _onStopRec += value; }
        remove { lock (Gate) _onStopRec -= value; }
    }

    /// <summary>Admin remote Zero (tare all channels).</summary>
    public static event Action? ZeroRequested
    {
        add { lock (Gate) _onZero += value; }
        remove { lock (Gate) _onZero -= value; }
    }

    public static void Start(Action<ApplicationKeyGateReason>? onBlocked = null)
    {
        var cfg = ApplicationKeyConfig.Load();
        // Source tree / author PC: empty LicenseServerUrl skips gate AND heartbeat.
        // The AcqLab that should receive Mesaj (publish-v2) must have LicenseServerUrl set.
        if (!cfg.IsConfigured) return;
        if (!ApplicationKeyStore.HasUnexpiredCache()) return;

        lock (Gate)
        {
            Stop();
            NativeTimerResolution.Request1Ms();
            _timerHeld = true;
            _onBlocked = onBlocked;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var urls = cfg.CandidateUrls;
            _ = Task.Run(() => LoopAsync(urls, ct), ct);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
            if (_timerHeld)
            {
                NativeTimerResolution.Release();
                _timerHeld = false;
            }
        }
    }

    /// <summary>
    /// After a new Error-level journal line: one extra pending/heartbeat (throttled).
    /// Empty LicenseServerUrl still skips (source tree / author PC).
    /// </summary>
    public static void NotifyUrgentError()
    {
        var cfg = ApplicationKeyConfig.Load();
        if (!cfg.IsConfigured) return;

        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (now - _lastUrgentUtc < UrgentMinInterval) return;
            _lastUrgentUtc = now;
        }

        var urls = cfg.CandidateUrls;
        _ = Task.Run(async () =>
        {
            try
            {
                if (ApplicationKeyStore.HasUnexpiredCache())
                    await HeartbeatFirstAsync(urls, CancellationToken.None).ConfigureAwait(false);
                else
                    await ApplicationKeyClient.RegisterPendingFirstAsync(urls, Timeout).ConfigureAwait(false);
            }
            catch
            {
                /* never throw into UI */
            }
        });
    }

    private static async Task LoopAsync(IReadOnlyList<string> baseUrls, CancellationToken ct)
    {
        await SendQuietAsync(baseUrls, ct).ConfigureAwait(false);
        _ = LiveLoopAsync(baseUrls, ct);
        using var timer = new PeriodicTimer(LicensedInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await SendQuietAsync(baseUrls, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private static async Task LiveLoopAsync(IReadOnlyList<string> baseUrls, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                await SendLiveQuietAsync(baseUrls, ct).ConfigureAwait(false);
                var waitMs = LiveInterval.TotalMilliseconds
                    - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (waitMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private static async Task SendLiveQuietAsync(IReadOnlyList<string> baseUrls, CancellationToken ct)
    {
        try
        {
            foreach (var raw in baseUrls)
            {
                var url = (raw ?? "").Trim().TrimEnd('/');
                if (url.Length == 0) continue;
                if (await ApplicationKeyClient.LiveCachedAsync(url, LiveTimeout, ct).ConfigureAwait(false))
                    return;
            }
        }
        catch
        {
            /* never throw into UI */
        }
    }

    private static async Task HeartbeatFirstAsync(IReadOnlyList<string> baseUrls, CancellationToken ct)
    {
        foreach (var raw in baseUrls)
        {
            var url = (raw ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) continue;
            var r = await ApplicationKeyClient.HeartbeatCachedAsync(url, Timeout, ct).ConfigureAwait(false);
            if (r != ApplicationKeyHeartbeatResult.Unreachable)
                return;
        }
    }

    private static async Task SendQuietAsync(IReadOnlyList<string> baseUrls, CancellationToken ct)
    {
        try
        {
            ApplicationKeyHeartbeatDetail detail = ApplicationKeyHeartbeatDetail.From(
                ApplicationKeyHeartbeatResult.Unreachable);
            string? baseUrl = null;
            foreach (var raw in baseUrls)
            {
                var url = (raw ?? "").Trim().TrimEnd('/');
                if (url.Length == 0) continue;
                var next = await ApplicationKeyClient.HeartbeatCachedDetailAsync(url, Timeout, ct)
                    .ConfigureAwait(false);
                detail = next;
                baseUrl = url;
                if (next.Result != ApplicationKeyHeartbeatResult.Unreachable)
                    break;
            }
            if (baseUrl is null)
                return;
            if (detail.Result == ApplicationKeyHeartbeatResult.Ok)
            {
                var rec = ApplicationKeyStore.Load();
                if (rec is not null)
                    ApplicationKeyStore.SaveAccepted(
                        rec.KeyHash,
                        rec.KeyLast4,
                        rec.MachineIdHash,
                        rec.Hostname,
                        rawKey: null,
                        validUntilUtc: rec.ValidUntilUtc);
                if (detail.UpdateRequested)
                    RaiseRemoteUpdate(detail.TargetVersion);
                if (detail.StopRecRequested)
                {
                    RaiseStopRec();
                    await ApplicationKeyClient.HeartbeatCachedDetailAsync(
                            baseUrl, Timeout, ct, stopAck: true)
                        .ConfigureAwait(false);
                }
                if (detail.ZeroRequested)
                {
                    RaiseZero();
                    await ApplicationKeyClient.HeartbeatCachedDetailAsync(
                            baseUrl, Timeout, ct, zeroAck: true)
                        .ConfigureAwait(false);
                }
                if (!string.IsNullOrWhiteSpace(detail.AdminMessage))
                {
                    // Ack on the server first so the same text is not pushed every live tick
                    // if the overlay is still open (or OK used to nest the dispatcher).
                    RaiseAdminMessage(detail.AdminMessage);
                    await ApplicationKeyClient.HeartbeatCachedDetailAsync(
                            baseUrl, Timeout, ct, updateAck: false, forceScreenshot: false, messageAck: true)
                        .ConfigureAwait(false);
                }
                if (detail.JournalRequested)
                {
                    var extra = await ApplicationKeyClient.HeartbeatCachedDetailAsync(
                            baseUrl, Timeout, ct, updateAck: false, forceScreenshot: true)
                        .ConfigureAwait(false);
                    if (extra.Result == ApplicationKeyHeartbeatResult.Revoked)
                        RaiseBlocked(ApplicationKeyGateReason.Revoked);
                    else if (extra.Result == ApplicationKeyHeartbeatResult.Expired)
                        RaiseBlocked(ApplicationKeyGateReason.Expired);
                }
            }
            else if (detail.Result == ApplicationKeyHeartbeatResult.Revoked)
            {
                RaiseBlocked(ApplicationKeyGateReason.Revoked);
            }
            else if (detail.Result == ApplicationKeyHeartbeatResult.Expired)
            {
                RaiseBlocked(ApplicationKeyGateReason.Expired);
            }
            // Invalid / unreachable after cache: do not tear down a live measurement session.
        }
        catch
        {
            // never throw into UI
        }
    }

    private static void RaiseRemoteUpdate(string? targetVersion)
    {
        Action<string?>? cb;
        lock (Gate)
            cb = _onRemoteUpdate;
        if (cb is null) return;
        try { cb(targetVersion); }
        catch { /* never throw into heartbeat */ }
    }

    private static void RaiseStopRec()
    {
        Action? cb;
        lock (Gate)
            cb = _onStopRec;
        if (cb is null) return;
        try { cb(); }
        catch { /* never throw into heartbeat */ }
    }

    private static void RaiseZero()
    {
        Action? cb;
        lock (Gate)
            cb = _onZero;
        if (cb is null) return;
        try { cb(); }
        catch { /* never throw into heartbeat */ }
    }

    private static bool RaiseAdminMessage(string text)
    {
        Action<string>? cb;
        lock (Gate)
            cb = _onAdminMessage;
        if (cb is null) return false;
        try
        {
            cb(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RaiseBlocked(ApplicationKeyGateReason reason)
    {
        ApplicationKeyStore.Clear();
        Action<ApplicationKeyGateReason>? cb;
        lock (Gate)
            cb = _onBlocked;
        if (cb is null) return;
        try { cb(reason); }
        catch { /* never throw into heartbeat */ }
    }
}
