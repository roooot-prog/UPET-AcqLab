using System.Text.RegularExpressions;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Specimens;

namespace Spider8DAQ.Core.Advisory;

/// <summary>
/// Rule-based lab advisor: maps Status / EST / Connect errors to Romanian recovery steps.
/// Scores are boosted from <see cref="AdvisorMemory"/> feedback and <see cref="AdviceContext"/>.
/// </summary>
public static class LabAdvisor
{
    private sealed record Rule(
        string Id,
        string Pattern,
        AdviceSeverity Sev,
        string Title,
        string Suggestion,
        string[] Steps,
        string? ActionId,
        string? ActionLabel);

    private static readonly Rule[] Rules =
    [
        new("connect-openport", @"OpenPort|PORT_USB|eșuat #|esuat #|Intfac|Connect Intfac",
            AdviceSeverity.Error,
            "USBHBM / Intfac nu deschide portul",
            "Dispozitivul USB e ocupat sau nedetectat. Treceți pe DEST sau eliberați USBHBM.",
            [
                "Închideți catman Easy (ține exclusiv USBHBM).",
                "Închideți alte instanțe UPET AcqLab.",
                "Scoateți USB 5 s, reintroduceți; power-cycle Spider8 dacă LED ERROR.",
                "Backend: HBM USB → Connect din nou (poate trece pe DEST SoftSetup)."
            ], "preflight", "Preflight"),

        new("connect-catman", @"catman|Easy rulează",
            AdviceSeverity.Warning,
            "catman Easy rulează în paralel",
            "Doar un client pe USBHBM. Închideți catman Easy înainte de Connect.",
            ["Închideți catman Easy", "Reconnect USB", "Connect în UPET"],
            "connect", "Connect"),

        new("connect-timeout", @"timeout|fără răspuns|Connect eșuat|Connect esuat|nu răspunde",
            AdviceSeverity.Error,
            "Connect fără răspuns de la Spider8",
            "Verificați cablul USB, driverul usbhbm și că Spider8 e alimentat.",
            [
                "Alimentare Spider8 ON, LED fără ERROR.",
                "Driver USBHBM (VID_10D1) în Device Manager.",
                "Închideți catman → Connect HBM USB.",
                "Alternativ: Serial pe COM real sau Simulator pentru demo."
            ], "connect", "Connect"),

        new("connect-generic", @"\bConnect\b.*(eșuat|esuat|fail)|conectare (eșuat|esuat)|nu (e|este) conectat|de ce.*(connect|conect)",
            AdviceSeverity.Info,
            "Conectare dispozitiv",
            "Alegeți backend (HBM USB / Serial / Simulator) apoi Connect.",
            [
                "HBM USB: catman închis, USBHBM prezent → Connect.",
                "Serial: selectați COM real (nu Simulator).",
                "Preflight F10 verifică USB / canale On."
            ], "connect", "Connect"),

        new("led-error", @"LED ERROR|Power-cycle|EST=100|hard EST|_deviceInError|EST\s*=\s*1|stare ERROR",
            AdviceSeverity.Error,
            "Spider8 în stare ERROR (EST)",
            "Citirea EST? șterge eroarea; dacă persistă → power-cycle.",
            [
                "Stop măsurare → Disconnect.",
                "Opriți alimentarea Spider8 ~10 s, reporniți.",
                "Connect → Start. Dacă LED rămâne roșu, verificați cablajul canalului."
            ], "preflight", "Preflight"),

        new("no-live-samples", @"fără eșantioane|flat|LiveValues|OMB|nu (primesc|vin) date|fără date|nu am date|zero live",
            AdviceSeverity.Warning,
            "Nu vin date live",
            "Connect OK nu înseamnă stream: trebuie Start + canale On + OMB.",
            [
                "Canalul senzorului: On + Rec bifate.",
                "Apply senzor pe canalul corect (P15=DcVoltage, U2B=Full).",
                "Stop → Start (F5) după Apply.",
                "Un singur canal On dacă maparea OMB e suspectă."
            ], "start", "Start live"),

        new("usb-hbm", @"USBHBM|usbhbm|VID_10D1|HBM USB|USB (lips|absent|missing)|fără USB",
            AdviceSeverity.Warning,
            "USB HBM / driver",
            "Spider8 trebuie văzut ca USBHBM (VID_10D1). catman Easy blochează portul.",
            [
                "Device Manager → Universal Serial Bus devices → USBHBM.",
                "Închideți catman Easy complet.",
                "Alt port USB / cablu scurt; evitați hub-uri nealimentate.",
                "Preflight F10 confirmă prezența USBHBM."
            ], "preflight", "Preflight"),

        new("serial-com", @"\bCOM\d+\b|Serial|port serial|RS-?232",
            AdviceSeverity.Info,
            "Backend Serial / COM",
            "Serial folosește un COM real alocat Spider8 — nu Simulator.",
            [
                "Device Manager → Ports (COM & LPT) — notați COM-ul.",
                "Backend: Serial → selectați COM → Connect.",
                "Dacă OpenPort eșuează pe Serial, încercați HBM USB."
            ], "connect", "Connect"),

        new("preflight", @"[Pp]reflight|F10|verificare (USB|lab)",
            AdviceSeverity.Info,
            "Preflight laborator",
            "Preflight F10: USBHBM, catman, canale On, tare/shunt pe dispozitiv.",
            [
                "Rulați Preflight (F10) înainte de măsurătoare.",
                "Corectați liniile FAIL (USB, catman, canale).",
                "Apoi Connect → Apply senzor → Zero → Start."
            ], "preflight", "Preflight"),

        new("shunt-no-omb", @"fără citire OMB|fara citire OMB",
            AdviceSeverity.Error,
            "Shunt fără citire OMB",
            ShuntCheck.SuggestNoOmb,
            [
                "Start (nu Stop) până se mișcă Citirea — Rec e alt buton.",
                "Catman închis; LIVE fără EST=10003.",
                "Spider32.dll: comutați HBM USB (DEST) — DLL-ul nu citește OMB după SH."
            ], "start", "Start live"),

        new("shunt-dll", @"Spider32\.dll nu interoghează OMB|S8_MeasOneVal, nu ASCII",
            AdviceSeverity.Error,
            "Shunt pe Spider32.dll fără OMB",
            ShuntCheck.SuggestNoOmbDll,
            [
                "Backend HBM USB (DEST SoftSetup) pentru ASS + OMB?0.",
                "Sau Start până se mișcă Citirea, apoi Shunt (Citire live, nu Rec)."
            ], "start", "Start live"),

        new("shunt-est", @"bannerul roșu|bannerul rosu|repetă Shunt când bannerul|repete Shunt cand bannerul|EST=10003",
            AdviceSeverity.Error,
            "Shunt cu LED Error / EST=10003",
            ShuntCheck.SuggestEstError,
            [
                "EST=10003 = eroare hard LED (10001–10020), nu ACK 10000.",
                "Power-cycle ~10 s, USB, cablaj; fără catman.",
                "Repetați Shunt după ce bannerul roșu a dispărut."
            ], "connect", "Connect"),

        new("shunt-step-low", @"Treapta e sub așteptat|~25% jos|25% jos",
            AdviceSeverity.Error,
            "Shunt FAIL — treaptă prea joasă",
            ShuntCheck.SuggestStepLow,
            [
                "Cablare 3 fire, pin 120 (Rsh 29.9 kΩ), nu pin 350.",
                "Arată cablare; dummy pe martor; fără sarcină.",
                "Nu Aplică Scale din shunt pe FAIL."
            ], "db15", "Arată cablare"),

        new("shunt-step-high", @"Treapta e peste așteptat|peste așteptat",
            AdviceSeverity.Error,
            "Shunt FAIL — treaptă prea înaltă",
            ShuntCheck.SuggestStepHigh,
            [
                "Verificați GF, R Ω și tipul de punte (Half+dummy vs Simplu).",
                "Rsh pin 120 (29.9 kΩ) vs pin 350 (≈87 kΩ).",
                "Nu Aplică Scale din shunt pe FAIL."
            ], "db15", "Arată cablare"),

        new("shunt-step", @"pin 120 vs 350|Aplică Scale din shunt pe FAIL|Aplica Scale din shunt pe FAIL",
            AdviceSeverity.Error,
            "Shunt FAIL treaptă",
            ShuntCheck.SuggestStepFailBody,
            [
                "Verificați cablarea 3 fire / pin 120 vs 350 (Rsh 29.9 = pin 120).",
                "Arată cablare; dummy pe martor; fără sarcină.",
                "Nu Aplică Scale din shunt pe FAIL."
            ], "db15", "Arată cablare"),

        new("scale-timbru-absurd", @"Scale invalid — Aplică Timbru|Scale invalid pe canal Timbru|Scale=91885",
            AdviceSeverity.Error,
            "Scale Timbru invalid",
            ShuntCheck.SuggestScaleAbsurd,
            [
                "Aplică Timbru — Scale = 4000/GF (Half+dummy T° ≈1887 la GF=2.12).",
                "Autorange scrie domeniul în Capacity, nu în Scale.",
                "Nu folosiți Aplică Scale din shunt cât Scale e absurd."
            ], "first-measure", "Asistent Timbru"),

        new("shunt-pass", @"Treaptă OK față de Timbru",
            AdviceSeverity.Info,
            "Shunt PASS — Zero CH",
            ShuntCheck.SuggestPass,
            [
                "Zero CH pe liber, apoi Rec."
            ], "zero", "Zero"),

        new("shunt-half-dummy", @"shunt intern NU e în punte|PASS \(Half\+dummy\)|SKIP verificare quarter|pin 120 nefolosit|Nu e FAIL de cablaj 1893",
            AdviceSeverity.Info,
            "Shunt Half+dummy — ASS nu e în punte",
            ShuntCheck.SuggestHalfDummySkip,
            [
                "Half + activ + pasiv T°: două timbre externe; nu legați pin 120/350/700.",
                "Șuntul intern Spider8 e pe completarea nefolosită — residual mic ≠ cablu rupt.",
                "Verificare reală: apăsați pe timbrul activ (Citirea trebuie să se miște).",
                "Nu e FAIL de cablaj 1893; nu Aplică Scale din shunt. Quarter (un timbru + pin 120) rămâne pe Rsh ±0.5–5%."
            ], "first-measure", "Asistent Timbru"),

        new("shunt-rsh", @"lipsește Rsh|lipseste Rsh|coloana Rsh",
            AdviceSeverity.Warning,
            "Lipsește Rsh",
            ShuntCheck.SuggestMissingRsh,
            [
                "Connect pe Spider8-30 ca să se umple Rsh.",
                "Sau completați coloana Rsh kΩ."
            ], "connect", "Connect"),

        new("shunt-timbru-missing", @"lipsește Timbru|lipseste Timbru",
            AdviceSeverity.Warning,
            "Lipsește Timbru",
            ShuntCheck.SuggestMissingTimbru,
            [
                "Asistent Timbru → Aplică pe canal (GF, R, punte)."
            ], "first-measure", "Asistent Timbru"),

        new("shunt", @"[Ss]hunt",
            AdviceSeverity.Info,
            "Shunt / verificare punte",
            "Shunt verifică răspunsul punții — nu schimbă polaritatea forței.",
            [
                "Pentru semn invers: Inversare polaritate (Scale × −1), apoi Zero.",
                "Asistent Timbru / Aplică pe canal (GF, R Ω, punte); Rsh kΩ se completează la Connect (intern Spider8-30).",
                "Quarter (un timbru + pin 120): treaptă din Timbru + Rsh, PASS la Diferență permisă % (0.5–5%).",
                "Half + activ + dummy T° (2 timbre externe): ASS nu e în punte — PASS (Half+dummy) / SKIP 1893, nu FAIL cablaj."
            ], "shunt", "Shunt CH"),

        new("polarity", @"polaritate|Scale ×|Scale x −1|compresie|semn invers|inversare",
            AdviceSeverity.Info,
            "Polaritate / semn forță",
            "Scale × −1 inversează semnul; apoi Zero pe liber.",
            ["Click dreapta canal → Inversare polaritate", "Zero CH fără sarcină", "Ctrl+Z = Undo dacă e nevoie"],
            "invert", "Inversare"),

        new("defect-zero", @"Zero uitat|zero uitat",
            AdviceSeverity.Warning,
            "Zero uitat (detectare)",
            "Offset mare pe liber — faceți Zero înainte de măsurare/Record.",
            ["Scoateți sarcina", "Zero CH / Zero all (F9)", "Verificați ~0", "Apoi încărcați / Rec"],
            null, null),

        new("defect-sat", @"Saturare",
            AdviceSeverity.Error,
            "Saturare semnal",
            "Semnalul atinge Capacity/plafon. Reduceți sarcina sau corectați Scale.",
            ["Reduceți sarcina", "Verificați Capacity senzor", "Apply senzor / Scale", "Tab Defecte → Saturare"],
            null, null),

        new("defect-cable", @"Cablu / semnal mort|Canal mort|cablu desprins|Semnal plat",
            AdviceSeverity.Error,
            "Semnal plat / cablu",
            "Canal On dar semnal plat. Verificați DB15 și maparea.",
            ["Verificați DB15 / cablu", "Un canal On de test", "Apply senzor + Start", "Tab Defecte → Semnal plat"],
            "preflight", "Preflight"),

        new("defect-shunt", @"Treaptă tip șunt|tip șunt\?",
            AdviceSeverity.Warning,
            "Șunt vs sarcină reală",
            "Salt brusc poate fi Shunt check, nu încărcare mecanică.",
            ["Nu confundați Shunt cu sarcină", "Verificați R_shunt", "Zero după Shunt", "Tab Defecte"],
            "shunt", "Shunt CH"),

        new("defect-pressure-zero", @"Presiune fără Zero|Presiune cu offset",
            AdviceSeverity.Warning,
            "Presiune fără Zero",
            "Traductorul are offset — Zero la atmosferă.",
            ["Deschideți la atmosferă", "Zero pe canalul de presiune", "Verificați ~0 bar"],
            null, null),

        new("defect-wrong-ch", @"Canal greșit",
            AdviceSeverity.Warning,
            "Canal greșit pentru Rec/senzor",
            "Semnalul e pe alt CH decât cel pe care înregistrați.",
            ["Găsiți canalul cu semnal pe grafic", "Apply senzor pe acel CH", "Rec On pe canalul util"],
            null, null),

        new("defect-drift", @"Drift\?",
            AdviceSeverity.Info,
            "Drift / derapare",
            "Semnalul se mișcă lent — termic sau Zero vechi.",
            ["Așteptați 1–2 min", "Zero din nou pe liber", "Verificați cablul"],
            null, null),

        new("p15-dcvoltage", @"P15|DcVoltage|presiune.*bar|0…10 V|0\.\.10|0\.\.\.10",
            AdviceSeverity.Info,
            "P15 — tensiune DC, nu punte Full",
            "P15RVA are ieșire 0…10 V, alimentare externă 18…30 V, Bridge=DcVoltage.",
            [
                "Apply P15 pe canal → Bridge DcVoltage, Scale≈20 bar/V, Exc=0.",
                "Alimentare DIN: 1=+UB, 2=0V, 3=+Uout → Spider8 pin 7/4 sau SR01.",
                "Stop→Start după Apply."
            ], "db15", "Ghid DB15"),

        new("u2b-force", @"U2B|5kN|celul[aă] de for|forță.*kN|force.*cell",
            AdviceSeverity.Info,
            "U2B — Full bridge mV/V",
            "Unitate N (1114 N = 1,114 kN). Capacity 5 kN nu plafonează softul.",
            [
                "Cablare DB15: Alb→8, Roșu→15, Albastru→6, Negru→5, Verde→13, Gri→12.",
                "Apply U2B 5kN → Full, Scale 2500 → Zero pe liber.",
                "Meniu Ghid cablare DB15 pentru detalii."
            ], "db15", "Ghid DB15"),

        new("timbru", @"[Tt]imbru|µm/m|um/m|Gauge Factor|Quarter|Half.*punte|tensometr|strain",
            AdviceSeverity.Info,
            "Timbru / punte tensometrică",
            "Folosiți panoul Timbru din Bibliotecă senzori sau categoria Mărci tensometrice.",
            [
                "Bibliotecă → Mărci tensometrice sau panou Timbru / punte.",
                "Setați GF, tip punte, R → Calculează Scale → Aplică.",
                "Zero după Apply."
            ], "first-measure", "Asistent 5 pași"),

        new("record-empty", @"înregistrare goală|0 eșantioane|citire eșuată|Record.*(eșuat|esuat|fail)|CSV.*(eșuat|blocat|lips)|cum.*(record|csv|înregistr)",
            AdviceSeverity.Warning,
            "Înregistrare / citire CSV",
            "CSV poate exista pe disc dar jurnalul nu-l citește dacă e blocat la scriere.",
            [
                "Stop măsurare (închide Record) înainte de deschiderea în Jurnal.",
                "Deschideți folder recordings din meniu Export.",
                "Refresh listă măsurători."
            ], "recordings", "Deschide recordings"),

        new("capacity-over", @"suprasarcin|%FS|peste.*Capacity|6282|satur|over.?range|peste capacitate",
            AdviceSeverity.Warning,
            "Valori peste Capacity senzor",
            "Softul afișează forța măsurată; Capacity e nominală, nu limită software.",
            [
                "Comparați cu Matest la același moment (nu Max global UPET).",
                "Opriți Record la stop presa / MARK.",
                "Verificați montajul dacă Matest=1 kN și U2B≈6 kN simultan."
            ], null, null),

        new("scale-suspect", @"Scale suspect|Capacity×|Capacityx|scale suspect",
            AdviceSeverity.Warning,
            "Scale suspect",
            "|F| depășește Capacity×1.2 (sau spike după Zero). Softul NU plafonează valorile.",
            [
                "Verificați Scale (Apply senzor U2B 5kN → 2500) și Zero pe liber.",
                "Dacă sarcina e intenționat mare: bifați «Sarcină mare OK» în Metrologie.",
                "Comparați cu etalon / Matest pe aceeași treaptă."
            ], null, null),

        new("half-bridge-thermal", @"compensare temperatur|Half bridge|½ punte|1/2 punte|half bridge|dummy T|Activ\+dummy|timbru pasiv",
            AdviceSeverity.Info,
            "Half bridge — compensare temperatură",
            "Pentru activ pe piesă + timbru pasiv (compensare T°) pe placă: Timbru → Half → Config „Activ + timbru pasiv (compensare T°)”, același canal.",
            [
                "Bibliotecă → Timbru / Asistent: Tip=Half, Config=Activ + timbru pasiv (compensare T°), GF de pe pachet.",
                "Calculează Scale → Aplică → Zero fără sarcină.",
                "Timbru pasiv pe material nesolicitat, aproape de piesă (aceeași T°).",
                "Nu legați pin 120/350/700. Shunt intern nu e în punte — residual mic e OK, nu FAIL 1893. Verificați apăsând pe activ."
            ], "first-measure", "Asistent 5 pași"),

        new("autorange-overflow", @"Overflow — oprește|CHn Overflow|CH\d+ Overflow",
            AdviceSeverity.Error,
            "Overflow domeniu tensometrie",
            "În Rec domeniul rămâne blocat (nu e AGC). La saturație: Overflow, nu schimbă treapta din mers.",
            [
                "Opriți Record.",
                "Măriți domeniul sau lăsați Autorange să aleagă înainte de Rec.",
                "Zero (F9) fără sarcină, apoi reia."
            ], null, null),

        new("excitation-mismatch", @"Excitație suspect|Exc V|Excitație|excitation",
            AdviceSeverity.Warning,
            "Excitație / gain la Connect",
            "Exc V diferit de așteptarea senzorului poate înclina Scale.",
            [
                "Verificați Exc V pe canal (tipic 2.5 V Spider8 SoftSetup).",
                "P15 / DcVoltage: Exc=0 (nu forțați 2.5 V).",
                "Re-Apply senzor din bibliotecă după corectare."
            ], "preflight", "Preflight"),

        new("version-old", @"2\.9\.|Program Files|versiune veche",
            AdviceSeverity.Info,
            "Versiune veche posibilă",
            "Funcțiile noi (Timbru, Matest, Advisor, Metrologie) sunt în publish-v2 3.x.",
            ["Rulați publish-v2\\UPETAcqLab.exe", "Verificați Versiune în Despre / status"],
            null, null)
    ];

