using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Compact industrial-style PDF (1–3 pages): identity, specimen, method, validity zone,
/// best-effort results, quality notes, signature lines, disclaimer; optional dedicated
/// cylinder-contour page when ContourJpegPath is set.
/// Hand-rolled PDF 1.4 (Helvetica) — same approach as <see cref="PdfReportExporter"/>.
/// </summary>
public static class IndustrialReportExporter
{
    private const double PageW = 612;
    private const double PageH = 792;
    private const double Margin = 42;
    private const double ContentW = PageW - 2 * Margin;

    /// <summary>Same text as <see cref="IndustrialReportSections.DisclaimerRo"/> (PdfSafe applied at render).</summary>
    public const string DisclaimerRo = IndustrialReportSections.DisclaimerRo;

    public sealed class Options
    {
        public string SoftwareVersion { get; init; } = "";
        /// <summary>Validă / Alterată / Incompletă / Lipsă / Neverificată (from MeasurementFingerprint).</summary>
        public string? FingerprintStatusLabel { get; init; }
        /// <summary>Sealed CSV path for fingerprint verify (preferred over session/.upet payload).</summary>
        public string? CsvPath { get; init; }
        public string? PolarityOrDefectWarning { get; init; }
        public string? OverviewJpegPath { get; init; }
        /// <summary>Optional cylinder top-view contour JPEG (Fmax).</summary>
        public string? ContourJpegPath { get; init; }
        /// <summary>Optional deformation-curve JPEGs (uᵢ vs t / cursă / F).</summary>
        public IReadOnlyList<string>? DeformationCurveJpegPaths { get; init; }
        /// <summary>Override session cursors; null = use OfflineSession.CursorA/B.</summary>
        public int? CursorA { get; init; }
        public int? CursorB { get; init; }
    }

    public static void Export(
        string path,
        OfflineSession session,
        ProjectMeta meta,
        Options? options = null)
    {
        options ??= new Options();
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        SampleDimensions.ApplyComputedFields(meta);
        StrainAnalysisIndicators.ApplyFromSummaries(meta);

        var images = new List<(byte[] Jpeg, int W, int H, string Role)>();
        try
        {
            var logoJpeg = ReportHeaderHelper.TryGetLogoJpegBytes(640);
            if (logoJpeg is { Length: > 0 } && IsJpeg(logoJpeg))
            {
                var (lw, lh) = ReadJpegSize(logoJpeg);
                images.Add((logoJpeg, lw, lh, "logo"));
            }
        }
        catch { /* logo optional */ }

        if (!string.IsNullOrWhiteSpace(options.OverviewJpegPath) && File.Exists(options.OverviewJpegPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(options.OverviewJpegPath);
                if (IsJpeg(bytes))
                {
                    var (w, h) = ReadJpegSize(bytes);
                    images.Add((bytes, w, h, "yt"));
                }
            }
            catch { /* skip */ }
        }

        CylinderContourExport.MergeFromSession(meta, session);
        var includeContourChapter = CylinderContourExport.ShouldAttempt(meta, session);

        if (includeContourChapter &&
            !string.IsNullOrWhiteSpace(options.ContourJpegPath) && File.Exists(options.ContourJpegPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(options.ContourJpegPath);
                if (IsJpeg(bytes))
                {
                    var (w, h) = ReadJpegSize(bytes);
                    images.Add((bytes, w, h, "contour"));
                }
            }
            catch { /* skip */ }
        }

        if (includeContourChapter && options.DeformationCurveJpegPaths is { Count: > 0 })
        {
            foreach (var jp in options.DeformationCurveJpegPaths)
            {
                if (string.IsNullOrWhiteSpace(jp) || !File.Exists(jp)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(jp);
                    if (!IsJpeg(bytes)) continue;
                    var (w, h) = ReadJpegSize(bytes);
                    images.Add((bytes, w, h, "deform_curve"));
                }
                catch { /* skip */ }
            }
        }

        var ctx = BuildContext(session, meta, options);
        var pageContents = BuildPages(ctx, images);

        WritePdf(fullPath, images, pageContents);
    }

    private sealed class ReportContext
    {
        public required OfflineSession Session { get; init; }
        public required ProjectMeta Meta { get; init; }
        public required Options Options { get; init; }
        public required OfflineSession StatsSession { get; init; }
        public required bool ValidityWindow { get; init; }
        public required int IndexA { get; init; }
        public required int IndexB { get; init; }
        public required string ValidityText { get; init; }
        public required IReadOnlyList<(string Label, string Value)> ResultRows { get; init; }
        public required IReadOnlyList<ChannelStats> ChannelStats { get; init; }
        public required IReadOnlyList<string> QualityLines { get; init; }
        public string? ContourFootnoteRo { get; init; }
    }

