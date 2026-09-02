using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Detects HBM USB IO devices (class HBM_USB_DEVICES / service USBHBM) as seen by Windows.
/// These are NOT COM ports — catman and Spider32.dll talk through usbhbm.sys (VID_10D1).
/// UPET must not redistribute the proprietary INF/SYS; detection only helps the lab pick Spider32 backend.
/// </summary>
public sealed record HbmUsbDeviceInfo(
    string Serial,
    string InstanceId,
    string Description,
    string DisplayLabel);

[SupportedOSPlatform("windows")]
public static class HbmUsbDeviceScanner
{
    public const string VendorIdPrefix = "VID_10D1";

    public static IReadOnlyList<HbmUsbDeviceInfo> GetDevices()
    {
        var results = new List<HbmUsbDeviceInfo>();
        try
        {
            using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbRoot is null) return results;

            foreach (var vidPid in usbRoot.GetSubKeyNames()
                         .Where(n => n.StartsWith(VendorIdPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                using var vidKey = usbRoot.OpenSubKey(vidPid);
                if (vidKey is null) continue;

                foreach (var serial in vidKey.GetSubKeyNames())
                {
                    using var inst = vidKey.OpenSubKey(serial);
                    if (inst is null) continue;

                    var desc = CleanDeviceDesc(inst.GetValue("DeviceDesc") as string)
                               ?? "Spider8 / HBM USB";
                    var service = inst.GetValue("Service") as string;
                    // Prefer devices bound to USBHBM; still list VID_10D1 if service missing (partial install).
                    if (!string.IsNullOrEmpty(service)
                        && !service.Equals("USBHBM", StringComparison.OrdinalIgnoreCase)
                        && !service.Equals("usbhbm", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var instanceId = $@"USB\{vidPid}\{serial}";
                    results.Add(new HbmUsbDeviceInfo(
                        Serial: serial,
                        InstanceId: instanceId,
                        Description: desc,
                        DisplayLabel: $"USB {serial}"));
                }
            }
        }
        catch
        {
            // Registry access can fail in locked-down lab accounts — return empty.
        }

        return results
            .OrderBy(d => d.Serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>True when Windows already has an HBM USB IO device (driver may still be required for IO).</summary>
    /// <remarks>
    /// Registry Enum\USB is <b>not</b> DIGCF_PRESENT — stale instances remain after unplug/power-off.
    /// Do not use this as the live presence check. Prefer <see cref="IsDestInterfacePresent"/>.
    /// </remarks>
    public static bool IsUsbHbmPresent() => GetDevices().Count > 0;

    /// <summary>
    /// Live DEST interface via SetupAPI DIGCF_PRESENT (usbhbm PIPE00/PIPE01). Empty after power-off or unplug.
    /// </summary>
    public static bool IsDestInterfacePresent(string? serialHint = null)
    {
        try
        {
            if (HbmUsbIo.FindDestDevicePaths(serialHint).Count > 0)
                return true;
            if (!string.IsNullOrWhiteSpace(serialHint)
                && HbmUsbIo.FindDestDevicePaths(null).Count > 0)
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Targets for the port combo: real COM ports first, then USBHBM serial strings
    /// (usable as Spider32 port hint; not openable via System.IO.Ports).
    /// </summary>
    public static IReadOnlyList<string> GetConnectionTargets()
    {
        var list = new List<string>();
        list.AddRange(ComPortScanner.GetPorts());
        foreach (var d in GetDevices())
        {
            if (!list.Contains(d.Serial, StringComparer.OrdinalIgnoreCase))
                list.Add(d.Serial);
        }
        return list;
    }

    public static bool IsHbmUsbTarget(string? portOrSerial)
    {
        if (string.IsNullOrWhiteSpace(portOrSerial)) return false;
        var t = portOrSerial.Trim();
        if (t.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.StartsWith("USBHBM", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("USB ", StringComparison.OrdinalIgnoreCase)) return true;
        return GetDevices().Any(d =>
            d.Serial.Equals(t, StringComparison.OrdinalIgnoreCase)
            || d.DisplayLabel.Equals(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string? CleanDeviceDesc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var semi = s.LastIndexOf(';');
        if (semi >= 0 && semi < s.Length - 1)
            s = s[(semi + 1)..].Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
