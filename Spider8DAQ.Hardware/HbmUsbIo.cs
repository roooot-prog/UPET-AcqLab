using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Opens HBM USB IO (usbhbm.sys) DEST interface as dual bulk pipes:
/// OUT = path\PIPE01, IN = path\PIPE00 (same model as Intfac32 read/write handles).
/// Spider8 ASCII over USB uses LF (\n) request terminators (see catman bulk capture).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HbmUsbIo
{
    public static readonly Guid DestInterfaceGuid = new("1B447280-1A9B-11D3-ADCA-444553540000");
    public const string Vid10D1 = "vid_10d1";

    public static IReadOnlyList<string> FindDestDevicePaths(string? serialHint = null)
    {
        var results = new List<string>();
        var guid = DestInterfaceGuid;
        var set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == InvalidHandle)
            return results;

        try
        {
            for (uint i = 0; i < 64; i++)
            {
                var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                    break;

                if (!TryGetPath(set, ref data, out var path) || string.IsNullOrEmpty(path))
                    continue;

                if (!path.Contains(Vid10D1, StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("USBHBM", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(serialHint))
                {
                    var hint = NormalizeSerial(serialHint);
                    if (!path.Contains(hint, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                results.Add(path);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return results;
    }

    public static string NormalizeSerial(string? serialHint)
    {
        if (string.IsNullOrWhiteSpace(serialHint)) return "";
        var hint = serialHint.Trim();
        if (hint.StartsWith("USB ", StringComparison.OrdinalIgnoreCase))
            hint = hint[4..].Trim();
        return hint;
    }

    public static SafeHbmUsbHandle Open(string? serialHint = null)
    {
        var paths = FindDestDevicePaths(serialHint);
        if (paths.Count == 0 && !string.IsNullOrWhiteSpace(serialHint))
            paths = FindDestDevicePaths(null);
        if (paths.Count == 0)
            throw new InvalidOperationException(
                "Niciun dispozitiv USBHBM (interfață DEST) găsit. Verificați driverul HBM USB IO și cablul.");

        Exception? last = null;
        foreach (var path in paths)
        {
            try
            {
                return SafeHbmUsbHandle.OpenPipes(path);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "USBHBM vizibil dar pipe-urile PIPE00/PIPE01 nu s-au putut deschide (închideți catman Easy). " +
            (last?.Message ?? ""),
            last);
    }

    private static bool TryGetPath(IntPtr set, ref SpDeviceInterfaceData data, out string path)
    {
        path = "";
        SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required == 0 || required > 4096)
            return false;

        var buf = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buf, required, out _, IntPtr.Zero))
                return false;

            path = Marshal.PtrToStringUni(buf + 4) ?? "";
            return path.Length > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    internal sealed class SafeHbmUsbHandle : IDisposable
    {
        private IntPtr _hin;
        private IntPtr _hout;
        private readonly object _ioLock = new();

        public string Path { get; }
        public bool IsOpen =>
            _hin != IntPtr.Zero && _hin != InvalidHandle
            && _hout != IntPtr.Zero && _hout != InvalidHandle;

        private SafeHbmUsbHandle(IntPtr hin, IntPtr hout, string path)
        {
            _hin = hin;
            _hout = hout;
            Path = path;
        }

        public static SafeHbmUsbHandle OpenPipes(string basePath)
        {
            // Prefer PIPE00/PIPE01 names (bulkusb); fall back to \0 \1.
            foreach (var (rin, wout) in new[] { ("\\PIPE00", "\\PIPE01"), ("\\0", "\\1") })
            {
                var hin = CreateFileW(
                    basePath + rin, GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (hin == InvalidHandle) continue;

                var hout = CreateFileW(
                    basePath + wout, GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (hout == InvalidHandle)
                {
                    CloseHandle(hin);
                    continue;
                }

                return new SafeHbmUsbHandle(hin, hout, basePath + wout);
            }

            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "CreateFile PIPE00/PIPE01 eșuat");
        }

        public void Write(ReadOnlySpan<byte> data, int timeoutMs = 2000)
        {
            lock (_ioLock)
            {
                EnsureOpen();
                var buf = data.ToArray();
                OverlappedIo(_hout, write: true, buf, buf.Length, timeoutMs, out var transferred);
                if (transferred != buf.Length)
                    throw new IOException($"USBHBM WriteFile scurt: {transferred}/{buf.Length}");
            }
        }

        public int Read(byte[] buffer, int timeoutMs = 500)
        {
            lock (_ioLock)
            {
                EnsureOpen();
                try
                {
                    OverlappedIo(_hin, write: false, buffer, buffer.Length, timeoutMs, out var transferred);
                    return transferred;
                }
                catch (TimeoutException)
                {
                    return 0;
                }
            }
        }

        /// <summary>Send ASCII command (LF-terminated) and collect response until CR/LF or timeout.</summary>
        public string TransactAscii(string command, int timeoutMs = 2000)
        {
            var payload = command;
            if (!payload.EndsWith("\n", StringComparison.Ordinal) && !payload.EndsWith("\r", StringComparison.Ordinal))
                payload += "\n";
            Write(Encoding.ASCII.GetBytes(payload), Math.Min(timeoutMs, 800));
            var deadline = Environment.TickCount64 + timeoutMs;
            var sb = new StringBuilder();
            var buf = new byte[512];
            while (Environment.TickCount64 < deadline)
            {
                var remain = (int)Math.Max(20, deadline - Environment.TickCount64);
                var n = Read(buf, Math.Min(remain, 120));
                if (n > 0)
                {
                    sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                    if (sb.ToString().IndexOfAny(['\r', '\n']) >= 0)
                        break;
                }
            }
            return sb.ToString().Trim();
        }

        private void EnsureOpen()
        {
            if (!IsOpen)
                throw new InvalidOperationException("USBHBM handle închis.");
        }

        public void Dispose()
        {
            var hin = Interlocked.Exchange(ref _hin, IntPtr.Zero);
            var hout = Interlocked.Exchange(ref _hout, IntPtr.Zero);
            if (hin != IntPtr.Zero && hin != InvalidHandle)
            {
                CancelIo(hin);
                CloseHandle(hin);
            }
            if (hout != IntPtr.Zero && hout != InvalidHandle)
            {
                CancelIo(hout);
                CloseHandle(hout);
            }
        }

        private static unsafe void OverlappedIo(
            IntPtr handle, bool write, byte[] buffer, int length, int timeoutMs, out int transferred)
        {
            transferred = 0;
            using var evt = new ManualResetEvent(false);
            var ovPtr = Marshal.AllocHGlobal(Marshal.SizeOf<OverlappedStruct>());
            try
            {
                var ov = new OverlappedStruct
                {
                    EventHandle = evt.SafeWaitHandle.DangerousGetHandle()
                };
                Marshal.StructureToPtr(ov, ovPtr, false);

                fixed (byte* pBuf = buffer)
                {
                    var ok = write
                        ? WriteFile(handle, (IntPtr)pBuf, length, out _, ovPtr)
                        : ReadFile(handle, (IntPtr)pBuf, length, out _, ovPtr);

                    var err = Marshal.GetLastWin32Error();
                    if (!ok && err != ErrorIoPending)
                        throw new System.ComponentModel.Win32Exception(err, write ? "WriteFile" : "ReadFile");

                    if (!ok)
                    {
                        if (!evt.WaitOne(Math.Max(1, timeoutMs)))
                        {
                            CancelIo(handle);
                            GetOverlappedResult(handle, ovPtr, out _, true);
                            throw new TimeoutException(write ? "USBHBM write timeout" : "USBHBM read timeout");
                        }
                    }

                    if (!GetOverlappedResult(handle, ovPtr, out transferred, true))
                    {
                        var e2 = Marshal.GetLastWin32Error();
                        if (e2 is 995 or 121)
                            throw new TimeoutException(write ? "USBHBM write aborted" : "USBHBM read aborted");
                        throw new System.ComponentModel.Win32Exception(e2);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ovPtr);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlappedStruct
    {
        public IntPtr InternalLow;
        public IntPtr InternalHigh;
        public int OffsetLow;
        public int OffsetHigh;
        public IntPtr EventHandle;
    }

    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const int GenericRead = unchecked((int)0x80000000);
    private const int GenericWrite = 0x40000000;
    private const int FileShareRead = 0x1;
    private const int FileShareWrite = 0x2;
    private const int OpenExisting = 3;
    private const int FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName, int desiredAccess, int shareMode, IntPtr security,
        int creationDisposition, int flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr handle, IntPtr buffer, int numberOfBytesToWrite,
        out int numberOfBytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr handle, IntPtr buffer, int numberOfBytesToRead,
        out int numberOfBytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        IntPtr handle, IntPtr overlapped, out int numberOfBytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIo(IntPtr handle);
}