    private static ReportContext BuildContext(OfflineSession session, ProjectMeta meta, Options options)
    {
        var n = Math.Max(0, session.Timestamps.Count);
        var rawA = options.CursorA ?? session.CursorA;
        var rawB = options.CursorB ?? session.CursorB;
        var a = n == 0 ? 0 : Math.Clamp(Math.Min(rawA, rawB), 0, n - 1);
        var b = n == 0 ? 0 : Math.Clamp(Math.Max(rawA, rawB), 0, n - 1);
        // Default load sets CursorA=0, CursorB=N-1 — that is the full recording, not a hand-picked zone.
        var fullSpan = n > 1 && a == 0 && b == n - 1;
        var validityWindow = n > 1 && a != b && !fullSpan;
        var statsSession = validityWindow ? session.ExportRegion(a, b + 1) : session;

        string validityText;
        if (!validityWindow || n == 0)
        {
            validityText = "Zona predare / date oficiale: intreaga inregistrare.";
        }
        else
        {
            var t0 = session.Timestamps[0];
            var tA = (session.Timestamps[a] - t0).TotalSeconds;
            var tB = (session.Timestamps[b] - t0).TotalSeconds;
            validityText =
                $"Zona predare / date oficiale: index {a}-{b} " +
                $"(t={tA.ToString("0.###", CultureInfo.InvariantCulture)}-" +
                $"{tB.ToString("0.###", CultureInfo.InvariantCulture)} s).";
        }

        var channelStats = statsSession.Stats?.ToList() ?? new List<ChannelStats>();
        var resultRows = BuildResultRows(session, statsSession, meta, options, channelStats);
        var quality = BuildQualityLines(session, meta, options, validityWindow);
        string? contourFootnote = null;
        if (CylinderContourExport.ShouldAttempt(meta, session))
        {
            try
            {
                var contour = CylinderContourExport.TryCompute(
                    session, meta, options.CursorA, options.CursorB);
                if (contour is { IsValid: true })
                {
                    contourFootnote = string.IsNullOrWhiteSpace(contour.PlotFootnoteRo)
                        ? contour.IndexRuleFootnoteRo
                        : contour.PlotFootnoteRo;
                    quality.Add(contour.ToSummaryRo());
                    if (!string.IsNullOrWhiteSpace(contour.FlatSensorWarningRo))
                        quality.Add(contour.FlatSensorWarningRo!);
                    foreach (var row in contour.BuildReportRows())
                        resultRows.Add(row);
                }
                else if (contour is not null && !string.IsNullOrWhiteSpace(contour.Error))
                {
                    quality.Add("Contur cilindru: " + contour.Error);
                }
            }
            catch (Exception ex)
            {
                quality.Add("Contur cilindru: generare eșuată — " + ex.Message);
            }
        }

        return new ReportContext
        {
            Session = session,
            Meta = meta,
            Options = options,
            StatsSession = statsSession,
            ValidityWindow = validityWindow,
            IndexA = a,
            IndexB = b,
            ValidityText = validityText,
            ResultRows = resultRows,
            ChannelStats = channelStats,
            QualityLines = quality,
            ContourFootnoteRo = contourFootnote
        };
    }

    private static List<(string Label, string Value)> BuildResultRows(
        OfflineSession fullSession,
        OfflineSession statsSession,
        ProjectMeta meta,
        Options options,
        IReadOnlyList<ChannelStats> stats)
    {
        var rows = new List<(string, string)>();

        foreach (var s in stats.Take(10))
        {
            rows.Add((
                Trunc(PdfSafe(s.Name), 36),
                $"min={Fmt(s.Min)}  max={Fmt(s.Max)}  mean={Fmt(s.Mean)}"));
        }

        foreach (var (label, value) in StrainAnalysisIndicators.BuildReportMetaRows(meta, emptyAsDash: false))
            rows.Add((label, value));

        var force = TryDeriveForceStress(statsSession, meta);
        if (force is not null)
        {
            rows.Add(("Fmax (canal forta)", force.FmaxText));
            if (!string.IsNullOrWhiteSpace(force.SigmaText))
                rows.Add(("sigma derivat", force.SigmaText));
            if (!string.IsNullOrWhiteSpace(force.Assumption))
                rows.Add(("Ipoteza Fmax/sigma", force.Assumption));
        }

        var csvPath = MeasurementFingerprint.ResolveExistingCsvPath(
            options.CsvPath, fullSession.SourcePath);
        var status = MeasurementFingerprint.ResolveReportStatusLabel(
            fullSession, meta, options.FingerprintStatusLabel, csvPath);

        if (!string.IsNullOrWhiteSpace(meta.MeasurementFingerprint))
            rows.Add(("Amprenta masurare", meta.MeasurementFingerprint.Trim()));
        else
            rows.Add(("Amprenta masurare", "- (nesigilata)"));
        rows.Add(("Stare amprenta", status));

        return rows;
    }

    private sealed class ForceStressResult
    {
        public string FmaxText { get; init; } = "";
        public string? SigmaText { get; init; }
        public string? Assumption { get; init; }
    }

