using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Licensing;

public enum ApplicationKeyHeartbeatResult
{
    Ok,
    Invalid,
    Unreachable
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

        try
        {
            using var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "UPETAcqLab");
            var url = baseUrl.TrimEnd('/') + "/api/v1/heartbeat";
            var json = JsonSerializer.Serialize(new HeartbeatRequest
            {
                KeyHash = keyHash,
                MachineIdHash = machineIdHash,
                Hostname = hostname
            }, JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
                return ApplicationKeyHeartbeatResult.Ok;
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return ApplicationKeyHeartbeatResult.Invalid;
            return ApplicationKeyHeartbeatResult.Unreachable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApplicationKeyHeartbeatResult.Unreachable;
        }
        catch
        {
            return ApplicationKeyHeartbeatResult.Unreachable;
        }
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
            ApplicationKeyStore.SaveAccepted(hash, ApplicationKeyCrypto.KeyLast4(key), machine, host);
        return result;
    }

    public static Task<ApplicationKeyHeartbeatResult> HeartbeatCachedAsync(
        string baseUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var rec = ApplicationKeyStore.Load();
        if (rec is null || string.IsNullOrWhiteSpace(rec.KeyHash))
            return Task.FromResult(ApplicationKeyHeartbeatResult.Invalid);

        var machine = string.IsNullOrWhiteSpace(rec.MachineIdHash)
            ? ApplicationKeyCrypto.HashedMachineId()
            : rec.MachineIdHash;
        var host = string.IsNullOrWhiteSpace(rec.Hostname) ? Environment.MachineName : rec.Hostname;
        return HeartbeatAsync(baseUrl, rec.KeyHash, machine, host, timeout, cancellationToken);
    }

    private sealed class HeartbeatRequest
    {
        [JsonPropertyName("keyHash")]
        public string KeyHash { get; set; } = "";

        [JsonPropertyName("machineIdHash")]
        public string MachineIdHash { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";
    }
}
