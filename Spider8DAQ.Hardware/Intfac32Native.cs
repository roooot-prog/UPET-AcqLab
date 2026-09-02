using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Spider8DAQ.Hardware;

/// <summary>
/// P/Invoke to HBM <c>Intfac32.dll</c> (same stack catman Easy uses for USBHBM).
/// Prefer this over raw DEST bulk pipes for Spider8 measurement ASCII.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Intfac32Native
{
    private static IntPtr _lib;
    private static readonly object Gate = new();
    private static bool _bound;
    private static string? _loadedFrom;

    private static OpenInterfaceLibFn? OpenLib;
    private static CloseInterfaceLibFn? CloseLib;
    private static UsbGetMaxFn? UsbMax;
    private static SelectDeviceFn? SelectDev;
    private static ActivateInterpreterFn? Activate;
    private static SpiderWakeFn? Wake;
    private static OpenPortFn? OpenPort;
    private static OpenPortIntFn? OpenPortInt;
    private static ClosePortFn? ClosePort;
    private static SetTimeoutFn? SetTimeout;
    private static WritePortFn? WritePort;
    private static ReadPortFn? ReadPort;
    private static FlushInFn? FlushIn;
    private static GetNumHandlesFn? NumHandles;

    public static bool IsAvailable()
    {
        try
        {
            EnsureLoaded();
            return (OpenPort is not null || OpenPortInt is not null) && WritePort is not null && ReadPort is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string? LoadedFrom => _loadedFrom;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_bound) return;

            var path = ResolveDllPath();
            if (path is null)
                throw new FileNotFoundException(
                    "Intfac32.dll lipsește (vendor\\ sau catmanEasy).");

            var dir = Path.GetDirectoryName(path)!;
            TrySetDllDirectory(dir);
            try { Directory.SetCurrentDirectory(dir); } catch { /* best-effort */ }

            // catman load order
            try { NativeLibrary.Load(Path.Combine(dir, "Papo32.dll")); } catch { /* optional */ }
            try { NativeLibrary.Load(Path.Combine(dir, "interlnk.dll")); } catch { /* optional */ }
            try { NativeLibrary.Load(Path.Combine(dir, "Interlnk.dll")); } catch { /* optional */ }

            _lib = NativeLibrary.Load(path);
            _loadedFrom = path;

            OpenLib = GetExport<OpenInterfaceLibFn>(_lib, "HBM_OpenInterfaceLib");
            CloseLib = GetExport<CloseInterfaceLibFn>(_lib, "HBM_CloseInterfaceLib");
            UsbMax = GetExport<UsbGetMaxFn>(_lib, "USB_GetMaxUsbDevice");
            SelectDev = GetExport<SelectDeviceFn>(_lib, "HBM_SelectDevice");
            Activate = GetExport<ActivateInterpreterFn>(_lib, "HBM_ActivateInterpreter");
            Wake = GetExport<SpiderWakeFn>(_lib, "HBM_Spider8_WakeUp");
            OpenPort = GetExport<OpenPortFn>(_lib, "HBM_OpenPort");
            OpenPortInt = GetExport<OpenPortIntFn>(_lib, "HBM_OpenPort");
            ClosePort = GetExport<ClosePortFn>(_lib, "HBM_ClosePort");
            SetTimeout = GetExport<SetTimeoutFn>(_lib, "HBM_SetTimeout");
            WritePort = GetExport<WritePortFn>(_lib, "HBM_WritePort");
            ReadPort = GetExport<ReadPortFn>(_lib, "HBM_ReadPort");
            FlushIn = GetExport<FlushInFn>(_lib, "HBM_FlushInQueue");
            NumHandles = GetExport<GetNumHandlesFn>(_lib, "HBM_GetNumOpenHandles");

            if ((OpenPort is null && OpenPortInt is null) || WritePort is null || ReadPort is null)
                throw new InvalidOperationException("Intfac32.dll fără HBM_OpenPort/WritePort/ReadPort.");

            _bound = true;
        }
    }

    public static int OpenInterfaceLib() => OpenLib?.Invoke() ?? -1;
    public static int CloseInterfaceLib() => CloseLib?.Invoke() ?? -1;
    public static int GetMaxUsbDevice() => UsbMax?.Invoke() ?? 0;
    public static int SelectDevice(int id) => SelectDev?.Invoke(id) ?? -1;
    public static int ActivateInterpreter() => Activate?.Invoke() ?? -1;
    public static int Spider8WakeUp() => Wake?.Invoke() ?? -1;
    public static int ClosePortNow() => ClosePort?.Invoke() ?? -1;
    public static int SetTimeoutMs(int ms) => SetTimeout?.Invoke(ms) ?? -1;
    public static int FlushInQueue() => FlushIn?.Invoke() ?? -1;
    public static int GetNumOpenHandles() => NumHandles?.Invoke() ?? -1;

    /// <summary>
    /// Release stale Intfac USB claim before OpenPort / DEST fallback.
    /// ClosePort twice + optional CloseInterfaceLib — prevents PORT_USB=-1 from leftover handles.
    /// </summary>
    public static void ReleaseUsbStack(bool closeLib = false)
    {
        try { FlushInQueue(); } catch { /* ignore */ }
        try { ClosePortNow(); } catch { /* ignore */ }
        Thread.Sleep(40);
        try { ClosePortNow(); } catch { /* ignore */ }
        if (closeLib)
        {
            try { CloseInterfaceLib(); } catch { /* ignore */ }
            Thread.Sleep(60);
        }
        else
            Thread.Sleep(40);
    }

    /// <summary>True if catman Easy holds USBHBM exclusively (blocks HBM_OpenPort).</summary>
    public static bool IsCatmanEasyRunning()
    {
        try
        {
            foreach (var name in new[] { "catmanEASY", "catmanEASY_P", "catmanEasy" })
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    /// <summary>
    /// Open USB/COM via Intfac. Prefer integer PORT_* (VB PortNum%) — string names always yield ERR_INVALID_PORT=-9.
    /// Returns &gt;=0 on success.
    /// </summary>
    public static int TryOpenPort(string port, int baud = 9600, int recvBuf = 4096)
    {
        EnsureLoaded();

        // Map common names → PORT_* integers (S8_dll.h / Spider32).
        if (TryMapPortNumber(port, out var portNum))
        {
            var rcInt = TryOpenPortNumber(portNum, baud, recvBuf);
            // Integer signature is authoritative: -1 open-failed, -9 invalid, >=0 ok.
            return rcInt;
        }

        if (OpenPort is null) return -1;

        // catman display strings (rarely accepted; often -9).
        foreach (var parity in new[] { "e", "n", "N" })
        {
            try
            {
                var rc = OpenPort(port, baud, parity, 8, 1, recvBuf);
                if (rc >= 0) return rc;
                if (rc != -9) return rc;
            }
            catch
            {
                // Wrong marshalling — try next.
            }
        }

        return -9;
    }

    /// <summary>Open by PORT_* integer (PORT_COM1=1, PORT_USB=100, PORT_LPT1=90).</summary>
    public static int TryOpenPortNumber(int portNum, int baud = 9600, int recvBuf = 4096)
    {
        EnsureLoaded();
        if (OpenPortInt is null) return -1;
        foreach (var parity in new[] { "e", "n" })
        {
            try
            {
                var rc = OpenPortInt(portNum, baud, parity, 8, 1, recvBuf);
                if (rc >= 0) return rc;
                // -1 = open failed (port known); -9 = invalid — keep trying parity.
                if (rc != -9 && rc != -1)
                    return rc;
                if (rc == -1)
                    return rc; // recognized but failed
            }
            catch
            {
                return -99;
            }
        }
        return -1;
    }

    public static bool TryMapPortNumber(string? port, out int portNum)
    {
        portNum = 0;
        if (string.IsNullOrWhiteSpace(port)) return false;
        var p = port.Trim();
        if (int.TryParse(p, out portNum)) return true;
        if (p.Equals("USB", StringComparison.OrdinalIgnoreCase)
            || p.Equals("USB0", StringComparison.OrdinalIgnoreCase)
            || p.Equals("USB1", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("USBHBM", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("USB - Device", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("USB ", StringComparison.OrdinalIgnoreCase)
            || p.Equals("PORT_USB", StringComparison.OrdinalIgnoreCase))
        {
            portNum = 100; // PORT_USB
            return true;
        }
        if (p.Equals("COM1", StringComparison.OrdinalIgnoreCase) || p.StartsWith("COM1 ", StringComparison.OrdinalIgnoreCase))
        { portNum = 1; return true; }
        if (p.Equals("COM2", StringComparison.OrdinalIgnoreCase) || p.StartsWith("COM2 ", StringComparison.OrdinalIgnoreCase))
        { portNum = 2; return true; }
        if (p.Equals("LPT1", StringComparison.OrdinalIgnoreCase)) { portNum = 90; return true; }
        if (p.Equals("LPT2", StringComparison.OrdinalIgnoreCase)) { portNum = 91; return true; }
        if (p.Equals("GPIB", StringComparison.OrdinalIgnoreCase) || p.Equals("GPIB0", StringComparison.OrdinalIgnoreCase))
        { portNum = 0; return true; }
        return false;
    }

    /// <summary>Enumerate likely USB port names / PORT_* for HBM_OpenPort.</summary>
    public static IEnumerable<string> CandidateUsbPortNames(string? serialHint)
    {
        // Integer PORT_USB first (correct VB signature) — string "100" also maps via TryMapPortNumber.
        yield return "100";
        yield return "PORT_USB";
        yield return "USB";
        yield return "USB0";

        var serial = HbmUsbIo.NormalizeSerial(serialHint);
        if (string.IsNullOrEmpty(serial) && HbmUsbDeviceScanner.IsUsbHbmPresent())
            serial = HbmUsbDeviceScanner.GetDevices()[0].Serial;

        if (!string.IsNullOrEmpty(serial))
        {
            yield return "USB - Device " + serial;
            yield return "USB " + serial;
            yield return serial;
        }

        yield return "USB1";
        yield return "1"; // COM1 last-resort
    }

    public static int Write(string cmd)
    {
        EnsureLoaded();
        if (WritePort is null) return -1;
        var s = cmd;
        if (!s.EndsWith('\r') && !s.EndsWith('\n'))
            s += "\r";
        return WritePort(s);
    }

    public static string Read(int maxChars = 2048)
    {
        EnsureLoaded();
        if (ReadPort is null) return "";
        var sb = new StringBuilder(maxChars);
        _ = ReadPort(sb, maxChars);
        return sb.ToString();
    }

    public static string Transact(string cmd, int settleMs = 80, int readRounds = 4)
    {
        Write(cmd);
        if (settleMs > 0) Thread.Sleep(settleMs);
        var sb = new StringBuilder();
        for (var i = 0; i < readRounds; i++)
        {
            var chunk = Read(2048);
            if (!string.IsNullOrEmpty(chunk))
                sb.Append(chunk);
            else if (sb.Length > 0)
                break;
            else
                Thread.Sleep(20);
        }
        return sb.ToString().Trim();
    }

    public static string? ResolveDllPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "vendor", "Intfac32.dll"),
            Path.Combine(baseDir, "Intfac32.dll"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "Intfac32.dll")),
            @"C:\Program Files (x86)\HBM\catmanEasy\intfac32.dll",
            @"C:\Program Files (x86)\HBM\catmanEasy\Intfac32.dll",
            @"C:\Program Files\HBM\catmanEasy\Intfac32.dll"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TrySetDllDirectory(string dir)
    {
        try
        {
            var k32 = NativeLibrary.Load("kernel32.dll");
            if (NativeLibrary.TryGetExport(k32, "SetDllDirectoryW", out var ptr))
            {
                var fn = Marshal.GetDelegateForFunctionPointer<SetDllDirectoryWDelegate>(ptr);
                fn(dir);
            }
        }
        catch { /* best-effort */ }
    }

    private static T? GetExport<T>(IntPtr lib, string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(lib, name, out var ptr) || ptr == IntPtr.Zero)
            return null;
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenInterfaceLibFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloseInterfaceLibFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UsbGetMaxFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SelectDeviceFn(int id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ActivateInterpreterFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SpiderWakeFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int OpenPortFn(string port, int baud, string parity, int data, int stop, int recvBuf);

    // VB: HBM_OpenPort%(PortNum%, BaudRate&, Parity$, DataBits%, StopBits%, RecvBuff%)
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int OpenPortIntFn(int port, int baud, string parity, int data, int stop, int recvBuf);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClosePortFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetTimeoutFn(int ms);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int WritePortFn(string s);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int ReadPortFn(StringBuilder sb, int max);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FlushInFn();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetNumHandlesFn();

    private delegate bool SetDllDirectoryWDelegate(string path);
}