    private static ForceStressResult? TryDeriveForceStress(OfflineSession session, ProjectMeta meta)
    {
        var idx = FindForceChannelIndex(session, meta);
        if (idx < 0 || idx >= session.Columns.Count) return null;

        var name = session.ChannelNames[idx];
        var unit = GuessUnit(name, meta, idx);
        var stats = idx < session.Stats.Count
            ? session.Stats[idx]
            : SignalAnalysis.ComputeStats(name, session.Columns[idx]);
        if (stats.Count == 0 || double.IsNaN(stats.Max)) return null;

        // Prefer peak tensile (max); if compressive-only, use |min|.
        var fPeak = Math.Abs(stats.Max) >= Math.Abs(stats.Min) ? stats.Max : stats.Min;
        var fAbs = Math.Abs(fPeak);
        var fN = ConvertForceToNewtons(fAbs, unit, out var unitNote);
        var fmaxText =
            $"{Fmt(fPeak)} {unit}".Trim() +
            (string.IsNullOrWhiteSpace(unitNote) ? "" : $"  (~{Fmt(fN)} N)");

        var area = SampleDimensions.ResolveAreaMm2(meta);
        string? sigmaText = null;
        string? assumption =
            "Fmax = extrem pe canalul de forta in zona oficiala; polaritate neatribuita automat.";
        if (!string.IsNullOrWhiteSpace(unitNote))
            assumption += " " + unitNote;

        if (area > 0 && fN > 0)
        {
            // N / mm² = MPa
            var sigmaMpa = fN / area;
            sigmaText =
                $"{Fmt(sigmaMpa)} MPa  (sigma ≈ |F|_N / A_mm2; A={Fmt(area)} mm2)";
            assumption +=
                " sigma = |F| convertit in N impartit la aria sectiunii [mm2] (echivalent MPa).";
        }
        else if (area <= 0)
        {
            assumption += " Aria probei nestata — sigma necalculat.";
        }

        return new ForceStressResult
        {
            FmaxText = fmaxText + $" [{Trunc(PdfSafe(name), 28)}]",
            SigmaText = sigmaText,
            Assumption = assumption
        };
    }

    private static int FindForceChannelIndex(OfflineSession session, ProjectMeta meta)
    {
        for (var i = 0; i < session.ChannelNames.Count; i++)
        {
            var name = session.ChannelNames[i];
            var unit = GuessUnit(name, meta, i);
            var cat = meta.Channels?.FirstOrDefault(c =>
                c.Index == i ||
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.SensorCategory;
            if (IsForceLike(name, unit, cat))
                return i;
        }
        return -1;
    }

    private static string GuessUnit(string channelName, ProjectMeta meta, int index)
    {
        var fromMeta = meta.Channels?.FirstOrDefault(c =>
            c.Index == index ||
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase))?.Unit;
        if (!string.IsNullOrWhiteSpace(fromMeta)) return fromMeta!;
        var a = channelName.IndexOf('[');
        var b = channelName.IndexOf(']');
        if (a >= 0 && b > a) return channelName[(a + 1)..b].Trim();
        return "";
    }