    public static IReadOnlyList<AdviceItem> Advise(
        string? statusOrQuestion,
        AdvisorMemory? memory = null,
        int max = 3) =>
        Advise(statusOrQuestion, memory, AdviceContext.Empty, max);

    public static IReadOnlyList<AdviceItem> Advise(
        string? statusOrQuestion,
        AdvisorMemory? memory,
        AdviceContext? context,
        int max = 3)
    {
        var text = statusOrQuestion ?? "";
        var ctx = context ?? AdviceContext.Empty;
        var hits = new List<AdviceItem>();
        var asked = LooksLikeQuestion(text);
        var strain = ExperimentTypes.IsStrainGauges(ctx.ExperimentType);
        var contour = ExperimentTypes.IsCylinderContour(ctx.ExperimentType);

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var r in Rules)
            {
                if (memory?.IsRejected(r.Id) == true) continue;
                if (strain && IsContourOnlyRule(r.Id)) continue;
                if (contour && r.Id is "timbru" or "half-bridge-thermal" && !ContainsAny(text.ToLowerInvariant(), "timbru", "tensometr", "gf", "µm", "um/m"))
                    continue;
                if (!Regex.IsMatch(text, r.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;
                hits.Add(ToItem(r, ScoreBase(r, memory) + ContextBoost(r.Id, text, ctx)));
            }
        }

        AddSessionAnalysis(hits, text, ctx, memory, asked);

        if (hits.Count == 0 && asked)
            hits.Add(BuildGenericHelp(text, ctx));

        IEnumerable<AdviceItem> ranked = hits
            .GroupBy(h => h.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First());

        if (strain)
            ranked = ranked.Where(h => !MentionsContour(h));

        ranked = ranked.Where(h => memory?.IsRejected(h.Id) != true);

        return ranked
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Severity)
            .Take(Math.Clamp(max, 2, 4))
            .ToList();
    }

    /// <summary>
    /// Blocking next-step (one) + signal quality + type-specific + same-type peers.
    /// Layout of live plots is never mentioned.
    /// </summary>
    private static void AddSessionAnalysis(
        List<AdviceItem> hits, string text, AdviceContext ctx, AdvisorMemory? memory, bool asked)
    {
        var lower = text.ToLowerInvariant();
        var strain = ExperimentTypes.IsStrainGauges(ctx.ExperimentType);
        var contour = ExperimentTypes.IsCylinderContour(ctx.ExperimentType);
        var mem = memory;

        // --- one blocking next step ---
        if (ctx.ExportStatus == AdvisorExportStatus.Failed)
        {
            var err = string.IsNullOrWhiteSpace(ctx.LastExportError)
                ? "Verificați calea/fișierul și reluați Export."
                : ctx.LastExportError!;
            EnsureHint(hits, "ctx-export-failed", AdviceSeverity.Error,
                "Export eșuat",
                err,
                ["Reluați Export după ce fișierul nu e deschis în Excel.", "CSV-ul înregistrării rămâne intact."],
                "recordings", "Recordings", 2.6 + (mem?.Boost("ctx-export-failed") ?? 0));
        }
        else if (ctx.ExportStatus == AdvisorExportStatus.InProgress)
        {
            EnsureHint(hits, "ctx-export-busy", AdviceSeverity.Info,
                "Export în curs",
                "Exportul rulează în fundal — puteți continua. Graficul live arată max 8000 puncte; CSV-ul salvează tot.",
                ["Așteptați mesajul de gata.", "Nu e nevoie să opriți măsurarea."],
                null, null, 2.5);
        }
        else if (!ctx.IsConnected && !hits.Any(h => h.Id.StartsWith("connect-", StringComparison.OrdinalIgnoreCase))
                 && (asked || LooksLikeProblem(lower) || string.IsNullOrWhiteSpace(text)
                     || ContainsAny(lower, "connect", "conect", "usb", "date", "start")))
        {
            EnsureHint(hits, "ctx-not-connected", AdviceSeverity.Warning,
                "Pasul următor: Connect",
                "Fără Connect nu există stream, Zero sau Record pe aparat.",
                ["Backend HBM USB / Serial / Simulator", "Închideți catman Easy", "Apăsați Connect"],
                "connect", "Connect", 2.4 + (mem?.Boost("ctx-not-connected") ?? 0));
        }
        else if (ctx.IsConnected && ctx.ChannelsOn <= 0)
        {
            EnsureHint(hits, "ctx-no-channels", AdviceSeverity.Warning,
                "Pasul următor: canal On",
                "Bifați On (și Rec) pe canalul senzorului înainte de Start/Record.",
                ["Tabel canale → On pe CH util", "Apoi Start (F5)"],
                "preflight", "Preflight", 2.3 + (mem?.Boost("ctx-no-channels") ?? 0));
        }
        else if (ctx.IsConnected && !ctx.HasAppliedSensor && !contour
                 && string.IsNullOrWhiteSpace(ctx.SelectedSensor))
        {
            var tip = strain
                ? "Aplicați marca (GF → Scale) pe canal, apoi Zero pe liber."
                : "Aplicați senzorul din bibliotecă pe canalul On.";
            EnsureHint(hits, "ctx-no-sensor", AdviceSeverity.Warning,
                "Pasul următor: aplicați senzorul",
                tip,
                strain
                    ? ["Bibliotecă → Mărci tensometrice / Timbru", "GF, Calculează Scale, Aplică", "Zero (F9) fără sarcină"]
                    : ["Selectați senzorul", "Aplică pe canalul On", "Zero (F9) fără sarcină"],
                strain ? "first-measure" : "new-experiment",
                strain ? "Asistent 5 pași" : "Nou exp.",
                2.2 + (mem?.Boost("ctx-no-sensor") ?? 0));
        }
        else if (ctx.IsConnected && ctx.IsStreaming && !ctx.IsZeroed)
        {
            var zeroTip = strain
                ? "ε nu e zerouată — Zero (F9) pe piesă liberă, apoi încărcați."
                : "Zero (F9) fără sarcină înainte de Record.";
            EnsureHint(hits, "ctx-not-zeroed", AdviceSeverity.Warning,
                "Pasul următor: Zero",
                zeroTip,
                ["Scoateți sarcina / presa pe liber", "Zero (F9)", "Verificați ~0, apoi Record"],
                "zero", "Zero", 2.15 + (mem?.Boost("ctx-not-zeroed") ?? 0));
        }
        else if (ctx.IsConnected && !ctx.IsStreaming &&
                 (asked || ContainsAny(lower, "date", "live", "eșantion", "esantion", "start", "omb", "flat")
                  || hits.Any(h => h.Id == "no-live-samples")))
        {
            EnsureHint(hits, "ctx-not-streaming", AdviceSeverity.Warning,
                "Pasul următor: Start",
                "Aparatul e conectat — Start (F5) după Apply senzor. Record e separat.",
                ["Canale On + Rec", "Start live (F5)", "Apoi Zero, apoi Record"],
                "start", "Start live", 2.1 + (mem?.Boost("ctx-not-streaming") ?? 0));
        }
        else if (ctx.IsRecording)
        {
            EnsureHint(hits, "ctx-recording", AdviceSeverity.Info,
                "Înregistrare în curs",
                "Record rulează. Graficul live e limitat la 8000 puncte; CSV-ul va conține toată durata.",
                ["Nu opriți fără motiv.", "După Stop: Export poate rula în fundal."],
                null, null, 1.55);
        }

        AddSignalHints(hits, ctx, mem);
        if (strain) AddStrainHints(hits, ctx, mem, asked);
        else if (contour) AddContourHints(hits, ctx, mem, asked);
        else AddGenericDaqHints(hits, ctx, mem, asked);

        if (ExperimentTypes.IsCompression(ctx.ExperimentType) && !strain)
            AddSpecimenHints(hits, ctx, mem, text, asked);

        AddPeerHint(hits, ctx, mem, asked);

        var sensor = ctx.SelectedSensor ?? "";
        if (sensor.Contains("P15", StringComparison.OrdinalIgnoreCase) &&
            !hits.Any(h => h.Id == "p15-dcvoltage") &&
            (asked || ContainsAny(lower, "p15", "presiune", "bar", "dc")))
        {
            var r = Rules.First(x => x.Id == "p15-dcvoltage");
            hits.Add(ToItem(r, ScoreBase(r, memory) + 0.8));
        }

        if (!strain &&
            (sensor.Contains("U2B", StringComparison.OrdinalIgnoreCase) ||
             sensor.Contains("5kN", StringComparison.OrdinalIgnoreCase)) &&
            !hits.Any(h => h.Id == "u2b-force") &&
            (asked || LooksLikeProblem(lower) || ContainsAny(lower, "u2b", "forță", "forta", "kn")))
        {
            var r = Rules.First(x => x.Id == "u2b-force");
            hits.Add(ToItem(r, ScoreBase(r, memory) + 0.8));
        }
    }

    private static void AddSignalHints(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory)
    {
        if (ctx.HasNan)
        {
            EnsureHint(hits, "ctx-nan", AdviceSeverity.Error,
                "Valori NaN pe canal",
                "Un canal On dă NaN — verificați cablul / Apply senzor, apoi Stop→Start.",
                ["Canal On + Rec", "Apply senzor", "Stop → Start"],
                "preflight", "Preflight", 2.05 + (memory?.Boost("ctx-nan") ?? 0));
        }

        if (ctx.HasClipping || ctx.HasOverload)
        {
            var ch = ctx.ClipChannelHint;
            var msg = string.IsNullOrWhiteSpace(ch)
                ? "Semnal la Capacity — reduceți sarcina sau corectați Scale."
                : $"Saturare pe {ch} — reduceți sarcina sau corectați Scale.";
            EnsureHint(hits, "ctx-clip", AdviceSeverity.Error,
                "Saturare / plafon",
                msg,
                ["Reduceți sarcina", "Verificați Capacity / Scale", "Nu continuați Record în plafon"],
                null, null, 2.0 + (memory?.Boost("ctx-clip") ?? 0));
        }

        if (ctx.HasFlatChannel)
        {
            var ch = ctx.FlatChannelHint;
            var msg = string.IsNullOrWhiteSpace(ch)
                ? "Un canal e plat — verificați cablul / senzorul (nu ascundeți graficul)."
                : $"Semnal plat pe {ch} — verificați cablul / senzorul.";
            EnsureHint(hits, "ctx-flat", AdviceSeverity.Warning,
                "Canal plat",
                msg,
                ["Verificați DB15", "Un canal On de test", "Apply senzor + Start"],
                "preflight", "Preflight", 1.85 + (memory?.Boost("ctx-flat") ?? 0));
        }

        foreach (var f in ctx.Findings.Take(2))
        {
            if (string.IsNullOrWhiteSpace(f.Message)) continue;
            var id = "finding-" + (string.IsNullOrWhiteSpace(f.Kind) ? "x" : f.Kind);
            if (hits.Any(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            EnsureHint(hits, id, AdviceSeverity.Warning,
                string.IsNullOrWhiteSpace(f.ChannelHint) ? "Semnal" : f.ChannelHint,
                f.Message,
                ["Corectați înainte de Record / Export"],
                null, null, 1.7);
        }
    }

    private static void AddStrainHints(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory, bool asked)
    {
        if (!ctx.HasGaugeFactorScale && (ctx.IsConnected || asked || ctx.HasOfflineSession))
        {
            EnsureHint(hits, "ctx-strain-gf", AdviceSeverity.Warning,
                "Lipsește GF / Scale",
                "Pentru tensometrie setați Gauge Factor, calculați Scale (µm/m), aplicați, apoi Zero pe liber.",
                ["Panou Timbru: GF + tip punte", "Calculează Scale → Aplică", "Zero (F9), apoi încărcare"],
                "first-measure", "Asistent 5 pași", 1.65 + (memory?.Boost("ctx-strain-gf") ?? 0));
        }

        if (ctx.HasOfflineSession && ctx.OfflineSampleCount > 20 && !ctx.HasUnloadMarkers)
        {
            EnsureHint(hits, "ctx-strain-unload", AdviceSeverity.Info,
                "Descărcare (revenire elastică)",
                "Marcați începutul și sfârșitul descărcării în Analiză — ε_max e pe canal, nu pe foaia Contur.",
                ["Cursor: start descărcare", "Cursor: sfârșit descărcare", "Citire revenire elastică"],
                null, null, 1.15 + (memory?.Boost("ctx-strain-unload") ?? 0));
        }

        if (ctx.PoissonNu is double nu && double.IsFinite(nu) && asked)
        {
            EnsureHint(hits, "ctx-strain-poisson", AdviceSeverity.Info,
                "Poisson ν",
                $"ν aparent ≈ {nu.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} — verificați canalele ε_l / ε_t.",
                ["ε_l și ε_t pe canale diferite", "Fereastră fără platou mort"],
                null, null, 1.05);
        }

        if (asked && !hits.Any(h => h.Id is "timbru" or "ctx-strain-gf"))
        {
            EnsureHint(hits, "ctx-strain-eps", AdviceSeverity.Info,
                "Tensometrie — ε",
                "Lucrați în µm/m: GF → Scale → Zero → Record → descărcare. Raportul nu include foaia Contur.",
                ["Zero pe liber", "Record toată încercarea", "Marcați descărcarea"],
                "first-measure", "Asistent 5 pași", 0.95);
        }
    }

    private static void AddContourHints(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory, bool asked)
    {
        if (ctx.SampleDiameterMm <= 0)
        {
            EnsureHint(hits, "ctx-contour-diameter", AdviceSeverity.Warning,
                "Lipsește Ø",
                "Pentru contur cilindru setați diametrul Ø la Start experiment (R₀ = Ø/2).",
                ["Nou experiment → Compresiune cilindru – contur", "Diametru [mm] obligatoriu", "Apoi mapare S1…Sn"],
                "new-experiment", "Nou exp.", 1.8 + (memory?.Boost("ctx-contour-diameter") ?? 0));
        }

        if (ctx.SampleLengthMm <= 0)
        {
            EnsureHint(hits, "ctx-contour-l0", AdviceSeverity.Warning,
                "Lipsește L0",
                "Completați lungimea L0 a probei — harta de bombare folosește L0 pe înălțime.",
                ["Start experiment → Lungime [mm]", "Ø și L0 înainte de Record"],
                "new-experiment", "Nou exp.", 1.55 + (memory?.Boost("ctx-contour-l0") ?? 0));
        }

        if (!ctx.ContourMapped || !ctx.ContourAnglesSet)
        {
            EnsureHint(hits, "ctx-contour-map", AdviceSeverity.Warning,
                "Mapare S1…Sn / unghiuri",
                "Verificați canalele S1…Sn, unghiurile și cursa — altfel ovalitatea la Fmax e greșită.",
                ["Start experiment → expander Contur", "Unghiuri 0°/90°… sau 0°/45°…", "Cursă + forță pe canale Rec"],
                "new-experiment", "Nou exp.", 1.5 + (memory?.Boost("ctx-contour-map") ?? 0));
        }

        if (!string.IsNullOrWhiteSpace(ctx.FlatRadialWarning))
        {
            EnsureHint(hits, "ctx-contour-flat-radial", AdviceSeverity.Warning,
                "Senzor radial plat",
                ctx.FlatRadialWarning!,
                ["Verificați cablul S# plat", "Nu comparați u_max până e corect"],
                null, null, 1.6);
        }

        if (ctx.HasOfflineSession && ctx.OvalityMm is double ov && double.IsFinite(ov))
        {
            var u = ctx.UMaxMm is double um && double.IsFinite(um)
                ? $" u_max={um.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} mm."
                : "";
            var b = ctx.BarrelingIndex is double bi && double.IsFinite(bi)
                ? $" bombare={bi.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}."
                : "";
            EnsureHint(hits, "ctx-contour-result", AdviceSeverity.Info,
                "Contur la Fmax",
                $"Ovalitate {ov.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} mm.{u}{b} Schema e în raportul Contur (plan + hartă bombare).",
                ["Platouri scurte înainte de Fmax", "Stop Record după descărcare"],
                null, null, 1.12);
        }
        else if (asked || ctx.IsStreaming)
        {
            EnsureHint(hits, "ctx-contour-fmax", AdviceSeverity.Info,
                "Platouri și Fmax",
                "Țineți platouri scurte; conturul se ia la Fmax. Nu schimbați layout-ul graficelor live.",
                ["Ø, L0, unghiuri S# setate", "Record până trece Fmax", "Export în fundal după Stop"],
                null, null, 0.92);
        }
    }

    private static void AddSpecimenHints(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory, string text, bool asked)
    {
        if (!ExperimentTypes.IsCompression(ctx.ExperimentType)) return;
        if (ExperimentTypes.IsStrainGauges(ctx.ExperimentType)) return;

        var pack = ctx.SpecimenFormulaPack ?? "";
        if (!string.IsNullOrWhiteSpace(pack))
        {
            var name = string.IsNullOrWhiteSpace(ctx.SpecimenName) ? "epruvetă" : ctx.SpecimenName!;
            var cls = string.IsNullOrWhiteSpace(ctx.SpecimenClass) ? "" : " (" + ctx.SpecimenClass + ")";
            var packText = FormulaPacks.AdvisorTextRo(pack);
            if (string.IsNullOrWhiteSpace(packText)) return;
            EnsureHint(hits, "ctx-specimen-pack", AdviceSeverity.Info,
                "Epruvetă · pachet formule",
                name + cls + ". " + packText,
                ["Valorile E, ν, L0, Ø sunt indicative — confirmați pe probă",
                 "Conturul geometric/culorile nu depind de material"],
                null, null, 1.08 + (memory?.Boost("ctx-specimen-pack") ?? 0));
            return;
        }

        // Missing card is valid. Never warn. Mention the library only if the operator asked about it.
        var lower = (text ?? "").ToLowerInvariant();
        if (asked && ContainsAny(lower, "epruvet", "specimen", "sare", "granit", "s355", "bibliotec"))
        {
            EnsureHint(hits, "ctx-specimen-optional", AdviceSeverity.Info,
                "Bibliotecă epruvete (opțional)",
                "Epruveta e opțională. La Start experiment puteți lăsa gol sau căuta sare, granit, S355. Nu blochează pornirea.",
                ["Tip experiment = compresiune", "Căutare Epruvetă (opțional)", "Cardul completează L0, Ø, E, ν și pachetul"],
                "new-experiment", "Nou exp.", 0.72);
        }
    }

    private static void AddGenericDaqHints(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory, bool asked)
    {
        if (!ctx.IsConnected) return;
        if (!asked && ctx.IsStreaming && ctx.IsZeroed && ctx.HasAppliedSensor) return;
        EnsureHint(hits, "ctx-generic-daq", AdviceSeverity.Info,
            "Achiziție",
            "Connect → Apply senzor → Start → Zero → Record. Exportul poate rula în fundal; CSV-ul e complet.",
            ["Preflight F10 dacă USB e incert", "Nu e nevoie să ascundeți graficele"],
            "preflight", "Preflight", 0.7 + (memory?.Boost("ctx-generic-daq") ?? 0));
    }

    private static void AddPeerHint(List<AdviceItem> hits, AdviceContext ctx, AdvisorMemory? memory, bool asked)
    {
        if (ctx.Current is null) return;
        if (string.IsNullOrWhiteSpace(ctx.Current.ExperimentType)) return;
        if (ctx.IsRecording) return;
        if (!ctx.HasOfflineSession && ctx.Current.SampleCount < 8) return;

        var item = ExperimentPeerCatalog.Compare(ctx.Current, ctx.Peers, ctx.PeerScanComplete, memory, asked);
        if (item is null) return;
        if (hits.Any(h => h.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))) return;
        hits.Add(item);
    }

    private static bool IsContourOnlyRule(string id) =>
        id.StartsWith("ctx-contour", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("peer-contour", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsContour(AdviceItem h)
    {
        if (IsContourOnlyRule(h.Id)) return true;
        var blob = h.Title + " " + h.Suggestion + " " + string.Join(' ', h.Steps);
        return blob.Contains("foaia Contur", StringComparison.OrdinalIgnoreCase)
               || blob.Contains("foaia «Contur»", StringComparison.OrdinalIgnoreCase)
               || blob.Contains("Contur cilindru", StringComparison.OrdinalIgnoreCase)
               || blob.Contains("deschideți foaia", StringComparison.OrdinalIgnoreCase)
                  && blob.Contains("Contur", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureHint(
        List<AdviceItem> hits, string id, AdviceSeverity sev,
        string title, string suggestion, string[] steps,
        string? actionId, string? actionLabel, double score)
    {
        if (hits.Any(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) return;
        hits.Add(new AdviceItem
        {
            Id = id,
            Title = title,
            Suggestion = suggestion,
            Severity = sev,
            Steps = steps,
            Score = score,
            ActionId = actionId,
            ActionLabel = actionLabel
        });
    }

    private static AdviceItem BuildGenericHelp(string text, AdviceContext ctx)
    {
        var steps = new List<string>
        {
            "Preflight F10 — verifică USB / canale On.",
            "Ghid cablare DB15 — meniu Export / Bibliotecă.",
            "Asistent prima măsurătoare — 5 pași.",
            "Jurnal — erori recente."
        };
        if (!ctx.IsConnected) steps.Insert(0, "Apăsați Connect (HBM USB după ce catman e închis).");
        else if (!ctx.IsStreaming) steps.Insert(0, "Apăsați Start (F5) după Apply senzor.");

        var richer = EnrichQuestionAnswer(text, ctx);
        return new AdviceItem
        {
            Id = "generic-help",
            Title = "Asistent lab",
            Suggestion = richer ??
                "Descrieți simptomul (Connect, fără date, tensometrie, contur, Record). Preflight F10.",
            Severity = AdviceSeverity.Info,
            Steps = steps,
            Score = 0.55,
            ActionId = "preflight",
            ActionLabel = "Preflight"
        };
    }

    private static string? EnrichQuestionAnswer(string text, AdviceContext ctx)
    {
        var t = text.ToLowerInvariant();
        var strain = ExperimentTypes.IsStrainGauges(ctx.ExperimentType);
        if (strain && ContainsAny(t, "contur", "bombare", "ovalitate"))
            return "La tensometrie raportul nu include foaia Contur. Lucrați ε, Zero, GF/Scale și descărcare.";
        if (ContainsAny(t, "p15", "presiune", "bar"))
            return "Pentru P15: Bridge=DcVoltage, alimentare 18…30 V pe senzor, Apply pe canal, Stop→Start. Nu e Full bridge.";
        if (ContainsAny(t, "u2b", "forță", "forta", "celul") && !strain)
            return "Pentru U2B: Full bridge, cablare DB15 (vezi Ghid), Scale 2500, Zero pe liber, unitate N.";
        if (ContainsAny(t, "fără citire omb", "fara citire omb", "spider32.dll nu interoghează"))
            return t.Contains("spider32", StringComparison.OrdinalIgnoreCase)
                ? ShuntCheck.SuggestNoOmbDll
                : ShuntCheck.SuggestNoOmb;
        if (ContainsAny(t, "connect", "usb", "openport", "catman"))
            return "Connect: închideți catman Easy, verificați USBHBM, backend HBM USB → Connect. Preflight F10 ajută.";
        if (ContainsAny(t, "date", "live", "eșantion", "esantion", "omb"))
            return "Date live: Connect OK + canale On + Apply senzor + Start (F5). Record e separat. Graficul live arată 8000 pct; CSV e complet.";
        if (ContainsAny(t, "timbru", "tensometr", "µm", "um/m", "gf"))
            return "Timbru: panoul Timbru / Mărci tensometrice — GF, tip punte, Calculează Scale → Aplică → Zero. Fără foaie Contur.";
        if (ContainsAny(t, "record", "csv", "înregistr"))
            return "Record: Start live → Record. CSV-ul salvează tot. Export poate rula în fundal. Folder recordings.";
        if (ContainsAny(t, "bannerul roșu", "bannerul rosu", "est=10003"))
            return ShuntCheck.SuggestEstError;
        if (ContainsAny(t, "scale invalid", "91885"))
            return ShuntCheck.SuggestScaleAbsurd;
        if (ContainsAny(t, "treaptă ok", "treapta ok"))
            return ShuntCheck.SuggestPass;
        if (ContainsAny(t, "led", "est", "error"))
            return "LED ERROR / EST: Stop → Disconnect → power-cycle Spider8 ~10 s → Connect din nou.";
        if (ContainsAny(t, "shunt"))
            return "Shunt verifică puntea; pentru semn invers folosiți Inversare polaritate, nu shunt.";
        if (ContainsAny(t, "polar", "semn") && !ContainsAny(t, "contur"))
            return "Polaritate: click dreapta canal → Inversare polaritate (Scale × −1), apoi Zero pe liber.";
        if (ContainsAny(t, "scale suspect", "capacity", "suprasarcin"))
            return "Scale suspect: |F|>Capacity×1.2 fără confirmare sarcină mare — verificați Scale/Zero; valorile nu sunt plafonate.";
        if (ContainsAny(t, "half", "compensare", "temperatur"))
            return "Half bridge: verificați compensarea termică (timbru compensat) înainte de etalonare.";
        if (ContainsAny(t, "exc", "excita"))
            return "Exc V: tipic 2.5 V pe Spider8 SoftSetup; P15=0 V. Diferențe față de catalog pot înclina Scale.";
        if (ContainsAny(t, "filtru", "bessel", "butterworth", "anti-alias"))
            return "Filtru: coloana Filtru (Hz) + tip Bessel/Butterworth în Metrologie — LPF software pe live și Record.";
        if (ContainsAny(t, "treapt", "median", "liniaritate", "histerez"))
            return "Metrologie: Citire treaptă (mediană/medie), Δ% vs m·g, liniaritate ε(F), Histereză Zero — tab Calibrare.";
        if (ContainsAny(t, "compar", "anterior", "ultimul test"))
            return "Asistentul compară cu ultimele 1–3 înregistrări de același tip din recordings (ε_max sau ovalitate/bombare).";
        return null;
    }

    private static double ScoreBase(Rule r, AdvisorMemory? memory) =>
        1.0 + (memory?.Boost(r.Id) ?? 0);

    private static double ContextBoost(string ruleId, string text, AdviceContext ctx)
    {
        var boost = 0.0;
        var sensor = ctx.SelectedSensor ?? "";
        if (ruleId == "p15-dcvoltage" && sensor.Contains("P15", StringComparison.OrdinalIgnoreCase))
            boost += 0.6;
        if (ruleId == "u2b-force" &&
            (sensor.Contains("U2B", StringComparison.OrdinalIgnoreCase) ||
             sensor.Contains("5kN", StringComparison.OrdinalIgnoreCase)))
            boost += 0.6;
        if (ruleId is "connect-openport" or "connect-timeout" or "connect-catman" or "usb-hbm" or "connect-generic"
            && !ctx.IsConnected)
            boost += 0.5;
        if (ruleId == "no-live-samples" && ctx.IsConnected && !ctx.IsStreaming)
            boost += 0.45;
        if (ruleId == "no-live-samples" && ctx.ChannelsOn <= 0)
            boost += 0.35;
        if (ruleId == "serial-com" && text.Contains("COM", StringComparison.OrdinalIgnoreCase))
            boost += 0.2;
        if (ruleId is "timbru" or "half-bridge-thermal" && ExperimentTypes.IsStrainGauges(ctx.ExperimentType))
            boost += 0.45;
        if (ruleId == "defect-zero" && ctx.IsConnected && !ctx.IsZeroed)
            boost += 0.4;
        if ((ruleId is "shunt-no-omb" or "shunt-est" or "shunt-step" or "shunt-step-low" or "shunt-step-high"
             or "shunt-dll" or "shunt-rsh" or "shunt-timbru-missing" or "shunt-pass" or "shunt-half-dummy")
            && text.Contains("Shunt", StringComparison.OrdinalIgnoreCase))
            boost += 1.6;
        if (ruleId == "shunt-half-dummy"
            && (text.Contains("shunt intern NU e în punte", StringComparison.Ordinal)
                || text.Contains("PASS (Half+dummy)", StringComparison.Ordinal)
                || text.Contains("Nu e FAIL de cablaj 1893", StringComparison.Ordinal)))
            boost += 0.5;
        if (ruleId is "shunt-step" or "shunt-step-low" or "shunt-step-high"
            && (text.Contains("Nu e FAIL de cablaj 1893", StringComparison.Ordinal)
                || text.Contains("shunt intern NU e în punte", StringComparison.Ordinal)
                || text.Contains("PASS (Half+dummy)", StringComparison.Ordinal)))
            boost -= 2.5;
        if (ruleId == "scale-timbru-absurd"
            && (text.Contains("Scale invalid", StringComparison.OrdinalIgnoreCase)
                || text.Contains("91885", StringComparison.Ordinal)))
            boost += 1.8;
        if (ruleId == "shunt-est" && text.Contains("bannerul roșu", StringComparison.OrdinalIgnoreCase))
            boost += 0.4;
        if (ruleId == "no-live-samples"
            && text.Contains("fără citire OMB", StringComparison.OrdinalIgnoreCase))
            boost -= 0.8;
        if (ruleId == "led-error"
            && text.Contains("Sugestie:", StringComparison.Ordinal)
            && text.Contains("Shunt", StringComparison.OrdinalIgnoreCase))
            boost -= 0.6;
        if (ruleId == "shunt" && ContainsAny(text.ToLowerInvariant(), "fail", "lipsește", "lipseste", "fără citire", "fara citire"))
            boost -= 0.5;
        return boost;
    }

    private static AdviceItem ToItem(Rule r, double score) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Suggestion = r.Suggestion,
        Severity = r.Sev,
        Steps = r.Steps,
        Score = score,
        ActionId = r.ActionId,
        ActionLabel = r.ActionLabel
    };

    public static bool LooksLikeProblem(string status)
    {
        var s = status.ToLowerInvariant();
        return s.Contains("eșuat") || s.Contains("esuat") || s.Contains("error") || s.Contains("eroare")
               || s.Contains("timeout") || s.Contains("led") || s.Contains("avertisment")
               || s.Contains("fără") || s.Contains("lipsă") || s.Contains("lipsa") || s.Contains("failed")
               || s.Contains("openport") || s.Contains("est=") || s.Contains("power-cycle")
               || s.Contains("golă") || s.Contains("gola") || s.Contains("citire")
               || s.Contains("suprasarcin") || s.Contains("fail") || s.Contains("ocupat");
    }

    private static bool LooksLikeQuestion(string text) =>
        text.Contains('?', StringComparison.Ordinal)
        || text.StartsWith("cum ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("de ce ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("ce ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("unde ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("care ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("nu ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("ajutor", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("help", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("compar", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string haystackLower, params string[] needles) =>
        needles.Any(n => haystackLower.Contains(n, StringComparison.Ordinal));
}
