namespace Spider8DAQ.Core.Licensing;

/// <summary>Aprobă grants a 30-day window, not a perpetual key.</summary>
public static class ApplicationKeyValidity
{
    public const int DurationDays = 30;

    public static DateTime FromApprovalUtc(DateTime? nowUtc = null) =>
        ToUtc(nowUtc ?? DateTime.UtcNow).AddDays(DurationDays);

    public static bool IsExpired(DateTime? validUntilUtc, DateTime? nowUtc = null)
    {
        if (validUntilUtc is null)
            return true;
        var until = ToUtc(validUntilUtc.Value);
        if (until.Year < 2000)
            return true;
        return ToUtc(nowUtc ?? DateTime.UtcNow) > until;
    }

    public static DateTime ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    /// <summary>Whole days until expiry (0 = today or already expired). Null if no window.</summary>
    public static int? DaysRemaining(DateTime? validUntilUtc, DateTime? nowUtc = null)
    {
        if (validUntilUtc is null) return null;
        var until = ToUtc(validUntilUtc.Value);
        if (until.Year < 2000) return null;
        var now = ToUtc(nowUtc ?? DateTime.UtcNow);
        if (now > until) return 0;
        return (int)Math.Floor((until - now).TotalDays);
    }

    /// <summary>Glanceable Romanian: "12" or "Expiră în 3 zile". Pending → "—".</summary>
    public static string FormatDaysRemainingRo(int? days, bool pending, bool expired)
    {
        if (pending) return "—";
        if (expired) return "0";
        if (days is null) return "—";
        if (days.Value <= 0) return "Expiră azi";
        if (days.Value == 1) return "Expiră în 1 zi";
        if (days.Value <= 7) return "Expiră în " + days.Value + " zile";
        return days.Value.ToString();
    }

    public static bool IsUrgentDays(int? days, bool expired) =>
        expired || (days is not null && days.Value <= 7);

    /// <summary>Prelungește: max(now, current expiry) + 30 days. No approve/pending wait.</summary>
    public static DateTime ExtendFromUtc(DateTime? currentExpiryUtc, DateTime? nowUtc = null)
    {
        var now = ToUtc(nowUtc ?? DateTime.UtcNow);
        if (currentExpiryUtc is null)
            return now.AddDays(DurationDays);
        var until = ToUtc(currentExpiryUtc.Value);
        if (until.Year < 2000 || until < now)
            until = now;
        return until.AddDays(DurationDays);
    }
}