    private static bool IsForceLike(string name, string? unit, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            (category.Contains("For", StringComparison.OrdinalIgnoreCase) ||
             category.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
             category.Contains("Load", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (name.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fort", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("U2B", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Load", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Equals("N", StringComparison.OrdinalIgnoreCase)
               || u.Equals("kN", StringComparison.OrdinalIgnoreCase)
               || u.Contains("kN", StringComparison.OrdinalIgnoreCase)
               || (u.Contains('N') && !u.Contains("mm", StringComparison.OrdinalIgnoreCase)
                   && !u.Contains("nm", StringComparison.OrdinalIgnoreCase));
    }

    private static double ConvertForceToNewtons(double value, string? unit, out string note)
    {
        note = "";
        if (string.IsNullOrWhiteSpace(unit))
        {
            note = "Unitate forta necunoscuta — tratat ca N.";
            return value;
        }
        var u = unit.Trim();
        if (u.Contains("kN", StringComparison.OrdinalIgnoreCase))
        {
            note = "Conversie: 1 kN = 1000 N.";
            return value * 1000.0;
        }
        if (u.Contains("kg", StringComparison.OrdinalIgnoreCase) &&
            !u.Contains("kN", StringComparison.OrdinalIgnoreCase))
        {
            note = "Conversie: kgf ≈ kg * 9.80665 N (aproximativ).";
            return value * 9.80665;
        }
        if (u.Contains('N'))
        {
            note = "Unitate tratata ca newtoni (N).";
            return value;
        }
        note = "Unitate forta nerecunoscuta — tratat ca N.";
        return value;
    }

    private static List<string> BuildQualityLines(
        OfflineSession session,
        ProjectMeta meta,
        Options options,
        bool validityWindow)
    {
        var lines = new List<string>();
        var fsSet = meta.SampleRateHz > 0 ? meta.SampleRateHz : 0;
        var fsEf = SamplingRateInfo.EstimateEffectiveHz(
            session.Timestamps, fsSet > 0 ? fsSet : 50);
        lines.Add(fsSet > 0
            ? $"fs setata={fsSet.ToString(CultureInfo.InvariantCulture)} Hz; fs efectiva≈{SamplingRateInfo.FormatHz(fsEf)} Hz."
            : $"fs efectiva≈{SamplingRateInfo.FormatHz(fsEf)} Hz (fs setata nespecificata).");

        if (validityWindow)
            lines.Add("Statisticile din Rezultate sunt pe zona CursorA-CursorB (date oficiale).");
        else
            lines.Add("Statisticile din Rezultate sunt pe intreaga inregistrare.");

        if (!string.IsNullOrWhiteSpace(options.PolarityOrDefectWarning))
            lines.Add("Avertisment polaritate/defect: " + options.PolarityOrDefectWarning.Trim());
        else if (!string.IsNullOrWhiteSpace(meta.DeviceEstHint) && meta.DeviceEstHint != "EST: —")
            lines.Add("Stare EST: " + meta.DeviceEstHint.Trim());

        lines.Add("Trasabilitate si interpretare conform sistemului calitatii / procedurilor laboratorului emitent.");
        lines.Add("Indicatorii derivati (sigma, nu, recuperare) sunt best-effort din datele disponibile.");
        return lines;
    }

    private static List<string> BuildPages(
        ReportContext ctx,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images)
    {
        var pages = new List<string>();
        var logoCount = images.Count(i => i.Role == "logo") > 0 ? 1 : 0;
        var ytIndex = -1;
        var contourIndex = -1;
        for (var i = 0; i < images.Count; i++)
        {
            if (images[i].Role == "yt" && ytIndex < 0) ytIndex = i;
            if (images[i].Role == "contour" && contourIndex < 0) contourIndex = i;
        }

        pages.Add(BuildPage1(ctx, images, logoCount, ytIndex));
        // Contour always gets its own page when present — page-2 packing used to drop it
        // when quality/result rows consumed vertical space (silent missing graph).
        if (NeedsSecondPage(ctx, ytIndex >= 0 || contourIndex >= 0))
            pages.Add(BuildPage2(ctx, images, ytIndex, contourIndex: -1));
        if (contourIndex >= 0)
            pages.Add(BuildContourPage(ctx, images, contourIndex));
        foreach (var di in Enumerable.Range(0, images.Count).Where(i => images[i].Role == "deform_curve"))
            pages.Add(BuildDeformationCurvePage(images, di));
        return pages;
    }

    private static bool NeedsSecondPage(ReportContext ctx, bool hasExtraImage)
    {
        // Page 1 packs header + sections; overflow signatures/disclaimer/plot → page 2
        // Always put signatures + disclaimer on page 2 when many result rows or plot present.
        return ctx.ResultRows.Count > 8 || ctx.ChannelStats.Count > 6 || hasExtraImage
               || ctx.QualityLines.Count > 4;
    }

    private static string BuildPage1(
        ReportContext ctx,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int logoCount,
        int ytIndex)
    {
        var sb = new StringBuilder(24_576);
        var y = PageH - Margin;
        var left = Margin;
        var right = PageW - Margin;
        var meta = ctx.Meta;
        var session = ctx.Session;
        var opt = ctx.Options;

        double logoDw = 0, logoDh = 0;
        if (logoCount > 0)
        {
            var (lw, lh, _) = (images[0].W, images[0].H, images[0].Role);
            var targetH = 72.0;
            var scale = targetH / Math.Max(1, lh);
            logoDw = lw * scale;
            logoDh = lh * scale;
            var logoY = y - logoDh;
            sb.AppendLine(Inv($"q {logoDw:0.##} 0 0 {logoDh:0.##} {left:0.##} {logoY:0.##} cm /Im0 Do Q"));
        }

        var textLeft = left + (logoDw > 0 ? logoDw + 12 : 0);
        var headerTop = y - 2;
        WriteText(sb, "F2", 14, textLeft, headerTop,
            PdfSafe(ReportHeaderHelper.ProductName + " — Raport industrial"));
        WriteText(sb, "F1", 8, textLeft, headerTop - 12,
            PdfSafe(ReportHeaderHelper.InstitutionLine));
        WriteText(sb, "F1", 8, textLeft, headerTop - 23,
            PdfSafe(ReportHeaderHelper.DepartmentName));
        WriteText(sb, "F2", 9, textLeft, headerTop - 35,
            PdfSafe(ReportHeaderHelper.ExpertLine));
        WriteText(sb, "F1", 8, textLeft, headerTop - 46,
            PdfSafe(ReportHeaderHelper.AuthorLine));
        var ver = string.IsNullOrWhiteSpace(opt.SoftwareVersion) ? "—" : opt.SoftwareVersion.Trim();
        WriteText(sb, "F2", 9, textLeft, headerTop - 58,
            PdfSafe($"Versiune {ver}  ·  Generat {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
        WriteText(sb, "F1", 7, textLeft, headerTop - 70,
            PdfSafe("Raport de măsurătoare — laborator metrologic industrial (Universitatea din Petroșani)."));

        y = Math.Min(y - Math.Max(logoDh, 74), headerTop - 74) - 8;
        sb.Append(ReportHeaderHelper.PdfTricolorStripe(left, y - 1.5, right - left, 3.5));
        y -= 14;

        y = WriteSection(sb, left, y, "1. Identitate", BuildIdentityRows(session, meta, opt));
        y = WriteSection(sb, left, y, "2. Specimen (proba)", BuildSpecimenRows(meta));
        y = WriteSection(sb, left, y, "3. Metoda", BuildMethodRows(meta));

        WriteText(sb, "F2", 10, left, y, "4. Zona de validitate");
        y -= 7;
        StrokeHLine(sb, left, left + 130, y);
        y -= 12;
        foreach (var line in Wrap(PdfSafe(ctx.ValidityText), ContentW, 8).Take(3))
        {
            WriteText(sb, "F1", 8, left, y, line);
            y -= 11;
        }
        y -= 8;

        WriteText(sb, "F2", 10, left, y, "5. Rezultate (best-effort)");
        y -= 7;
        StrokeHLine(sb, left, left + 150, y);
        y -= 12;

        // Compact channel stats table
        if (ctx.ChannelStats.Count > 0 && y > Margin + 120)
        {
            string[] headers = { "Canal", "Min", "Max", "Mean" };
            double[] colW = { 0.40, 0.20, 0.20, 0.20 };
            for (var i = 0; i < colW.Length; i++)
                colW[i] *= ContentW;

            var headH = 13.0;
            FillRect(sb, left, y - headH + 2, ContentW, headH, 0.11, 0.14, 0.19);
            var hx = left;
            for (var c = 0; c < headers.Length; c++)
            {
                WriteText(sb, "F2", 7, hx + 2, y - 8, headers[c], white: true);
                hx += colW[c];
            }
            y -= headH;

            var rowIdx = 0;
            foreach (var s in ctx.ChannelStats.Take(8))
            {
                if (y < Margin + 90) break;
                var rh = 12.0;
                if (rowIdx % 2 == 1)
                    FillRect(sb, left, y - rh + 2, ContentW, rh, 0.96, 0.97, 0.98);
                StrokeRect(sb, left, y - rh + 2, ContentW, rh);
                var cells = new[] { Trunc(PdfSafe(s.Name), 32), Fmt(s.Min), Fmt(s.Max), Fmt(s.Mean) };
                var cx = left;
                for (var c = 0; c < cells.Length; c++)
                {
                    WriteText(sb, "F1", 7, cx + 2, y - 8, cells[c]);
                    cx += colW[c];
                }
                y -= rh;
                rowIdx++;
            }
            y -= 8;
        }

        // Derived / fingerprint rows (skip channel lines already in table)
        foreach (var (label, value) in ctx.ResultRows.Where(r =>
                     !r.Label.StartsWith("min=", StringComparison.Ordinal) &&
                     !LooksLikeChannelStatRow(r)))
        {
            if (y < Margin + 60) break;
            y = WriteMetaRow(sb, left, y, label, value, compact: true);
        }

        if (!NeedsSecondPage(ctx, ytIndex >= 0 || !string.IsNullOrWhiteSpace(ctx.Options.ContourJpegPath)))
        {
            y = WriteQualityBlock(sb, left, y, ctx);
            y = WriteSignatures(sb, left, y);
            WriteDisclaimer(sb, left, y);
        }
        else
        {
            WriteText(sb, "F1", 7, left, Margin + 8,
                PdfSafe("(continua: calitate, semnaturi, disclaimer — pagina 2)"));
        }

        return sb.ToString();
    }

    private static bool LooksLikeChannelStatRow((string Label, string Value) row) =>
        row.Value.Contains("min=", StringComparison.Ordinal) &&
        row.Value.Contains("max=", StringComparison.Ordinal);

    private static string BuildPage2(
        ReportContext ctx,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int ytIndex,
        int contourIndex = -1)
    {
        _ = contourIndex; // Contour uses BuildContourPage — kept for call-site compatibility.
        var sb = new StringBuilder(12_288);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        WriteText(sb, "F2", 11, left, y, "Raport industrial — continuare");
        y -= 8;
        StrokeHLine(sb, left, left + 200, y);
        y -= 16;

        // Remaining result rows if any were truncated — re-list derived indicators compactly
        var derived = ctx.ResultRows.Where(r => !LooksLikeChannelStatRow(r)).ToList();
        if (derived.Count > 0)
        {
            WriteText(sb, "F2", 10, left, y, "Rezultate derivate (reluare)");
            y -= 7;
            StrokeHLine(sb, left, left + 140, y);
            y -= 12;
            foreach (var (label, value) in derived)
            {
                if (y < Margin + 220) break;
                y = WriteMetaRow(sb, left, y, label, value, compact: true);
            }
            y -= 6;
        }

        y = WriteQualityBlock(sb, left, y, ctx);

        // Contour is rendered on a dedicated page (BuildContourPage) so it cannot be clipped.

        if (ytIndex >= 0 && y > Margin + 160)
        {
            WriteText(sb, "F2", 10, left, y,
                PdfSafe(ctx.ValidityWindow
                    ? "Diagrama Y(t) — orientativ (inregistrare; zona oficiala = CursorA-B)"
                    : "Diagrama Y(t) — orientativ (intreaga inregistrare)"));
            y -= 7;
            StrokeHLine(sb, left, left + 220, y);
            y -= 10;
            var (_, pw, ph, _) = images[ytIndex];
            var maxW = ContentW;
            var maxH = Math.Min(160.0, y - Margin - 100);
            if (maxH >= 50)
            {
                var scale = Math.Min(maxW / pw, maxH / ph);
                var dw = pw * scale;
                var dh = ph * scale;
                var imgY = y - dh;
                sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{ytIndex} Do Q"));
                y = imgY - 12;
            }
        }

        y = WriteSignatures(sb, left, y);
        WriteDisclaimer(sb, left, Math.Max(y, Margin + 50));
        return sb.ToString();
    }

    /// <summary>Dedicated page for cylinder top-view contour — always shown when JPEG is present.</summary>
    private static string BuildContourPage(
        ReportContext ctx,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int contourIndex)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        WriteText(sb, "F2", 12, left, y, PdfSafe("Contur cilindru — plan + elevatie + sectiune (R0 + Contur Fmax)"));
        y -= 8;
        StrokeHLine(sb, left, left + 320, y);
        y -= 14;

        if (!string.IsNullOrWhiteSpace(ctx.ContourFootnoteRo))
        {
            foreach (var line in Wrap(PdfSafe(ctx.ContourFootnoteRo), ContentW, 8).Take(4))
            {
                WriteText(sb, "F1", 8, left, y, line);
                y -= 11;
            }
            y -= 6;
        }
        else
        {
            WriteText(sb, "F1", 8, left, y,
                PdfSafe("R0 nedeformat; plan + elevatie + sectiune; Contur la Fmax; u_max; tabel u_i; mini F-cursa."));
            y -= 16;
        }

        var (_, pw, ph, _) = images[contourIndex];
        var maxW = ContentW;
        var maxH = Math.Min(520.0, y - Margin - 40);
        if (maxH >= 80 && pw > 0 && ph > 0)
        {
            var scale = Math.Min(maxW / pw, maxH / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var imgY = y - dh;
            sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{contourIndex} Do Q"));
        }

        return sb.ToString();
    }

    private static string BuildDeformationCurvePage(
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int imageIndex)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        WriteText(sb, "F2", 12, left, y, PdfSafe("Curbe deformare — u_i senzori radiali"));
        y -= 8;
        StrokeHLine(sb, left, left + 280, y);
        y -= 14;
        WriteText(sb, "F1", 8, left, y,
            PdfSafe("Serie u1..un vs timp / cursa / forta; linia verticala = index Contur / Fmax."));
        y -= 16;

        var (_, pw, ph, _) = images[imageIndex];
        var maxW = ContentW;
        var maxH = Math.Min(480.0, y - Margin - 30);
        if (maxH >= 80 && pw > 0 && ph > 0)
        {
            var scale = Math.Min(maxW / pw, maxH / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var imgY = y - dh;
            sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{imageIndex} Do Q"));
        }

        return sb.ToString();
    }

    private static List<(string Label, string Value)> BuildIdentityRows(
        OfflineSession session, ProjectMeta meta, Options opt)
    {
        var fsSet = meta.SampleRateHz;
        var fsEf = SamplingRateInfo.EstimateEffectiveHz(
            session.Timestamps, fsSet > 0 ? fsSet : 50);
        var ver = string.IsNullOrWhiteSpace(opt.SoftwareVersion) ? "—" : opt.SoftwareVersion.Trim();
        var ident = new List<(string, string)>
        {
            ("Operator", Dash(meta.Operator)),
            ("Proiect", Dash(meta.ProjectName)),
            ("Proba / Sample", Dash(meta.SampleId)),
        };
        ident.AddRange(Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta));
        ident.Add(("Backend / dispozitiv", Dash(meta.Backend)));
        ident.Add(("Software", ReportHeaderHelper.ProductName + " " + ver));
        ident.Add(("Autor soft", ReportHeaderHelper.SoftwareAuthor));
        ident.Add(("Rata setata [Hz]", fsSet > 0 ? fsSet.ToString(CultureInfo.InvariantCulture) : "—"));
        ident.Add(("Rata efectiva [Hz]", SamplingRateInfo.FormatHz(fsEf)));
        ident.Add(("Esantioane", session.Timestamps.Count.ToString(CultureInfo.InvariantCulture)));
        ident.Add(("Sursa", Dash(string.IsNullOrWhiteSpace(session.SourcePath)
            ? ""
            : Path.GetFileName(session.SourcePath))));
        return ident;
    }

    private static List<(string Label, string Value)> BuildSpecimenRows(ProjectMeta meta)
    {
        var rows = Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta).ToList();
        rows.AddRange(SampleDimensions.BuildReportMetaRows(meta, emptyAsDash: true));
        if (rows.Count == 0)
            rows.Add(("Dimensiuni / greutate", "— (nesetat la Start experiment)"));
        return rows;
    }

    private static List<(string Label, string Value)> BuildMethodRows(ProjectMeta meta)
    {
        var sensorNames = meta.Channels is { Count: > 0 }
            ? string.Join("; ", meta.Channels.Take(8).Select(c => $"{c.Name}={c.SensorDisplay}"))
            : (meta.SensorSummary ?? "");
        var rows = new List<(string, string)>
        {
            ("Tip experiment", Dash(meta.ExperimentType)),
        };
        foreach (var (label, value) in Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta)
                     .Where(r => r.Label is "Epruvetă" or "Pachet formule" or "Interpretare (pachet)"))
            rows.Add((label, value));
        rows.Add(("Nume experiment", Dash(meta.ExperimentName)));
        rows.Add(("Senzori (rezumat)", Dash(string.IsNullOrWhiteSpace(sensorNames) ? meta.PlannedSensors : sensorNames)));
        rows.AddRange(new (string, string)[]
        {
            ("Calibrare / note", Dash(meta.CalibrationNotes)),
            ("Start experiment", FormatIsoLocal(meta.ExperimentStartLocal)),
            ("Stop experiment", FormatIsoLocal(meta.ExperimentEndLocal)),
        });
        if (meta.ActiveChannelCount > 0)
            rows.Add(("Canale active", meta.ActiveChannelCount.ToString(CultureInfo.InvariantCulture)));
        return rows;
    }

    private static double WriteSection(
        StringBuilder sb, double left, double y, string title, IReadOnlyList<(string, string)> rows)
    {
        WriteText(sb, "F2", 10, left, y, title);
        y -= 7;
        StrokeHLine(sb, left, left + Math.Min(160, title.Length * 6.5), y);
        y -= 11;
        foreach (var (label, value) in rows)
        {
            if (y < Margin + 80) break;
            y = WriteMetaRow(sb, left, y, label, value, compact: true);
        }
        return y - 6;
    }

    private static double WriteMetaRow(
        StringBuilder sb, double left, double y, string label, string value, bool compact)
    {
        var labelW = compact ? 108.0 : 118.0;
        var font = compact ? 7.5 : 8.0;
        var isFpStatus = IndustrialReportSections.IsFingerprintStatusRow(label);
        var display = isFpStatus
            ? MeasurementFingerprint.FormatStatusForPdf(value)
            : value;
        var wrapped = Wrap(PdfSafe(display), ContentW - labelW - 6, font);
        var h = Math.Max(compact ? 11.0 : 13.0, wrapped.Count * (font + 2) + 2);
        FillRect(sb, left, y - h + 2, labelW, h, 0.91, 0.93, 0.95);
        StrokeRect(sb, left, y - h + 2, ContentW, h);
        WriteText(sb, "F2", font, left + 3, y - 8, PdfSafe(Trunc(label, 22)));
        var ty = y - 8;
        (double r, double g, double b)? rgb = isFpStatus
            ? MeasurementFingerprint.StatusColorRgb(value)
            : null;
        foreach (var line in wrapped.Take(3))
        {
            WriteText(sb, "F1", font, left + labelW + 3, ty, line, fillRgb: rgb);
            ty -= font + 2;
        }
        return y - h;
    }

    private static double WriteQualityBlock(StringBuilder sb, double left, double y, ReportContext ctx)
    {
        WriteText(sb, "F2", 10, left, y, "6. Calitate / limitari");
        y -= 7;
        StrokeHLine(sb, left, left + 120, y);
        y -= 12;
        foreach (var line in ctx.QualityLines)
        {
            if (y < Margin + 90) break;
            foreach (var w in Wrap(PdfSafe("• " + line), ContentW, 8).Take(3))
            {
                WriteText(sb, "F1", 8, left, y, w);
                y -= 11;
            }
        }
        return y - 8;
    }

    private static double WriteSignatures(StringBuilder sb, double left, double y)
    {
        WriteText(sb, "F2", 10, left, y, "7. Semnaturi");
        y -= 7;
        StrokeHLine(sb, left, left + 90, y);
        y -= 18;

        var colW = ContentW / 2 - 10;
        WriteText(sb, "F1", 8, left, y, PdfSafe("Semnătură operator:"));
        WriteText(sb, "F1", 8, left + colW + 20, y, PdfSafe("Semnătură expert:"));
        y -= 28;
        StrokeHLine(sb, left, left + colW - 20, y);
        StrokeHLine(sb, left + colW + 20, left + ContentW, y);
        y -= 12;
        WriteText(sb, "F1", 7, left, y, PdfSafe("Nume / data / semnatura"));
        WriteText(sb, "F1", 7, left + colW + 20, y, PdfSafe("Nume / data / stampila"));
        return y - 16;
    }

    private static void WriteDisclaimer(StringBuilder sb, double left, double y)
    {
        WriteText(sb, "F2", 9, left, y, "8. Disclaimer");
        y -= 7;
        StrokeHLine(sb, left, left + 80, y);
        y -= 11;
        foreach (var line in Wrap(PdfSafe(DisclaimerRo), ContentW, 7).Take(8))
        {
            if (y < Margin) break;
            WriteText(sb, "F1", 7, left, y, line);
            y -= 9;
        }
    }

    private static string FormatIsoLocal(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "—";
        if (DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return iso;
    }

    private static string Dash(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    private static string PdfSafe(string? s) => ReportHeaderHelper.PdfSafe(s ?? "");

    private static string Fmt(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString("G6", CultureInfo.InvariantCulture);

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static List<string> Wrap(string text, double maxWidthPt, double fontSize)
    {
        var maxChars = Math.Max(8, (int)(maxWidthPt / (fontSize * 0.48)));
        var lines = new List<string>();
        var t = text.Replace("\r", "").Replace("\n", " ");
        if (string.IsNullOrEmpty(t)) { lines.Add(""); return lines; }
        while (t.Length > maxChars)
        {
            var cut = t.LastIndexOf(' ', maxChars);
            if (cut < maxChars / 3) cut = maxChars;
            lines.Add(t[..cut].TrimEnd());
            t = t[cut..].TrimStart();
            if (lines.Count >= 5)
            {
                lines[^1] = Trunc(lines[^1] + " " + t, maxChars);
                return lines;
            }
        }
        lines.Add(t);
        return lines;
    }

    private static void WritePdf(
        string fullPath,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        IReadOnlyList<string> pageContents)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        int ObjStart()
        {
            offsets.Add(ms.Position);
            return offsets.Count;
        }

        W("%PDF-1.4\n");

        var fontReg = ObjStart();
        W($"{fontReg} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        var fontBold = ObjStart();
        W($"{fontBold} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");

        var imageIds = new List<int>();
        for (var i = 0; i < images.Count; i++)
        {
            var (jpeg, w, h, _) = images[i];
            var id = ObjStart();
            imageIds.Add(id);
            W($"{id} 0 obj\n");
            W($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\n");
            W("stream\n");
            ms.Write(jpeg, 0, jpeg.Length);
            W("\nendstream\nendobj\n");
        }

        var contentIds = new List<int>();
        foreach (var content in pageContents)
        {
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var contentId = ObjStart();
            contentIds.Add(contentId);
            W($"{contentId} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
            ms.Write(contentBytes, 0, contentBytes.Length);
            W("\nendstream\nendobj\n");
        }

        var xobjects = new StringBuilder();
        for (var i = 0; i < imageIds.Count; i++)
            xobjects.Append(FormattableString.Invariant($" /Im{i} {imageIds[i]} 0 R"));

        var pagesId = offsets.Count + 1 + contentIds.Count;
        var catalogId = pagesId + 1;

        var pageIds = new List<int>();
        for (var p = 0; p < contentIds.Count; p++)
        {
            var pageId = ObjStart();
            pageIds.Add(pageId);
            W($"{pageId} 0 obj\n<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {PageW:0} {PageH:0}] /Contents {contentIds[p]} 0 R /Resources << /Font << /F1 {fontReg} 0 R /F2 {fontBold} 0 R >> /XObject <<{xobjects} >> >> >>\nendobj\n");
        }

        ObjStart();
        var kids = string.Join(" ", pageIds.Select(id => $"{id} 0 R"));
        W($"{pagesId} 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageIds.Count} >>\nendobj\n");

        ObjStart();
        W($"{catalogId} 0 obj\n<< /Type /Catalog /Pages {pagesId} 0 R >>\nendobj\n");

        var xref = ms.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
            W($"{off:D10} 00000 n \n");
        W($"trailer\n<< /Size {offsets.Count + 1} /Root {catalogId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(fullPath, ms.ToArray());
    }

    /// <summary>Identity strip on later pages that already have a header (not stamped on plot images).</summary>
    private static double WriteLaterPageIdentityStrip(StringBuilder sb, double left, double y)
    {
        WriteText(sb, "F1", 7, left, y, PdfSafe(ReportHeaderHelper.CompactIdentityLine));
        return y - 12;
    }

    private static void WriteText(
        StringBuilder sb, string font, double size, double x, double y, string text,
        bool white = false, (double r, double g, double b)? fillRgb = null)
    {
        var safe = EscapePdf(text);
        if (white)
            sb.AppendLine("1 1 1 rg");
        else if (fillRgb is { } c)
            sb.AppendLine(Inv($"{c.r:0.###} {c.g:0.###} {c.b:0.###} rg"));
        else
            sb.AppendLine("0.11 0.14 0.19 rg");
        sb.AppendLine(Inv($"BT /{font} {size:0.##} Tf {x:0.##} {y:0.##} Td ({safe}) Tj ET"));
        if (white || fillRgb is not null)
            sb.AppendLine("0.11 0.14 0.19 rg");
    }

    private static void StrokeHLine(StringBuilder sb, double x0, double x1, double y)
    {
        sb.AppendLine("0.11 0.14 0.19 RG");
        sb.AppendLine("1.0 w");
        sb.AppendLine(Inv($"{x0:0.##} {y:0.##} m {x1:0.##} {y:0.##} l S"));
    }

    private static void StrokeRect(StringBuilder sb, double x, double y, double w, double h)
    {
        sb.AppendLine("0.77 0.81 0.84 RG");
        sb.AppendLine("0.5 w");
        sb.AppendLine(Inv($"{x:0.##} {y:0.##} {w:0.##} {h:0.##} re S"));
    }

    private static void FillRect(StringBuilder sb, double x, double y, double w, double h, double r, double g, double b)
    {
        sb.AppendLine(Inv($"{r:0.###} {g:0.###} {b:0.###} rg"));
        sb.AppendLine(Inv($"{x:0.##} {y:0.##} {w:0.##} {h:0.##} re f"));
        sb.AppendLine("0.11 0.14 0.19 rg");
    }

    private static string EscapePdf(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            if (ch is '\\' or '(' or ')')
                sb.Append('\\').Append(ch);
            else if (ch > 127)
                sb.Append('?');
            else if (ch >= 32)
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string Inv(FormattableString fs) => FormattableString.Invariant(fs);

    private static bool IsJpeg(byte[] data) =>
        data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    private static (int w, int h) ReadJpegSize(byte[] data)
    {
        int w = 800, h = 450;
        for (var i = 0; i < data.Length - 9; i++)
        {
            if (data[i] == 0xFF && (data[i + 1] == 0xC0 || data[i + 1] == 0xC2))
            {
                h = (data[i + 5] << 8) | data[i + 6];
                w = (data[i + 7] << 8) | data[i + 8];
                break;
            }
        }
        return (Math.Max(1, w), Math.Max(1, h));
    }
}
