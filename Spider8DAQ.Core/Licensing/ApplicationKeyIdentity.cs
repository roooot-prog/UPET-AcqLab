using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;

namespace Spider8DAQ.Core.Licensing;

/// <summary>FileVersion + MAC for license heartbeat. Empty LicenseServerUrl still skips sending.</summary>
public static class ApplicationKeyIdentity
{
    public static string LocalFileVersion()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                var fromParts = FormatVersionParts(
                    fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
                var fromFile = CompactFileVersion(fvi.FileVersion);
                var fromProduct = CompactFileVersion(fvi.ProductVersion);
                // Prefer Win32 FILEVERSION (3.3.103) over a clipped string like "1.103".
                if (LooksLikeFullLabVersion(fromParts))
                    return fromParts;
                if (LooksLikeFullLabVersion(fromFile))
                    return fromFile;
                if (LooksLikeFullLabVersion(fromProduct))
                    return fromProduct;
                if (!string.IsNullOrEmpty(fromFile))
                    return fromFile;
                if (!string.IsNullOrEmpty(fromParts))
                    return fromParts;
            }
        }
        catch
        {
            /* fall through */
        }

        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var attr = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (!string.IsNullOrWhiteSpace(attr))
                return CompactFileVersion(attr);
            var v = asm.GetName().Version;
            if (v is not null)
                return CompactFileVersion(v.ToString());
        }
        catch
        {
            /* ignore */
        }

        return "";
    }

    /// <summary>3.3.103.0 → 3.3.103 so Admin matches csproj FileVersion.</summary>
    public static string CompactFileVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            t = t[1..];
        if (!Version.TryParse(t, out var v))
            return t;
        return FormatVersionParts(v.Major, v.Minor, v.Build, v.Revision);
    }

    /// <summary>3.3.103 not 1.103 (two-part / clipped) and not 1.0.0 (unset assembly).</summary>
    public static bool LooksLikeFullLabVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            t = t[1..];
        if (!Version.TryParse(t, out var v))
            return false;
        return v.Major >= 2 && v.Minor >= 0 && v.Build >= 0;
    }

    public static string FormatVersionParts(int major, int minor, int build, int revision)
    {
        if (major < 0) major = 0;
        if (minor < 0) minor = 0;
        if (build < 0)
            return $"{major}.{minor}";
        if (revision > 0)
            return $"{major}.{minor}.{build}.{revision}";
        return $"{major}.{minor}.{build}";
    }

    public static IReadOnlyList<string> LocalMacAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                byte[] bytes;
                try { bytes = nic.GetPhysicalAddress().GetAddressBytes(); }
                catch { continue; }
                if (bytes.Length != 6) continue;
                var allZero = true;
                foreach (var b in bytes)
                {
                    if (b != 0) { allZero = false; break; }
                }
                if (allZero) continue;
                var s = string.Join(":", bytes.Select(b => b.ToString("X2")));
                if (!list.Contains(s, StringComparer.OrdinalIgnoreCase))
                    list.Add(s);
            }
        }
        catch
        {
            /* ignore — never block heartbeat */
        }

        return list;
    }

    public static string JoinMacs(IEnumerable<string>? macs) =>
        string.Join(", ", (macs ?? Array.Empty<string>()).Where(m => !string.IsNullOrWhiteSpace(m)));
}
