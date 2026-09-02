namespace Spider8DAQ.Core.Time;

/// <summary>
/// Wall-clock for user-facing stamps: always follows the PC local timezone
/// (Windows Date &amp; Time). Internal elapsed/timing may still use UTC ticks.
/// </summary>
public static class AppClock
{
    /// <summary>PC local now (same as DateTime.Now).</summary>
    public static DateTime Now => DateTime.Now;

    /// <summary>Convert any DateTime to PC local for display / CSV.</summary>
    public static DateTime ToPcLocal(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };

    /// <summary>ISO-8601 with local offset (e.g. 2026-08-10T16:40:00.123+03:00).</summary>
    public static string FormatIso(DateTime value) =>
        ToPcLocal(value).ToString("O");

    /// <summary>Compact local stamp for UI / filenames.</summary>
    public static string FormatUi(DateTime? value = null) =>
        (value is DateTime dt ? ToPcLocal(dt) : Now).ToString("yyyy-MM-dd HH:mm:ss");

    public static string FormatUiShort(DateTime? value = null) =>
        (value is DateTime dt ? ToPcLocal(dt) : Now).ToString("HH:mm:ss");

    /// <summary>Time-only for Excel «Date» column (PC local), e.g. 16:57:26.774.</summary>
    public static string FormatTimeOnly(DateTime value) =>
        ToPcLocal(value).ToString("HH:mm:ss.fff");
}
