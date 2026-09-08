namespace Spider8DAQ.Core.Updates;

/// <summary>
/// Public GitHub Releases target for lab auto-update. Fill once; lab PCs need no token.
/// Empty or placeholders <c>OWNER</c>/<c>REPO</c> skip the check silently.
/// </summary>
public static class GitHubPublicRepo
{
    public const string Owner = "roooot-prog";
    public const string Repo = "UPET-AcqLab";

    /// <summary>Live LAN + Cloudflare URLs for lab heartbeats (updated on each Admin start / release).</summary>
    public static string LicenseServerCatalogUrl =>
        "https://raw.githubusercontent.com/" + Owner + "/" + Repo + "/main/tools/license-server.release.json";
}
