using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.Core.Updates;

/// <summary>
/// Checks a <b>public</b> GitHub repo for a newer release (unauthenticated
/// <c>releases/latest</c>, User-Agent UPETAcqLab). If a PAT is already in prefs
/// it is still sent (private fallback). Public assets use
/// <c>browser_download_url</c>; private assets still use the API URL + Bearer.
/// </summary>
public sealed class GitHubPrivateUpdater : IDisposable
{
    public const string UserAgent = "UPETAcqLab";
    public const string PreferredAssetName = "UPETAcqLab-update.zip";
    public const string ApplyScriptName = "apply-update.cmd";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GitHubPrivateUpdater(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _http = CreateDefaultClient();
            _ownsClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsClient = false;
        }
    }

    public static HttpClient CreateDefaultClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    public static bool IsConfigured(string? owner, string? repo) =>
        !string.IsNullOrWhiteSpace(owner)
        && !string.IsNullOrWhiteSpace(repo)
        && !IsPlaceholder(owner, repo);

    /// <summary>Legacy three-arg form: token is optional (public releases).</summary>
    public static bool IsConfigured(string? owner, string? repo, string? _) =>
        IsConfigured(owner, repo);

    public static bool IsPlaceholder(string? owner, string? repo)
    {
        var o = owner?.Trim() ?? "";
        var r = repo?.Trim() ?? "";
        return string.Equals(o, "OWNER", StringComparison.OrdinalIgnoreCase)
               && string.Equals(r, "REPO", StringComparison.OrdinalIgnoreCase);
    }

    public static Version GetLocalFileVersion(string? exePath = null)
    {
        try
        {
            var path = exePath;
            if (string.IsNullOrWhiteSpace(path))
                path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                if (TryParseVersion(fvi.FileVersion, out var fromFile) && fromFile is not null)
                    return fromFile;
            }
        }
        catch
        {
            /* fall through */
        }

        var asm = System.Reflection.Assembly.GetEntryAssembly()
                  ?? System.Reflection.Assembly.GetExecutingAssembly();
        return asm.GetName().Version ?? new Version(0, 0, 0);
    }

    public static bool TryParseVersion(string? text, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            t = t[1..];

        var m = Regex.Match(t, @"\d+\.\d+(?:\.\d+)?(?:\.\d+)?");
        if (!m.Success)
            return false;

        if (Version.TryParse(m.Value, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        return false;
    }

    public static bool IsNewer(Version remote, Version local) =>
        Normalize(remote) > Normalize(local);

    public static GitHubReleaseAsset? PickUpdateAsset(IEnumerable<GitHubReleaseAsset>? assets)
    {
        if (assets is null)
            return null;

        var list = assets.Where(a => !string.IsNullOrWhiteSpace(a.Name)).ToList();
        if (list.Count == 0)
            return null;

        var preferred = list.FirstOrDefault(a =>
            string.Equals(a.Name, PreferredAssetName, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
            return preferred;

        return list.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProtectedInstallPath(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return false;

        var full = Path.GetFullPath(installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var pf = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (full.StartsWith(pf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, pf, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async Task<GitHubCheckResult> CheckLatestAsync(
        string owner,
        string repo,
        string? token,
        Version localVersion,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(owner, repo))
        {
            return new GitHubCheckResult
            {
                Configured = false,
                Ok = true,
                Message = ""
            };
        }

        var url = LatestReleaseUrl(owner, repo);
        using var req = NewApiRequest(HttpMethod.Get, url, token);
        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new GitHubCheckResult
            {
                Configured = true,
                Ok = false,
                LocalVersion = localVersion,
                Message = "Nu s-a putut contacta GitHub: " + ex.Message
            };
        }

        var code = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            return new GitHubCheckResult
            {
                Configured = true,
                Ok = false,
                StatusCode = code,
                LocalVersion = localVersion,
                Message = DescribeHttpError(code)
            };
        }

        GitHubReleaseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GitHubReleaseDto>(body, JsonOpts);
        }
        catch (Exception ex)
        {
            return new GitHubCheckResult
            {
                Configured = true,
                Ok = false,
                StatusCode = code,
                LocalVersion = localVersion,
                Message = "Răspuns GitHub invalid: " + ex.Message
            };
        }

        if (dto is null)
        {
            return new GitHubCheckResult
            {
                Configured = true,
                Ok = false,
                StatusCode = code,
                LocalVersion = localVersion,
                Message = "Nu există un release latest pe acest repo."
            };
        }

        var assets = (dto.Assets ?? [])
            .Select(a => new GitHubReleaseAsset
            {
                Name = a.Name ?? "",
                ApiUrl = a.Url ?? "",
                BrowserDownloadUrl = a.BrowserDownloadUrl ?? "",
                Size = a.Size
            })
            .ToList();

        TryParseVersion(dto.TagName, out var fromTag);
        TryParseVersion(dto.Name, out var fromName);
        var remoteVer = fromTag ?? fromName;

        var info = new GitHubReleaseInfo
        {
            TagName = dto.TagName ?? "",
            Name = dto.Name ?? "",
            Body = dto.Body ?? "",
            Version = remoteVer,
            UpdateAsset = PickUpdateAsset(assets)
        };

        var newer = remoteVer is not null && IsNewer(remoteVer, localVersion);
        var display = remoteVer?.ToString(3) ?? info.TagName;
        return new GitHubCheckResult
        {
            Configured = true,
            Ok = true,
            IsNewer = newer,
            StatusCode = code,
            LocalVersion = localVersion,
            Release = info,
            Message = newer
                ? "Versiune nouă " + display
                : "Sunteți la zi (" + Normalize(localVersion).ToString(3) + ")."
        };
    }

    public async Task<GitHubTestConnectionResult> TestConnectionAsync(
        string owner,
        string repo,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(owner, repo))
        {
            return new GitHubTestConnectionResult
            {
                Ok = false,
                Message = "Completați owner și numele repo-ului public."
            };
        }

        var check = await CheckLatestAsync(owner, repo, token, GetLocalFileVersion(), cancellationToken)
            .ConfigureAwait(false);
        if (!check.Ok)
        {
            return new GitHubTestConnectionResult
            {
                Ok = false,
                StatusCode = check.StatusCode,
                Message = check.Message
            };
        }

        var tag = check.Release?.TagName ?? "(fără tag)";
        var asset = check.Release?.UpdateAsset?.Name ?? "(lipsește UPETAcqLab-update.zip)";
        return new GitHubTestConnectionResult
        {
            Ok = true,
            StatusCode = check.StatusCode,
            TagName = tag,
            Message = "Conexiune OK — latest " + tag + ", asset: " + asset + "."
        };
    }

    public async Task<string> DownloadUpdateZipAsync(
        GitHubReleaseAsset asset,
        string? token,
        string destinationZipPath,
        IProgress<GitHubDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var authenticated = !string.IsNullOrWhiteSpace(token);
        var url = authenticated && !string.IsNullOrWhiteSpace(asset.ApiUrl)
            ? asset.ApiUrl
            : asset.BrowserDownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = asset.ApiUrl;
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Asset-ul GitHub nu are URL de descărcare.");

        var dir = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(req, token);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        var resp = await SendFollowingRedirectsAsync(req, token, cancellationToken).ConfigureAwait(false);
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                throw new InvalidOperationException(DescribeHttpError(code) + " (descărcare asset).");
            }

            long? total = resp.Content.Headers.ContentLength;
            if (total is not > 0 && asset.Size > 0)
                total = asset.Size;

            await using var src = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fs = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long received = 0;
            var lastReport = -1L;
            progress?.Report(new GitHubDownloadProgress(0, total));
            int read;
            while ((read = await src.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (received - lastReport < 65536 && received != total)
                    continue;
                lastReport = received;
                progress?.Report(new GitHubDownloadProgress(received, total));
            }

            progress?.Report(new GitHubDownloadProgress(received, total ?? received));
        }

        return destinationZipPath;
    }

    /// <summary>
    /// Extracts the zip and writes a helper .cmd that replaces files next to the
    /// running exe after it exits, then restarts. Does not copy into Program Files.
    /// </summary>
    public static string PrepareApplyAndRestart(
        string zipPath,
        string installRoot,
        string exePath,
        int currentPid)
    {
        if (IsProtectedInstallPath(installRoot))
        {
            throw new InvalidOperationException(
                "Instalarea din Program Files nu se actualizează automat (folder doar-citire).\n" +
                "Copiați pachetul portabil sau reinstalați manual. Nu se scrie în Program Files.");
        }

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Arhiva de actualizare lipsește.", zipPath);

        var extractDir = Path.Combine(Path.GetDirectoryName(zipPath) ?? AppPaths.Updates, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var sourceDir = ResolvePublishRoot(extractDir);
        if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
            throw new InvalidOperationException("Arhiva de actualizare este goală.");

        // Empty source json must not wipe a lab's LAN/Cloudflare URLs.
        // A packed json with URLs is copied (so GitHub update can refresh the list).
        StripUnconfiguredLicenseJson(sourceDir);

        var scriptPath = Path.Combine(Path.GetDirectoryName(zipPath) ?? AppPaths.Updates, ApplyScriptName);
        File.WriteAllText(scriptPath, BuildApplyScript(), Encoding.ASCII);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = scriptPath,
            Arguments = currentPid + " " + Quote(sourceDir) + " " + Quote(installRoot) + " " + Quote(exePath),
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? extractDir
        };
        System.Diagnostics.Process.Start(psi);
        return scriptPath;
    }

    public static string ResolvePublishRoot(string extractDir)
    {
        var exe = Directory.EnumerateFiles(extractDir, "UPETAcqLab.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (exe is not null)
            return extractDir;

        var dirs = Directory.GetDirectories(extractDir);
        if (dirs.Length == 1)
        {
            var nested = Directory.EnumerateFiles(dirs[0], "UPETAcqLab.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (nested is not null)
                return dirs[0];
            return dirs[0];
        }

        var deep = Directory.EnumerateFiles(extractDir, "UPETAcqLab.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (deep is not null)
            return Path.GetDirectoryName(deep) ?? extractDir;

        return extractDir;
    }

    public static string LatestReleaseUrl(string owner, string repo) =>
        "https://api.github.com/repos/" + Uri.EscapeDataString(owner.Trim()) + "/" +
        Uri.EscapeDataString(repo.Trim()) + "/releases/latest";

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }

    /// <summary>
    /// Drop a packed <c>license-server.json</c> that has no URLs (author source
    /// tree). Configured json stays in the overlay so labs pick up LAN/Cloudflare.
    /// </summary>
    public static void StripUnconfiguredLicenseJson(string sourceDir)
    {
        var path = Path.Combine(sourceDir, ApplicationKeyConfig.FileName);
        if (!File.Exists(path)) return;
        if (!ApplicationKeyConfig.Load(sourceDir).IsConfigured)
            File.Delete(path);
    }

    /// <summary>
    /// Overlay zip files onto the install folder after the running exe exits.
    /// </summary>
    public static string BuildApplyScript() =>
        """
        @echo off
        setlocal EnableExtensions
        set "WAITPID=%~1"
        set "SRC=%~2"
        set "DST=%~3"
        set "EXE=%~4"
        if "%WAITPID%"=="" exit /b 1
        if "%SRC%"=="" exit /b 1
        if "%DST%"=="" exit /b 1
        :waitloop
        tasklist /FI "PID eq %WAITPID%" 2>nul | findstr /I /C:"%WAITPID%" >nul
        if not errorlevel 1 (
          timeout /t 1 /nobreak >nul
          goto waitloop
        )
        timeout /t 2 /nobreak >nul
        robocopy "%SRC%" "%DST%" /E /IS /IT /NFL /NDL /NJH /NJS /nc /ns /np /R:3 /W:1
        if exist "%EXE%" (
          start "" "%EXE%"
        )
        """;

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        HttpRequestMessage request,
        string? token,
        CancellationToken cancellationToken)
    {
        const int maxHops = 8;
        HttpResponseMessage? last = null;
        var current = request;
        for (var hop = 0; hop < maxHops; hop++)
        {
            last?.Dispose();
            last = await _http.SendAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var code = last.StatusCode;
            if (code is not HttpStatusCode.Moved and not HttpStatusCode.Redirect
                and not HttpStatusCode.RedirectMethod and not HttpStatusCode.TemporaryRedirect
                and not HttpStatusCode.PermanentRedirect)
            {
                return last;
            }

            var location = last.Headers.Location;
            if (location is null)
                return last;

            if (!location.IsAbsoluteUri && current.RequestUri is not null)
                location = new Uri(current.RequestUri, location);

            var follow = new HttpRequestMessage(HttpMethod.Get, location);
            follow.Headers.UserAgent.ParseAdd(UserAgent);
            // Private GitHub assets redirect to a signed objects.githubusercontent.com URL.
            // Forwarding Bearer there makes S3/GitHub return 400/403.
            if (IsGitHubApiHost(location.Host))
                ApplyAuth(follow, token);
            follow.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            current = follow;
        }

        return last ?? throw new InvalidOperationException("Redirect GitHub eșuat.");
    }

    private static bool IsGitHubApiHost(string host) =>
        host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static HttpRequestMessage NewApiRequest(HttpMethod method, string url, string? token)
    {
        var req = new HttpRequestMessage(method, url);
        ApplyAuth(req, token);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        return req;
    }

    private static void ApplyAuth(HttpRequestMessage req, string? token)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private static string DescribeHttpError(int code) =>
        code switch
        {
            401 => "GitHub a cerut autentificare (401). Repo privat sau token respins.",
            403 => "Acces interzis (403). Repo privat sau limită de rată GitHub (60/oră fără token).",
            404 => "Repo/release negăsit (404). Repo inexistent, încă privat, sau fără release latest.",
            _ => "GitHub a răspuns HTTP " + code + "."
        };

    private static Version Normalize(Version v)
    {
        var major = Math.Max(0, v.Major);
        var minor = v.Minor < 0 ? 0 : v.Minor;
        var build = v.Build < 0 ? 0 : v.Build;
        return new Version(major, minor, build);
    }

    private static string Quote(string path) => "\"" + path.Trim('"') + "\"";

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public GitHubAssetDto[]? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
