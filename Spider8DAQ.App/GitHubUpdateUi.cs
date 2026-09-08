using System.IO;
using System.Windows;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Licensing;
using Spider8DAQ.Core.Updates;

namespace Spider8DAQ.App;

/// <summary>
/// GitHub Releases auto-update: startup, Expert «Verifică actualizări», and Admin push
/// all download + replace + restart without a second confirm dialog.
/// </summary>
public static class GitHubUpdateUi
{
    private static int _adminApplyBusy;
    private static DateTime _nextGithubAllowedUtc = DateTime.MinValue;

    public static async Task CheckAsync(Window? owner, bool interactive)
    {
        if (Volatile.Read(ref _adminApplyBusy) != 0)
            return;

        var ownerName = GitHubPublicRepo.Owner;
        var repo = GitHubPublicRepo.Repo;
        var token = LabUiPrefs.GitHubToken;
        if (!GitHubPrivateUpdater.IsConfigured(ownerName, repo))
            return;

        UpdatingWindow? checking = null;
        try
        {
            using var updater = new GitHubPrivateUpdater();
            var local = GitHubPrivateUpdater.GetLocalFileVersion();
            if (interactive)
            {
                checking = UpdatingWindow.TryShow(owner, local.ToString(3), "…");
                checking?.SetStage("Se verifică GitHub…", "Versiunea instalată " + local.ToString(3));
            }
            var result = await updater.CheckLatestAsync(ownerName, repo, token, local);
            try { checking?.Close(); } catch { /* ignore */ }
            checking = null;
            if (!result.Ok)
            {
                if (interactive)
                {
                    MessageBox.Show(owner, result.Message, "UPET AcqLab",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (!result.IsNewer || result.Release is null)
            {
                if (interactive)
                {
                    MessageBox.Show(owner, result.Message, "UPET AcqLab",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (Volatile.Read(ref _adminApplyBusy) != 0)
                return;

            await ApplyAsync(owner, updater, result.Release, token, shutdown: true, showUi: true);
        }
        catch (Exception ex)
        {
            try { checking?.Close(); } catch { /* ignore */ }
            if (interactive)
            {
                MessageBox.Show(
                    owner,
                    AppPaths.FriendlyIoMessage(ex),
                    "UPET AcqLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    /// Admin push: same GitHub zip + restart as Actualizează, no professor confirm.
    /// Empty LicenseServerUrl never reaches here (heartbeat is skipped).
    /// </summary>
    public static async Task ApplyAdminRequestedAsync(string? targetVersion)
    {
        if (Interlocked.Exchange(ref _adminApplyBusy, 1) != 0)
            return;

        var keepBusy = false;
        Window? owner = null;
        try { owner = Application.Current?.MainWindow; }
        catch { /* ignore */ }

        try
        {
            var cfg = ApplicationKeyConfig.Load();
            if (!cfg.IsConfigured)
                return;

            var ownerName = GitHubPublicRepo.Owner;
            var repo = GitHubPublicRepo.Repo;
            var token = LabUiPrefs.GitHubToken;
            if (!GitHubPrivateUpdater.IsConfigured(ownerName, repo))
            {
                await AckQuietAsync(cfg.CandidateUrls).ConfigureAwait(true);
                return;
            }

            if (DateTime.UtcNow < _nextGithubAllowedUtc)
                return;

            using var updater = new GitHubPrivateUpdater();
            var local = GitHubPrivateUpdater.GetLocalFileVersion();
            var result = await updater.CheckLatestAsync(ownerName, repo, token, local).ConfigureAwait(true);
            if (!result.Ok || result.Release is null)
            {
                _nextGithubAllowedUtc = DateTime.UtcNow.AddMinutes(2);
                return;
            }

            if (AlreadyAtTarget(local, result.Release.Version, targetVersion))
            {
                await AckQuietAsync(cfg.CandidateUrls).ConfigureAwait(true);
                return;
            }

            if (!result.IsNewer)
            {
                await AckQuietAsync(cfg.CandidateUrls).ConfigureAwait(true);
                return;
            }

            var prepared = await ApplyAsync(owner, updater, result.Release, token, shutdown: false, showUi: true)
                .ConfigureAwait(true);
            if (!prepared)
            {
                if (GitHubPrivateUpdater.IsProtectedInstallPath(AppPaths.InstallRoot))
                    await AckQuietAsync(cfg.CandidateUrls).ConfigureAwait(true);
                return;
            }

            await AckQuietAsync(cfg.CandidateUrls).ConfigureAwait(true);
            keepBusy = true;
            Application.Current?.Shutdown(0);
        }
        catch
        {
            /* next heartbeat retries */
        }
        finally
        {
            if (!keepBusy)
                Interlocked.Exchange(ref _adminApplyBusy, 0);
        }
    }

    private static bool AlreadyAtTarget(Version local, Version? remote, string? targetVersion)
    {
        if (!string.IsNullOrWhiteSpace(targetVersion)
            && GitHubPrivateUpdater.TryParseVersion(targetVersion, out var want)
            && want is not null
            && !GitHubPrivateUpdater.IsNewer(want, local))
            return true;

        if (remote is not null && !GitHubPrivateUpdater.IsNewer(remote, local))
            return true;

        return false;
    }

    private static async Task AckQuietAsync(IReadOnlyList<string> baseUrls)
    {
        foreach (var raw in baseUrls)
        {
            var url = (raw ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) continue;
            try
            {
                await ApplicationKeyClient.AckRemoteUpdateAsync(url, TimeSpan.FromSeconds(8))
                    .ConfigureAwait(false);
                return;
            }
            catch
            {
                /* try next LAN / tunnel URL */
            }
        }
    }

    private static async Task<bool> ApplyAsync(
        Window? owner,
        GitHubPrivateUpdater updater,
        GitHubReleaseInfo release,
        string? token,
        bool shutdown,
        bool showUi)
    {
        var asset = release.UpdateAsset;
        if (asset is null
            || (string.IsNullOrWhiteSpace(asset.ApiUrl)
                && string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)))
        {
            if (showUi)
            {
                MessageBox.Show(
                    owner,
                    "Release-ul nu conține asset-ul UPETAcqLab-update.zip.\n" +
                    "Publicați zip-ul folderului publish pe tag-ul v*.",
                    "UPET AcqLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return false;
        }

        var installRoot = AppPaths.InstallRoot;
        if (GitHubPrivateUpdater.IsProtectedInstallPath(installRoot))
        {
            if (showUi)
            {
                MessageBox.Show(
                    owner,
                    "Instalarea din Program Files nu se actualizează automat.\n\n" +
                    "Folosiți copia portabilă (%LocalAppData%\\UPETAcqLab\\app sau publish-v2) " +
                    "sau reinstalați manual. Aplicația nu scrie în Program Files.",
                    "UPET AcqLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return false;
        }

        UpdatingWindow? progress = null;
        var applied = false;
        try
        {
            var toVer = release.Version?.ToString(3)
                        ?? release.TagName.TrimStart('v', 'V');
            var fromVer = GitHubPrivateUpdater.GetLocalFileVersion().ToString(3);
            if (showUi)
            {
                progress = UpdatingWindow.TryShow(owner, fromVer, toVer);
                progress?.SetStage(
                    "Se descarcă " + GitHubPrivateUpdater.PreferredAssetName + "…",
                    "Versiunea " + fromVer + " → " + toVer);
            }

            AppPaths.EnsureWritable(AppPaths.Updates);
            var zipPath = Path.Combine(AppPaths.Updates, GitHubPrivateUpdater.PreferredAssetName);
            IProgress<GitHubDownloadProgress>? dl = progress is null
                ? null
                : new Progress<GitHubDownloadProgress>(progress.SetDownload);
            await updater.DownloadUpdateZipAsync(asset, token, zipPath, dl).ConfigureAwait(true);

            progress?.SetStage(
                "Se pregătește instalarea…",
                "Fișierele se copiază după închiderea aplicației.",
                complete: true);

            var exe = Environment.ProcessPath
                      ?? Path.Combine(installRoot, "UPETAcqLab.exe");
            GitHubPrivateUpdater.PrepareApplyAndRestart(
                zipPath,
                installRoot,
                exe,
                Environment.ProcessId);

            progress?.SetStage(
                "Se repornește UPET AcqLab…",
                "Așteptați câteva secunde. Nu închideți fereastra.",
                complete: true);
            applied = true;
            if (shutdown)
                Application.Current?.Shutdown(0);
            return true;
        }
        catch (Exception ex)
        {
            if (showUi)
            {
                MessageBox.Show(
                    owner,
                    "Actualizarea a eșuat:\n\n" + AppPaths.FriendlyIoMessage(ex),
                    "UPET AcqLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            return false;
        }
        finally
        {
            if (!applied)
            {
                try { progress?.Close(); } catch { /* ignore */ }
            }
        }
    }
}
