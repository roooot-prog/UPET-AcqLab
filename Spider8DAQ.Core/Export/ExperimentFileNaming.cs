namespace Spider8DAQ.Core.Export;

/// <summary>
/// Unified lab file names next to recordings:
/// <c>{probă}_{yyyyMMdd_HHmmss}.csv</c>,
/// <c>{probă}_{yyyyMMdd_HHmmss}_before.jpg</c>,
/// <c>{probă}_{yyyyMMdd_HHmmss}_after.jpg</c>,
/// <c>{probă}_{yyyyMMdd_HHmmss}_raport.xlsx</c> (pdf/html/upet…).
/// </summary>
public static class ExperimentFileNaming
{
    public const string RoleBefore = "before";
    public const string RoleAfter = "after";
    public const string RoleRaport = "raport";
    /// <summary>Industrial lab PDF: <c>{stem}_industrial.pdf</c>.</summary>
    public const string RoleIndustrial = "industrial";
    /// <summary>Clip epruvetă pe durata Record: <c>{stem}_video.mp4</c>.</summary>
    public const string RoleVideo = "video";

    public static string SanitizeToken(string? raw, string fallback = "proba")
    {
        var s = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        s = s.Replace(' ', '_');
        while (s.Contains("__", StringComparison.Ordinal))
            s = s.Replace("__", "_", StringComparison.Ordinal);
        s = s.Trim('_');
        if (s.Length > 48) s = s[..48];
        return string.IsNullOrWhiteSpace(s) ? fallback : s;
    }

    /// <summary>Stem shared by one experiment session: <c>proba_20260815_153012</c>.</summary>
    public static string BuildStem(string? sampleId, DateTime when) =>
        $"{SanitizeToken(sampleId)}_{when:yyyyMMdd_HHmmss}";

    /// <summary>
    /// Full file name. Empty <paramref name="role"/> → stem + extension (CSV recording).
    /// Non-empty role → stem_role.ext (before / after / raport).
    /// </summary>
    public static string BuildFileName(string? sampleId, DateTime when, string? role, string extension)
    {
        var stem = BuildStem(sampleId, when);
        var ext = NormalizeExt(extension);
        if (string.IsNullOrWhiteSpace(role))
            return stem + ext;
        return $"{stem}_{SanitizeToken(role, "fisier")}{ext}";
    }

    public static string BuildPath(string directory, string? sampleId, DateTime when, string? role, string extension) =>
        Path.Combine(directory, BuildFileName(sampleId, when, role, extension));

    /// <summary>File name without extension (for camera preferred base).</summary>
    public static string BuildBaseName(string? sampleId, DateTime when, string? role) =>
        BuildFileName(sampleId, when, role, "");

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into recordings with the unified name.
    /// Returns the destination path, or empty if source is missing.
    /// </summary>
    public static string EnsureUnifiedCopy(
        string? sourcePath,
        string directory,
        string? sampleId,
        DateTime when,
        string role,
        bool deleteSourceIfDifferent = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return sourcePath ?? "";

        Directory.CreateDirectory(directory);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        var dest = UniquePath(BuildPath(directory, sampleId, when, role, ext));
        var srcFull = Path.GetFullPath(sourcePath);
        var destFull = Path.GetFullPath(dest);
        if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
            return destFull;

        File.Copy(sourcePath, dest, overwrite: true);
        if (deleteSourceIfDifferent)
        {
            try { File.Delete(sourcePath); } catch { /* ignore */ }
        }

        return dest;
    }

    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{stem}_{DateTime.Now:HHmmssfff}{ext}");
    }

    private static string NormalizeExt(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return "";
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
