namespace Spider8DAQ.Core.Updates;

/// <summary>
/// Public GitHub Releases target for lab auto-update. Fill once; lab PCs need no token.
/// Empty or placeholders <c>OWNER</c>/<c>REPO</c> skip the check silently.
/// </summary>
public static class GitHubPublicRepo
{
    public const string Owner = "roooot-prog";
    public const string Repo = "UPET-AcqLab";
}
