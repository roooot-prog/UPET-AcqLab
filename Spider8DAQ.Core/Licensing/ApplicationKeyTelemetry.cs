namespace Spider8DAQ.Core.Licensing;

public sealed class ApplicationKeyChannelInfo
{
    public string Name { get; set; } = "";
    public bool On { get; set; }
    public string? Unit { get; set; }
    public string? Sensor { get; set; }
    public string? Reading { get; set; }
    public double? Value { get; set; }
    public double? Lo { get; set; }
    public double? Hi { get; set; }
    /// <summary>ok, hi, lo, sat, usb.</summary>
    public string? Alarm { get; set; }
}

public sealed class ApplicationKeySessionInfo
{
    public bool Rec { get; set; }
    public int RecSeconds { get; set; }
    public string? LastFile { get; set; }
    public int AlarmCount { get; set; }
    public bool Conn { get; set; }
}

public sealed class ApplicationKeyJournalLine
{
    public string? Ts { get; set; }
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = "";
}

/// <summary>WPF App registers a provider; Core stays UI-free. Null provider → version/MAC/log file only.</summary>
public interface IApplicationKeyLiveSnapshot
{
    IReadOnlyList<ApplicationKeyChannelInfo> GetChannels();
    IReadOnlyList<ApplicationKeyJournalLine> GetJournal(int maxLines = 30);
    (string? message, string level) GetLastError();
    (byte[]? jpeg, byte[]? thumb) TryCapture(int maxJpegBytes, int maxThumbBytes, bool force);
    ApplicationKeySessionInfo GetSession();
}

public static class ApplicationKeyLiveSnapshot
{
    public const int JournalLines = 30;
    public const int ScreenshotMaxBytes = 200_000;

    public static IApplicationKeyLiveSnapshot? Provider { get; set; }
}

/// <summary>Short Romanian hint for Admin — only mapped strings, never fake Catman 1893 FAIL.</summary>
public static class ApplicationKeyRemediation
{
    public static string? Suggest(string? lastError)
    {
        if (string.IsNullOrWhiteSpace(lastError)) return null;
        var t = lastError.Trim();

        if (Contains(t, "comunicare pierdută", "comunicare pierduta", "usb/spider8 indisponibil",
                "dest usb absent", "fără cadru omb", "fara cadru omb"))
            return "Spider8 stins sau USB scos — power-on, apoi Conn.";

        if (Contains(t, "10003", "est=10003", "est 10003"))
            return "EST 10003 — reîncercați Start; dacă LED rămâne, power-cycle USB.";

        if (Contains(t, "semnal lipsă", "semnal lipsa", "punte deschisă", "punte deschisa",
                "semnal nu ok"))
            return "Canal gol / punte deschisă — verificați cablajul.";

        if (Contains(t, "half+dummy", "pass (half+dummy)", "skip 1893", "residual"))
            return "Shunt Half+dummy: residual mic e normal — ignorați (nu e FAIL 1893).";

        if (Contains(t, "catman"))
            return "Închideți catman Easy — ține USBHBM exclusiv.";

        if (Contains(t, "port_usb ocupat", "usbhbm"))
            return "USBHBM ocupat — un singur client (nu catman + UPET).";

        return null;
    }

    private static bool Contains(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
