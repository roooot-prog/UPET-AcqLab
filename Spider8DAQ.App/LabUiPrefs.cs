using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.App;

/// <summary>Persisted lab prefs (shunt allowed difference, operator/expert, theme; optional GitHub token leftover).</summary>
public static class LabUiPrefs
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly byte[] TokenEntropy = Encoding.UTF8.GetBytes("UPETAcqLab.GitHubToken.v1");
    private static double? _shuntAllowedDifferencePercent;
    private static bool? _expertMode;
    private static bool? _darkTheme;
    private static string? _gitHubOwner;
    private static string? _gitHubRepo;
    private static string? _gitHubToken;
    private static bool? _checkUpdatesOnStartup;

    private static string PrefsPath => AppPaths.LabPrefs;

    public static double ShuntAllowedDifferencePercent
    {
        get
        {
            _shuntAllowedDifferencePercent ??= ShuntCheck.ClampAllowedDifferencePercent(Load().ShuntAllowedDifferencePercent);
            return _shuntAllowedDifferencePercent.Value;
        }
        set
        {
            var v = ShuntCheck.ClampAllowedDifferencePercent(value);
            _shuntAllowedDifferencePercent = v;
            Save(dto => dto.ShuntAllowedDifferencePercent = v);
        }
    }

    /// <summary>Default false — technician sees operator chrome, not Spider32.dll first.</summary>
    public static bool ExpertMode
    {
        get
        {
            _expertMode ??= Load().ExpertMode;
            return _expertMode.Value;
        }
        set
        {
            _expertMode = value;
            Save(dto => dto.ExpertMode = value);
        }
    }

    public static bool DarkTheme
    {
        get
        {
            _darkTheme ??= Load().DarkTheme;
            return _darkTheme.Value;
        }
        set
        {
            _darkTheme = value;
            Save(dto => dto.DarkTheme = value);
        }
    }

    public static string GitHubOwner
    {
        get
        {
            _gitHubOwner ??= Load().GitHubOwner ?? "";
            return _gitHubOwner;
        }
        set
        {
            _gitHubOwner = value?.Trim() ?? "";
            Save(dto => dto.GitHubOwner = _gitHubOwner);
        }
    }

    public static string GitHubRepo
    {
        get
        {
            _gitHubRepo ??= Load().GitHubRepo ?? "";
            return _gitHubRepo;
        }
        set
        {
            _gitHubRepo = value?.Trim() ?? "";
            Save(dto => dto.GitHubRepo = _gitHubRepo);
        }
    }

    /// <summary>
    /// Fine-grained PAT (Contents: Read on one private repo). Stored DPAPI-protected
    /// in lab-prefs.json — never compiled into the exe.
    /// </summary>
    public static string GitHubToken
    {
        get
        {
            if (_gitHubToken is not null)
                return _gitHubToken;
            var dto = Load();
            _gitHubToken = UnprotectToken(dto);
            if (!string.IsNullOrWhiteSpace(dto.GitHubToken) && string.IsNullOrWhiteSpace(dto.GitHubTokenProtected))
            {
                Save(d =>
                {
                    d.GitHubTokenProtected = ProtectToken(_gitHubToken);
                    d.GitHubToken = null;
                });
            }
            return _gitHubToken;
        }
        set
        {
            _gitHubToken = value?.Trim() ?? "";
            Save(dto =>
            {
                dto.GitHubToken = null;
                dto.GitHubTokenProtected = ProtectToken(_gitHubToken);
            });
        }
    }

    /// <summary>Default true; check is skipped anyway when owner/repo/token are missing.</summary>
    public static bool CheckUpdatesOnStartup
    {
        get
        {
            _checkUpdatesOnStartup ??= Load().CheckUpdatesOnStartup;
            return _checkUpdatesOnStartup.Value;
        }
        set
        {
            _checkUpdatesOnStartup = value;
            Save(dto => dto.CheckUpdatesOnStartup = value);
        }
    }

    public static void SaveGitHubSettings(string owner, string repo, string? tokenIfChanged, bool checkOnStartup)
    {
        _gitHubOwner = owner?.Trim() ?? "";
        _gitHubRepo = repo?.Trim() ?? "";
        _checkUpdatesOnStartup = checkOnStartup;
        if (tokenIfChanged is not null)
            _gitHubToken = tokenIfChanged.Trim();
        Save(dto =>
        {
            dto.GitHubOwner = _gitHubOwner;
            dto.GitHubRepo = _gitHubRepo;
            dto.CheckUpdatesOnStartup = checkOnStartup;
            if (tokenIfChanged is not null)
            {
                dto.GitHubToken = null;
                dto.GitHubTokenProtected = ProtectToken(_gitHubToken);
            }
        });
    }

    public static bool HasGitHubToken => !string.IsNullOrWhiteSpace(GitHubToken);

    private static string ProtectToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "";
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), TokenEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return "";
        }
    }

    private static string UnprotectToken(Dto dto)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dto.GitHubTokenProtected))
            {
                var raw = Convert.FromBase64String(dto.GitHubTokenProtected);
                var bytes = ProtectedData.Unprotect(raw, TokenEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
        }
        catch
        {
            /* corrupt / other Windows user */
        }

        return dto.GitHubToken ?? "";
    }

    private static void Save(Action<Dto> mutate)
    {
        try
        {
            AppPaths.EnsureWritable(AppPaths.Data);
            var dto = Load();
            mutate(dto);
            File.WriteAllText(PrefsPath, JsonSerializer.Serialize(dto, Json));
        }
        catch
        {
            /* ignore prefs write failures */
        }
    }

    private static Dto Load()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(PrefsPath), Json);
                if (dto is not null) return dto;
            }
        }
        catch
        {
            /* defaults */
        }

        return new Dto();
    }

    private sealed class Dto
    {
        public double ShuntAllowedDifferencePercent { get; set; } = ShuntCheck.DefaultTolerancePercent;
        public bool ExpertMode { get; set; }
        public bool DarkTheme { get; set; }
        public string? GitHubOwner { get; set; }
        public string? GitHubRepo { get; set; }
        /// <summary>Legacy plaintext; migrated to GitHubTokenProtected on read.</summary>
        public string? GitHubToken { get; set; }
        /// <summary>DPAPI (CurrentUser) blob — not a raw PAT in the JSON.</summary>
        public string? GitHubTokenProtected { get; set; }
        public bool CheckUpdatesOnStartup { get; set; } = true;
    }
}
