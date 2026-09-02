namespace Spider8DAQ.Core.Calibration;

/// <summary>
/// In-app DB15 / SR01 wiring guide for UPET sensors (U2B 6-wire, P15 DIN→DB15).
/// Content aligned with docs/UPET-AcqLab-Cablare-senzori-DB15.docx concepts.
/// </summary>
public static class Db15WiringGuide
{
    public const string Title = "Ghid cablare DB15 — UPET AcqLab";

    public static string FullText() =>
        Title + Environment.NewLine + Environment.NewLine +
        U2bSection() + Environment.NewLine + Environment.NewLine +
        P15Section() + Environment.NewLine + Environment.NewLine +
        NotesSection();

    public static string U2bSection() =>
        "U2B (celulă forță) — 6 fire → DB15 / SR01\r\n" +
        "─────────────────────────────────────────\r\n" +
        "  Culoare fir     →  Pin DB15\r\n" +
        "  Alb             →  8   (Exc+ / +Uexc)\r\n" +
        "  Roșu            →  15  (Exc− / −Uexc)\r\n" +
        "  Albastru        →  6   (Sig+ / +Us)\r\n" +
        "  Negru           →  5   (Sig− / −Us)\r\n" +
        "  Verde           →  13  (Sense+ / +Usense)\r\n" +
        "  Gri             →  12  (Sense− / −Usense)\r\n" +
        "\r\n" +
        "Bridge: Full (6-wire preferred). Capacitate tipică 2 kN / 5 kN (unitate N).\r\n" +
        "După cablare: Apply senzor U2B → Zero fără sarcină → Start → Record.";

    public static string P15Section() =>
        "P15 (presiune) — DIN → DB15 / SR01\r\n" +
        "──────────────────────────────────\r\n" +
        "  DIN pin 1  →  Exc+ (alimentare senzor)\r\n" +
        "  DIN pin 2  →  Sig  (ieșire semnal)\r\n" +
        "  DIN pin 3  →  Exc− / GND\r\n" +
        "\r\n" +
        "Pe conectorul DB15/SR01 mapați Exc± și Sig conform adapterului Spider8.\r\n" +
        "Unitate tipică: bar. Pentru presă UPET: afișaj Presiune→kg (A≈201,06 cm²).";

    public static string NotesSection() =>
        "Note\r\n" +
        "────\r\n" +
        "• Verificați polaritatea: dacă forța la compresie e negativă → Inversare polaritate, apoi Zero.\r\n" +
        "• Nu amestecați fire Sense cu Sig pe U2B (6-wire).\r\n" +
        "• Meniu: Ajutor → Ghid cablare DB15 (sau butonul din tab Calibrare).";
}
