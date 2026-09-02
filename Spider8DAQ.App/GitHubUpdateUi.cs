using System.IO;
using System.Windows;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Updates;

namespace Spider8DAQ.App;

/// <summary>Startup + Expert check: public GitHub Releases, one-button Actualizează.</summary>
public static class GitHubUpdateUi
{
    public static async Task CheckAsync(Window? owner, bool interactive)
    {
        var ownerName = GitHubPublicRepo.Owner;
        var repo = GitHubPublicRepo.Repo;
        var token = LabUiPrefs.GitHubToken;
        if (!GitHubPrivateUpdater.IsConfigured(ownerName, repo))
            return;

        try
        {
            using var updater = new GitHubPrivateUpdater();
            var local = GitHubPrivateUpdater.GetLocalFileVersion();
            var result = await updater.CheckLatestAsync(ownerName, repo, token, local);
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

            var verText = result.Release.Version?.ToString(3)
                          ?? result.Release.TagName.TrimStart('v', 'V');
            var dlg = new UpdateAvailableWindow(verText);
            if (owner is not null)
                dlg.Owner = owner;
            if (dlg.ShowDialog() != true || !dlg.ApplyClicked)
                return;

            await ApplyAsync(owner, updater, result.Release, token);
        }
        catch (Exception ex)
        {
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

    private static async Task ApplyAsync(
        Window? owner,
        GitHubPrivateUpdater updater,
        GitHubReleaseInfo release,
        string? token)
    {
        var asset = release.UpdateAsset;
        if (asset is null
            || (string.IsNullOrWhiteSpace(asset.ApiUrl)
                && string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)))
        {
            MessageBox.Show(
                owner,
                "Release-ul nu conține asset-ul UPETAcqLab-update.zip.\n" +
                "Publicați zip-ul folderului publish pe tag-ul v*.",
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var installRoot = AppPaths.InstallRoot;
        if (GitHubPrivateUpdater.IsProtectedInstallPath(installRoot))
        {
            MessageBox.Show(
                owner,
                "Instalarea din Program Files nu se actualizează automat.\n\n" +
                "Folosiți copia portabilă (%LocalAppData%\\UPETAcqLab\\app sau publish-v2) " +
                "sau reinstalați manual. Aplicația nu scrie în Program Files.",
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            AppPaths.EnsureWritable(AppPaths.Updates);
            var zipPath = Path.Combine(AppPaths.Updates, GitHubPrivateUpdater.PreferredAssetName);
            await updater.DownloadUpdateZipAsync(asset, token, zipPath);

            var exe = Environment.ProcessPath
                      ?? Path.Combine(installRoot, "UPETAcqLab.exe");
            GitHubPrivateUpdater.PrepareApplyAndRestart(
                zipPath,
                installRoot,
                exe,
                Environment.ProcessId);
            Application.Current?.Shutdown(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                "Actualizarea a eșuat:\n\n" + AppPaths.FriendlyIoMessage(ex),
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
