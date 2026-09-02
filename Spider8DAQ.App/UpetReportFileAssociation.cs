using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App;

/// <summary>
/// Registers .upet with Explorer so report files show the UPET app icon.
/// HKCU only — no admin required. Runs at startup (idempotent).
/// </summary>
internal static class UpetReportFileAssociation
{
    private const string ProgId = "UPETAcqLab.Report.1";
    private const string FriendlyType = "Raport UPET AcqLab";

    public static void EnsureRegistered()
    {
        try
        {
            var exe = ResolveExePath();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            var icon = ResolveIconPath(exe);
            var openCmd = $"\"{exe}\" \"%1\"";

            foreach (var extName in new[] { UpetReportFile.Extension, UpetReportFile.LegacyExtension })
            {
                using var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extName);
                ext?.SetValue("", ProgId);
                ext?.SetValue("Content Type", "application/x-upet-report");
            }

            using (var prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                prog?.SetValue("", FriendlyType);
                prog?.SetValue("FriendlyTypeName", FriendlyType);
            }

            using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
                iconKey?.SetValue("", icon);

            using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                cmd?.SetValue("", openCmd);

            NotifyShellAssociationChanged();
        }
        catch
        {
            // non-fatal — association is convenience only
        }
    }

    private static string ResolveExePath()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                return Path.GetFullPath(p);
        }
        catch { /* ignore */ }

        return Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
    }

    private static string ResolveIconPath(string exe)
    {
        var dir = Path.GetDirectoryName(exe) ?? "";
        foreach (var candidate in new[]
                 {
                     Path.Combine(dir, "app.ico"),
                     Path.Combine(dir, "Assets", "app.ico")
                 })
        {
            if (File.Exists(candidate))
                return $"\"{candidate}\",0";
        }

        // Embedded ApplicationIcon in the EXE (UPET logo)
        return $"\"{exe}\",0";
    }

    private static void NotifyShellAssociationChanged()
    {
        try
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* ignore */ }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
