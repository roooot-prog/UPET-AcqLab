using System.Net;
using Spider8DAQ.Core.Updates;
using Xunit;

namespace Spider8DAQ.Core.Tests.Updates;

public class GitHubPrivateUpdaterTests
{
    [Theory]
    [InlineData("v3.3.89", "3.3.89")]
    [InlineData("3.3.89", "3.3.89")]
    [InlineData("UPET AcqLab 3.3.88", "3.3.88")]
    [InlineData("v3.3.89-lab", "3.3.89")]
    public void TryParseVersion_reads_tag_or_name(string text, string expected)
    {
        Assert.True(GitHubPrivateUpdater.TryParseVersion(text, out var v));
        Assert.Equal(expected, v!.ToString(3));
    }

    [Fact]
    public void TryParseVersion_rejects_empty()
    {
        Assert.False(GitHubPrivateUpdater.TryParseVersion(" ", out _));
        Assert.False(GitHubPrivateUpdater.TryParseVersion(null, out _));
    }

    [Fact]
    public void IsNewer_compares_three_part_versions()
    {
        Assert.True(GitHubPrivateUpdater.IsNewer(new Version(3, 3, 89), new Version(3, 3, 88)));
        Assert.False(GitHubPrivateUpdater.IsNewer(new Version(3, 3, 88), new Version(3, 3, 88)));
        Assert.False(GitHubPrivateUpdater.IsNewer(new Version(3, 3, 87), new Version(3, 3, 88)));
    }

    [Fact]
    public void IsConfigured_requires_owner_and_repo_not_token()
    {
        Assert.False(GitHubPrivateUpdater.IsConfigured(null, "repo"));
        Assert.False(GitHubPrivateUpdater.IsConfigured("own", ""));
        Assert.True(GitHubPrivateUpdater.IsConfigured("own", "repo"));
        Assert.True(GitHubPrivateUpdater.IsConfigured("own", "repo", null));
        Assert.True(GitHubPrivateUpdater.IsConfigured("own", "repo", ""));
        Assert.False(GitHubPrivateUpdater.IsConfigured("OWNER", "REPO"));
    }

    [Fact]
    public void IsConfigured_skips_placeholder_owner_repo()
    {
        Assert.True(GitHubPrivateUpdater.IsPlaceholder("OWNER", "REPO"));
        Assert.False(GitHubPrivateUpdater.IsPlaceholder("upet", "AcqLab"));
        if (GitHubPrivateUpdater.IsPlaceholder(GitHubPublicRepo.Owner, GitHubPublicRepo.Repo))
            Assert.False(GitHubPrivateUpdater.IsConfigured(GitHubPublicRepo.Owner, GitHubPublicRepo.Repo));
        else
            Assert.True(GitHubPrivateUpdater.IsConfigured(GitHubPublicRepo.Owner, GitHubPublicRepo.Repo));
    }

    [Fact]
    public void PickUpdateAsset_prefers_named_zip()
    {
        var assets = new[]
        {
            new GitHubReleaseAsset { Name = "notes.txt", ApiUrl = "https://api.github.com/a/1" },
            new GitHubReleaseAsset { Name = "UPETAcqLab-update.zip", ApiUrl = "https://api.github.com/a/2" },
            new GitHubReleaseAsset { Name = "other.zip", ApiUrl = "https://api.github.com/a/3" }
        };
        var pick = GitHubPrivateUpdater.PickUpdateAsset(assets);
        Assert.Equal("UPETAcqLab-update.zip", pick!.Name);
    }

    [Fact]
    public void PickUpdateAsset_falls_back_to_any_zip()
    {
        var assets = new[]
        {
            new GitHubReleaseAsset { Name = "readme.md", ApiUrl = "u1" },
            new GitHubReleaseAsset { Name = "setup.zip", ApiUrl = "u2" }
        };
        var pick = GitHubPrivateUpdater.PickUpdateAsset(assets);
        Assert.Equal("setup.zip", pick!.Name);
    }

    [Fact]
    public void LatestReleaseUrl_is_public_api_endpoint()
    {
        var url = GitHubPrivateUpdater.LatestReleaseUrl("upet-lab", "AcqLab");
        Assert.Equal("https://api.github.com/repos/upet-lab/AcqLab/releases/latest", url);
    }

