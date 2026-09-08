namespace Spider8DAQ.Core.Updates;

public sealed class GitHubReleaseInfo
{
    public string TagName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public Version? Version { get; init; }
    public GitHubReleaseAsset? UpdateAsset { get; init; }
}

public sealed class GitHubReleaseAsset
{
    public string Name { get; init; } = "";
    public string ApiUrl { get; init; } = "";
    public string BrowserDownloadUrl { get; init; } = "";
    public long Size { get; init; }
}

public sealed class GitHubCheckResult
{
    public bool Configured { get; init; }
    public bool Ok { get; init; }
    public bool IsNewer { get; init; }
    public string Message { get; init; } = "";
    public GitHubReleaseInfo? Release { get; init; }
    public Version? LocalVersion { get; init; }
    public int StatusCode { get; init; }
}

public sealed class GitHubTestConnectionResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public int StatusCode { get; init; }
    public string? TagName { get; init; }
}

public readonly struct GitHubDownloadProgress
{
    public GitHubDownloadProgress(long bytesReceived, long? totalBytes)
    {
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
    }

    public long BytesReceived { get; }
    public long? TotalBytes { get; }
    public bool HasTotal => TotalBytes is > 0;
    public double Percent =>
        HasTotal ? Math.Clamp(100.0 * BytesReceived / TotalBytes!.Value, 0, 100) : 0;
}
