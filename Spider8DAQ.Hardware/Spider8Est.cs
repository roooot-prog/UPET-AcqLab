namespace Spider8DAQ.Hardware;

/// <summary>
/// Spider8 EST? semantics (HBM command help EST.htm):
/// - 0 / empty = no error
/// - 10000 = reset status after power-on / BDR / BDP — MUST be read with EST? as first command;
///   reading clears it and turns off the red ERROR LED. Other cmds are rejected until then.
/// - 10001–10020 = hard errors (10005 wrong param / ParOutL; 10003 amplifier/channel LED —
///   UI: «LED Error pe aparat — date OK / power-cycle dacă persistă» when samples still flow).
/// - 10021–10022 = warnings
/// Reading EST? always clears the stored error and switches the red ERROR LED off.
/// A single hard code after SoftSetup/ACT is often recoverable (re-query EST? → 0).
/// Only hard codes that persist across consecutive EST? reads need power-cycle.
/// DCL is a no-op on Spider8 (compatibility only) — do not use it to clear LED.
/// </summary>
internal static class Spider8Est
{
    public const int ResetStatus = 10000;
    public const int HardErrorMin = 10001;
    public const int HardErrorMax = 10020;
    /// <summary>Observed hard LED (amplifier/channel). Data may still stream; power-cycle if sticky.</summary>
    public const int LedError10003 = 10003;

    /// <summary>How many consecutive hard EST? replies before treating as stuck (power-cycle).</summary>
    public const int StuckHardConsecutive = 2;

    public static IEnumerable<int> ParseCodes(string? est)
    {
        if (string.IsNullOrWhiteSpace(est)) yield break;

        var any = false;
        // Mid-stream replies can be mixed with OMB binary (#0…). Prefer ASCII digit runs.
        foreach (var part in est.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Skip OMB binary framing leftovers
            if (part.StartsWith('#') || part.IndexOfAny(['#', '\0']) >= 0)
                continue;
            if (int.TryParse(part.Trim(), out var code))
            {
                any = true;
                yield return code;
            }
        }
        if (any) yield break;

        // Fallback: scan for 100xx / bare 0 in noisy buffers
        for (var i = 0; i < est.Length; i++)
        {
            if (!char.IsDigit(est[i])) continue;
            var j = i;
            while (j < est.Length && char.IsDigit(est[j])) j++;
            var len = j - i;
            if (len is >= 1 and <= 5 && int.TryParse(est.AsSpan(i, len), out var code))
            {
                if (code == 0 || code is >= 10000 and <= 10099)
                    yield return code;
            }
            i = j;
        }
    }

    public static int? PrimaryCode(string? est)
    {
        int? best = null;
        foreach (var code in ParseCodes(est))
        {
            if (code >= HardErrorMin && code <= HardErrorMax) return code;
            if (code == ResetStatus) best ??= code;
            if (code == 0) best ??= 0;
            best ??= code;
        }
        return best;
    }

    /// <summary>True for hard fault codes 10001–10020 (not reset-ack 10000).</summary>
    public static bool IsHardError(string? est)
    {
        if (string.IsNullOrWhiteSpace(est)) return false;
        foreach (var code in ParseCodes(est))
        {
            if (code >= HardErrorMin && code <= HardErrorMax)
                return true;
        }
        // Do not match free-text "Err" — noisy buffers / status strings caused false LED ERROR banners.
        return false;
    }

    public static bool IsResetStatus(string? est)
    {
        foreach (var code in ParseCodes(est))
            if (code == ResetStatus) return true;
        return false;
    }

    public static bool IsClear(string? est)
    {
        var c = PrimaryCode(est);
        return c is null or 0;
    }

    /// <summary>
    /// Poll EST? until healthy, or until a hard code persists across consecutive reads.
    /// One SoftSetup/ACT 10005 that clears on the next EST? is NOT a power-cycle case.
    /// </summary>
    /// <param name="readEst">Sends EST? and returns raw reply (may throw).</param>
    /// <param name="log">Optional status line (UI / journal).</param>
    /// <param name="when">Context label for logs.</param>
    /// <param name="lastCode">Last primary EST code seen.</param>
    /// <param name="sawRecoverableHard">True if at least one hard code was cleared by a later EST?.</param>
    /// <param name="maxReads">Max EST? attempts.</param>
    /// <returns>False only when hard error is stuck (power-cycle required).</returns>
    public static bool TryReachHealthy(
        Func<string?> readEst,
        Action<string>? log,
        string when,
        out int? lastCode,
        out bool sawRecoverableHard,
        int maxReads = 6)
    {
        lastCode = null;
        sawRecoverableHard = false;
        var consecutiveHard = 0;

        for (var i = 0; i < maxReads; i++)
        {
            string? est;
            try { est = readEst(); }
            catch (Exception ex)
            {
                log?.Invoke($"EST? {when} write: {Trunc(ex.Message, 60)}");
                Thread.Sleep(50);
                continue;
            }

            lastCode = PrimaryCode(est);

            if (IsHardError(est))
            {
                consecutiveHard++;
                if (consecutiveHard >= StuckHardConsecutive)
                {
                    log?.Invoke(
                        $"EST? {when}: {Trunc(est ?? "", 40)} — Power-cycle Spider8 — LED ERROR");
                    return false;
                }

                // First hard reply: HBM clears LED/storage on read — re-query before refusing.
                sawRecoverableHard = true;
                log?.Invoke(
                    $"EST? {when}: {Trunc(est ?? "", 40)} — încerc clear (citire EST?)");
                Thread.Sleep(50);
                continue;
            }

            consecutiveHard = 0;

            if (IsResetStatus(est))
            {
                log?.Invoke($"EST? {when}: 10000 reset — ACK #{i + 1}");
                Thread.Sleep(50);
                continue;
            }

            if (sawRecoverableHard)
                log?.Invoke($"EST? {when}: recuperat după hard → {UiLedMessage(lastCode, samplesOk: false)}");
            else if (!string.IsNullOrWhiteSpace(est) && est != "0" && lastCode is not null and not 0)
                log?.Invoke($"EST? {when}: " + Trunc(est!, 40));

            return true;
        }

        // Empty / timeout after open is common — allow SoftSetup; do not demand power-cycle.
        return consecutiveHard < StuckHardConsecutive;
    }

    /// <summary>Romanian status for UI. Only hard errors should mention the hardware LED.</summary>
    public static string UiLedMessage(int? code, bool samplesOk)
    {
        if (code is null or 0)
            return samplesOk
                ? "EST=0 — date OK"
                : "EST=0";
        if (code == ResetStatus)
            return "EST=10000 (reset) — citit/șters cu EST?; LED ar trebui stins";
        if (code >= HardErrorMin && code <= HardErrorMax)
            return samplesOk
                ? $"EST={code} — LED Error pe aparat — date OK / power-cycle dacă persistă"
                : $"EST={code} — Power-cycle Spider8 — LED ERROR";
        // 10003: same hard band — message already covers «date OK / power-cycle».
        return $"EST={code}";
    }

    /// <summary>Obsolete name — prefer <see cref="IsHardError"/>; 10000 is NOT fatal.</summary>
    public static bool IsEstError(string? est) => IsHardError(est);

    private static string Trunc(string s, int n)
        => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";
}