    [Fact]
    public void IsProtectedInstallPath_detects_program_files()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(pf86))
            return;
        Assert.True(GitHubPrivateUpdater.IsProtectedInstallPath(Path.Combine(pf86, "UPET AcqLab")));
        Assert.False(GitHubPrivateUpdater.IsProtectedInstallPath(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UPETAcqLab", "app")));
    }

    [Fact]
    public void ResolvePublishRoot_uses_nested_folder_when_exe_is_inside()
    {
        var root = Path.Combine(Path.GetTempPath(), "upet_upd_" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "publish-v2");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "UPETAcqLab.exe"), "stub");
        try
        {
            Assert.Equal(nested, GitHubPrivateUpdater.ResolvePublishRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildApplyScript_waits_then_robocopies()
    {
        var script = GitHubPrivateUpdater.BuildApplyScript();
        Assert.Contains("robocopy", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WAITPID", script, StringComparison.Ordinal);
        Assert.Contains("start \"\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckLatest_skips_silently_when_not_configured()
    {
        using var updater = new GitHubPrivateUpdater();
        var result = await updater.CheckLatestAsync("", "", null, new Version(3, 3, 88));
        Assert.False(result.Configured);
        Assert.True(result.Ok);
        Assert.False(result.IsNewer);
        Assert.Equal("", result.Message);
    }

    [Fact]
    public async Task CheckLatest_skips_placeholder_owner_repo()
    {
        using var updater = new GitHubPrivateUpdater();
        var result = await updater.CheckLatestAsync("OWNER", "REPO", null, new Version(3, 3, 88));
        Assert.False(result.Configured);
        Assert.True(result.Ok);
        Assert.False(result.IsNewer);
    }

    [Fact]
    public async Task CheckLatest_public_sends_user_agent_without_bearer()
    {
        HttpRequestMessage? seen = null;
        var json =
            """
            {
              "tag_name": "v3.3.90",
              "name": "3.3.90",
              "body": "",
              "assets": [
                {
                  "name": "UPETAcqLab-update.zip",
                  "url": "https://api.github.com/repos/o/r/releases/assets/1",
                  "browser_download_url": "https://github.com/o/r/releases/download/v3.3.90/UPETAcqLab-update.zip",
                  "size": 12
                }
              ]
            }
            """;
        var handler = new StubHandler((req, _) =>
        {
            seen = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        });
        using var http = new HttpClient(handler);
        using var updater = new GitHubPrivateUpdater(http);
        var result = await updater.CheckLatestAsync("upet", "AcqLab", null, new Version(3, 3, 88));

        Assert.True(result.Ok);
        Assert.True(result.IsNewer);
        Assert.Equal("v3.3.90", result.Release!.TagName);
        Assert.Equal("UPETAcqLab-update.zip", result.Release.UpdateAsset!.Name);
        Assert.Equal(
            "https://github.com/o/r/releases/download/v3.3.90/UPETAcqLab-update.zip",
            result.Release.UpdateAsset.BrowserDownloadUrl);
        Assert.NotNull(seen);
        Assert.Equal("https://api.github.com/repos/upet/AcqLab/releases/latest", seen!.RequestUri!.ToString());
        Assert.Null(seen.Headers.Authorization);
        Assert.Contains(seen.Headers.UserAgent, u => u.ToString().Contains("UPETAcqLab", StringComparison.Ordinal));
        Assert.Contains(seen.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task CheckLatest_sends_bearer_when_token_present()
    {
        HttpRequestMessage? seen = null;
        var json =
            """
            {
              "tag_name": "v3.3.89",
              "name": "3.3.89",
              "body": "Citire dash",
              "assets": [
                {
                  "name": "UPETAcqLab-update.zip",
                  "url": "https://api.github.com/repos/o/r/releases/assets/1",
                  "browser_download_url": "https://github.com/o/r/releases/download/v3.3.89/UPETAcqLab-update.zip",
                  "size": 12
                }
              ]
            }
            """;
        var handler = new StubHandler((req, _) =>
        {
            seen = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        });
        using var http = new HttpClient(handler);
        using var updater = new GitHubPrivateUpdater(http);
        var result = await updater.CheckLatestAsync("upet", "AcqLab", "ghp_test_not_real", new Version(3, 3, 88));

        Assert.True(result.Ok);
        Assert.True(result.IsNewer);
        Assert.Equal("v3.3.89", result.Release!.TagName);
        Assert.Equal("Bearer", seen!.Headers.Authorization?.Scheme);
        Assert.Equal("ghp_test_not_real", seen.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task DownloadUpdateZip_public_uses_browser_url_without_bearer()
    {
        var hops = new List<string>();
        var handler = new StubHandler((req, _) =>
        {
            hops.Add(req.RequestUri!.ToString());
            Assert.Null(req.Headers.Authorization);
            if (req.RequestUri!.Host == "github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://objects.githubusercontent.com/signed/zip");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x50, 0x4B, 0x03, 0x04 })
            });
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var updater = new GitHubPrivateUpdater(http);
        var dest = Path.Combine(Path.GetTempPath(), "upet_dl_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var asset = new GitHubReleaseAsset
            {
                Name = "UPETAcqLab-update.zip",
                ApiUrl = "https://api.github.com/repos/o/r/releases/assets/99",
                BrowserDownloadUrl = "https://github.com/o/r/releases/download/v3.3.90/UPETAcqLab-update.zip"
            };
            await updater.DownloadUpdateZipAsync(asset, token: null, dest);
            Assert.Equal(2, hops.Count);
            Assert.StartsWith("https://github.com/", hops[0], StringComparison.Ordinal);
            Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, File.ReadAllBytes(dest));
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public async Task DownloadUpdateZip_uses_api_asset_url_with_bearer()
    {
        var hops = new List<string>();
        var handler = new StubHandler((req, _) =>
        {
            hops.Add(req.RequestUri!.ToString());
            if (req.RequestUri!.Host == "api.github.com")
            {
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                Assert.Contains(req.Headers.Accept, a => a.MediaType == "application/octet-stream");
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://objects.githubusercontent.com/signed/zip");
                return Task.FromResult(redirect);
            }

            Assert.Null(req.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x50, 0x4B, 0x03, 0x04 })
            });
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var updater = new GitHubPrivateUpdater(http);
        var dest = Path.Combine(Path.GetTempPath(), "upet_dl_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var asset = new GitHubReleaseAsset
            {
                Name = "UPETAcqLab-update.zip",
                ApiUrl = "https://api.github.com/repos/o/r/releases/assets/99"
            };
            await updater.DownloadUpdateZipAsync(asset, "ghp_test_not_real", dest);
            Assert.Equal(2, hops.Count);
            Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, File.ReadAllBytes(dest));
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public async Task CheckLatest_401_is_auth_error()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"message\":\"Bad credentials\"}")
            }));
        using var http = new HttpClient(handler);
        using var updater = new GitHubPrivateUpdater(http);
        var result = await updater.CheckLatestAsync("o", "r", "bad", new Version(3, 3, 88));
        Assert.False(result.Ok);
        Assert.Equal(401, result.StatusCode);
        Assert.Contains("autentificare", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
