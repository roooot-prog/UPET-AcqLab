namespace Spider8DAQ.Core.Licensing;

/// <summary>Launch + ~1h heartbeat. Never blocks LIVE; first activation is gated elsewhere.</summary>
public static class ApplicationKeyHeartbeat
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private static CancellationTokenSource? _cts;
    private static readonly object Gate = new();

    public static void Start()
    {
        var cfg = ApplicationKeyConfig.Load();
        if (!cfg.IsConfigured) return;
        if (!ApplicationKeyStore.HasAcceptedCache()) return;

        lock (Gate)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var url = cfg.NormalizedBaseUrl;
            _ = Task.Run(() => LoopAsync(url, ct), ct);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _cts?.Dispose(); } catch { /* ignore */ }
            _cts = null;
        }
    }

    private static async Task LoopAsync(string baseUrl, CancellationToken ct)
    {
        await SendQuietAsync(baseUrl, ct).ConfigureAwait(false);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await SendQuietAsync(baseUrl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private static async Task SendQuietAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var result = await ApplicationKeyClient.HeartbeatCachedAsync(baseUrl, Timeout, ct)
                .ConfigureAwait(false);
            if (result == ApplicationKeyHeartbeatResult.Ok)
            {
                var rec = ApplicationKeyStore.Load();
                if (rec is not null)
                    ApplicationKeyStore.SaveAccepted(rec.KeyHash, rec.KeyLast4, rec.MachineIdHash, rec.Hostname);
            }
            // Invalid after cache: do not tear down a live measurement session.
        }
        catch
        {
            // never throw into UI
        }
    }
}
