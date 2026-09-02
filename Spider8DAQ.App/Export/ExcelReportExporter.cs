using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Time;

namespace Spider8DAQ.App.Export;

/// <summary>Result of an Excel report export (data + unit-grouped overview + per-channel plots).</summary>
public sealed class ExcelExportResult
{
    public string Path { get; init; } = "";
    public int SampleCount { get; init; }
    public int ChannelCount { get; init; }
    public int ActiveChannelCount { get; init; }
    /// <summary>Embedded chart images: overview panel(s) by unit + N per-channel sheets.</summary>
    public int ChartCount { get; init; }
    public IReadOnlyList<string> ChartChannelNames { get; init; } = Array.Empty<string>();
    /// <summary>True when Contur sheet / PNG was embedded.</summary>
    public bool ContourIncluded { get; init; }
    /// <summary>Romanian reason when contour was expected but skipped.</summary>
    public string? ContourSkipReasonRo { get; init; }
}

/// <summary>
/// Framed Excel report: cover/stats + overview (grouped by unit) + one sheet per active channel (1000×500, data-scaled) + raw data.
/// </summary>
public static class ExcelReportExporter
{
    /// <summary>Excel worksheet row limit (header row excluded).</summary>
    private const int ExcelMaxDataRows = 1_048_575;

    private static readonly HashSet<string> ReservedSheetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Raport", "Grafic", "Date", "Grafice canale", "CWT", "Contur", "Curbe deformare",
        "Peak la MARK", "Canale", "Montaj"
    };

    public static ExcelExportResult Export(
        string xlsxPath,
        OfflineSession session,
        ProjectMeta meta,
        string? plotPngPath = null,
        string? csvPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(xlsxPath))!);

        string? overviewDir = null;
        string? perChannelDir = null;
        string? cwtDir = null;
        string? contourDir = null;
        IReadOnlyList<(string UnitKey, string Title, string Path)> overviewPlots =
            Array.Empty<(string, string, string)>();
        IReadOnlyList<(string Name, string Path)> perChannel = Array.Empty<(string, string)>();
        IReadOnlyList<(string Name, string Path)> cwtPlots = Array.Empty<(string, string)>();
        string? contourPng = null;
        string? contourFootnote = null;
        string? contourSkipReason = null;
        CylinderContourResult? contourResult = null;
        CylinderDeformationCurveRenderer.CurvePack? deformCurves = null;
        string? deformDir = null;
        var attemptContour = false;

        // Contur chapter only for «Compresiune cilindru – contur» (leftover ContourConfig on other types is ignored).
        CylinderContourExport.MergeFromSession(meta, session);
        CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session);
        attemptContour = CylinderContourExport.ShouldAttempt(meta, session);

        // Optional caller-supplied single PNG still accepted as first overview panel.
        if (!string.IsNullOrWhiteSpace(plotPngPath) && File.Exists(plotPngPath))
        {
            overviewPlots = new List<(string, string, string)> { ("", "Grafic măsurătoare", plotPngPath) };
        }
        else
        {
            try
            {
                overviewDir = Path.Combine(Path.GetTempPath(), $"upet_ovplots_{Guid.NewGuid():N}");
                overviewPlots = SessionPlotRenderer.SaveUnitGroupedPngs(session, overviewDir);
            }
            catch
            {
                overviewPlots = Array.Empty<(string, string, string)>();
            }
        }

        try
        {
            perChannelDir = Path.Combine(Path.GetTempPath(), $"upet_chplots_{Guid.NewGuid():N}");
            perChannel = SessionPlotRenderer.SavePerChannelPngs(session, perChannelDir);
        }
        catch
        {
            perChannel = Array.Empty<(string, string)>();
        }

        try
        {
            cwtDir = Path.Combine(Path.GetTempPath(), $"upet_cwt_{Guid.NewGuid():N}");
            var fs = meta.SampleRateHz > 0 ? meta.SampleRateHz : 50;
            cwtPlots = CwtPlotRenderer.SavePerChannelPngs(session, cwtDir, fallbackSampleRateHz: fs, windowSeconds: 0);
        }
        catch
        {
            cwtPlots = Array.Empty<(string, string)>();
        }

        if (attemptContour)
        {
            try
            {
                contourDir = Path.Combine(Path.GetTempPath(), $"upet_contour_{Guid.NewGuid():N}");
                Directory.CreateDirectory(contourDir);
                var path = Path.Combine(contourDir, "contour.png");
                contourResult = CylinderContourExport.TryCompute(session, meta);
                if (contourResult is { IsValid: true })
                {
                    try
                    {
                        CylinderContourPlotRenderer.BuildPlot(contourResult).SavePng(
                            path, CylinderContourPlotRenderer.DefaultWidth, CylinderContourPlotRenderer.DefaultHeight);
                        if (File.Exists(path) && new FileInfo(path).Length > 0)
                        {
                            contourPng = path;
                            contourFootnote = contourResult.PlotFootnoteRo;
                        }
                        else
                        {
                            contourSkipReason = "Contur cilindru: PNG generat gol (0 bytes).";
                        }
                    }
                    catch (Exception plotEx)
                    {
                        contourSkipReason = "Contur cilindru: eroare randare PNG — " + plotEx.Message;
                    }

                    try
                    {
                        deformDir = Path.Combine(contourDir, "curbe");
                        deformCurves = CylinderDeformationCurveRenderer.TryBuildPngs(
                            session, meta, contourResult, deformDir);
                    }
                    catch
                    {
                        deformCurves = null;
                    }
                }
                else
                {
                    contourSkipReason = contourResult?.Error
                        ?? CylinderContourExport.DescribeSkipReason(meta, session);
                }
            }
            catch (Exception ex)
            {
                contourPng = null;
                contourResult = null;
                contourSkipReason = "Contur cilindru omis: " + ex.Message;
            }
        }

        var overviewOk = overviewPlots.Count(p => File.Exists(p.Path));
        var perChannelOk = perChannel.Count(p => File.Exists(p.Path));
        var cwtOk = cwtPlots.Count(p => File.Exists(p.Path));
        var contourOk = !string.IsNullOrWhiteSpace(contourPng) && File.Exists(contourPng) ? 1 : 0;

        try
        {
            using var workbook = new XLWorkbook();
            // Raport first (cover) → Contur → Curbe deformare → rest.
            WriteReportSheet(workbook, session, meta, perChannel, overviewOk, cwtOk, csvPath,
                contourSheetPresent: attemptContour, contourImageOk: contourOk, contourFootnote, contourSkipReason,
                deformCurveCount: deformCurves?.AllPngs.Count ?? 0);
            if (attemptContour)
            {
                WriteContourSheet(workbook, contourPng, contourFootnote, contourResult, contourSkipReason);
                WriteDeformationCurvesSheet(workbook, session, meta, contourResult, deformCurves);
            }
            WriteMontageSheet(workbook, meta);
            WriteChannelsSheet(workbook, meta);
            WritePeakAtMarkSheet(workbook, session);
            WriteGraphSheet(workbook, session, overviewPlots);
            WriteDataSheet(workbook, session);
            if (perChannel.Count > 0)
            {
                WritePerChannelOverviewSheet(workbook, perChannel);
                WritePerChannelSheets(workbook, session, perChannel);
            }
            if (cwtPlots.Count > 0)
                WriteCwtSheet(workbook, session, cwtPlots, meta);
            workbook.SaveAs(xlsxPath);
            TrySaveExcelGraphsBeside(xlsxPath, overviewPlots, perChannel, cwtPlots, contourPng, deformCurves);
        }
        finally
        {
            CleanupTempPngs(overviewDir, overviewPlots.Select(p => p.Path), ownedOnly: overviewDir is not null);
            CleanupTempPngs(perChannelDir, perChannel.Select(p => p.Path), ownedOnly: true);
            CleanupTempPngs(cwtDir, cwtPlots.Select(p => p.Path), ownedOnly: true);
            if (contourDir is not null)
            {
                try
                {
                    if (Directory.Exists(contourDir))
                        Directory.Delete(contourDir, recursive: true);
                }
                catch { /* ignore */ }
            }
        }

        var deformOk = deformCurves?.AllPngs.Count ?? 0;
        return new ExcelExportResult
        {
            Path = xlsxPath,
            SampleCount = session.Timestamps.Count,
            ChannelCount = session.ChannelNames.Count,
            ActiveChannelCount = perChannelOk,
            ChartCount = overviewOk + perChannelOk + cwtOk + contourOk + deformOk,
            ChartChannelNames = perChannel.Select(p => p.Name).ToList(),
            ContourIncluded = contourOk > 0,
            ContourSkipReasonRo = contourOk > 0 ? null : (attemptContour ? contourSkipReason : null)
        };
    }

    private static void TrySaveExcelGraphsBeside(
        string xlsxPath,
        IReadOnlyList<(string UnitKey, string Title, string Path)> overviewPlots,
        IReadOnlyList<(string Name, string Path)> perChannel,
        IReadOnlyList<(string Name, string Path)> cwtPlots,
        string? contourPng = null,
        CylinderDeformationCurveRenderer.CurvePack? deformCurves = null)
    {
        try
        {
            var full = Path.GetFullPath(xlsxPath);
            var dir = Path.GetDirectoryName(full) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(full);
            var outDir = Path.Combine(dir, stem + "_grafice");
            Directory.CreateDirectory(outDir);
            var n = 0;
            foreach (var (_, title, path) in overviewPlots.Where(p => File.Exists(p.Path)))
            {
                var name = $"01_overview_{SanitizeFile(title)}.png";
                File.Copy(path, Path.Combine(outDir, name), overwrite: true);
                n++;
            }
            var i = 0;
            foreach (var (ch, path) in perChannel.Where(p => File.Exists(p.Path)))
            {
                i++;
                File.Copy(path, Path.Combine(outDir, $"02_yt_{i:00}_{SanitizeFile(ch)}.png"), overwrite: true);
                n++;
            }
            i = 0;
            foreach (var (ch, path) in cwtPlots.Where(p => File.Exists(p.Path)))
            {
                i++;
                File.Copy(path, Path.Combine(outDir, $"03_cwt_{i:00}_{SanitizeFile(ch)}.png"), overwrite: true);
                n++;
            }
            if (!string.IsNullOrWhiteSpace(contourPng) && File.Exists(contourPng))
            {
                File.Copy(contourPng, Path.Combine(outDir, "04_contour_cilindru.png"), overwrite: true);
                n++;
            }
            if (deformCurves is not null)
            {
                var di = 0;
                foreach (var (title, path) in deformCurves.AllPngs)
                {
                    di++;
                    File.Copy(path, Path.Combine(outDir, $"05_curbe_{di:00}_{SanitizeFile(title)}.png"), overwrite: true);
                    n++;
                }
            }
            if (n == 0)
            {
                try { Directory.Delete(outDir, false); } catch { /* ignore */ }
            }
        }
        catch { /* optional */ }
    }

    private static string SanitizeFile(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray();
        var t = new string(chars).Trim();
        if (t.Length > 48) t = t[..48];
        return string.IsNullOrWhiteSpace(t) ? "canal" : t;
    }

    private static void CleanupTempPngs(string? dir, IEnumerable<string> paths, bool ownedOnly)
    {
        if (!ownedOnly || dir is null) return;
        try
        {
            foreach (var p in paths)
            {
                try { File.Delete(p); } catch { /* ignore */ }
            }
            try { Directory.Delete(dir, false); } catch { /* ignore */ }
        }
        catch { /* ignore */ }
    }

    /// <summary>Legacy CSV→xlsx path used by older call sites; builds OfflineSession then full report.</summary>
    public static ExcelExportResult ExportFromCsv(string csvPath, string xlsxPath, ProjectMeta? meta = null)
    {
        var session = OfflineSession.FromCsv(csvPath);
        meta ??= new ProjectMeta();
        CylinderContourExport.MergeFromSession(meta, session);
        return Export(xlsxPath, session, meta, csvPath: csvPath);
    }

    private static void WriteReportSheet(
        XLWorkbook workbook,
        OfflineSession session,
        ProjectMeta meta,
        IReadOnlyList<(string Name, string Path)> perChannel,
        int overviewChartCount,
        int cwtChartCount = 0,
        string? csvPath = null,
        bool contourSheetPresent = false,
        int contourImageOk = 0,
        string? contourFootnote = null,
        string? contourSkipReason = null,
        int deformCurveCount = 0)
    {
        var ws = workbook.Worksheets.Add("Raport");
        ws.Position = 1;
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 6);
        contentRow = ExcelReportHeader.ApplyTricolorStripe(ws, contentRow, 1, 6);

        ws.Range(contentRow, 1, contentRow, 6).Merge();
        var title = ws.Cell(contentRow, 1);
        title.Value = "UPET AcqLab — Raport măsurătoare";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 18;
        title.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        title.Style.Alignment.WrapText = true;
        ws.Row(contentRow).Height = 28;

        ws.Range(contentRow + 1, 1, contentRow + 1, 6).Merge();
        ws.Cell(contentRow + 1, 1).Value = $"Generat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

        var sectionRow = contentRow + 3;
        ws.Cell(sectionRow, 1).Value = "1. Identificare experiment";
        StyleSectionTitle(ws, sectionRow, 6);

        var activeChannels = session.Columns.Count(c => ExportLabels.IsActiveColumn(c));
        var fsEf = SamplingRateInfo.EstimateEffectiveHz(session.Timestamps, meta.SampleRateHz > 0 ? meta.SampleRateHz : 50);
        var dtMean = SamplingRateInfo.MeanDeltaSeconds(session.Timestamps);
        var rateSet = string.IsNullOrWhiteSpace(meta.Backend)
            ? (meta.SampleRateHz > 0 ? $"{meta.SampleRateHz} Hz" : "")
            : $"{meta.Backend} · {meta.SampleRateHz} Hz";
        var activeCount = meta.ActiveChannelCount > 0 ? meta.ActiveChannelCount : activeChannels;
        var hasBefore = !string.IsNullOrWhiteSpace(meta.MontagePhotoPath) && File.Exists(meta.MontagePhotoPath);
        var hasAfter = !string.IsNullOrWhiteSpace(meta.MontagePhotoAfterPath) && File.Exists(meta.MontagePhotoAfterPath);
        var hasPhoto = hasBefore || hasAfter;

        var metaStart = sectionRow + 1;
        var row = metaStart;
        void Meta(string label, string? value, bool force = false)
        {
            if (!force && string.IsNullOrWhiteSpace(value)) return;
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 2).Value = value ?? "";
            ws.Range(row, 2, row, 6).Merge();
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF4");
            ws.Range(row, 1, row, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 6).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C5CED6");
            row++;
        }

        Meta("Instituție", ReportHeaderHelper.UniversityName, force: true);
        Meta("Autor soft", ReportHeaderHelper.SoftwareAuthor, force: true);
        Meta("Proiect", meta.ProjectName);
        Meta("Operator", meta.Operator);
        Meta("Sample / Probă", meta.SampleId);
        foreach (var (label, value) in Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta))
            Meta(label, value);
        // Specimen: SampleLengthMm / SampleMassG / SampleDimensionsSummary (0 = omit)
        foreach (var (label, value) in SampleDimensions.BuildReportMetaRows(meta))
            Meta(label, value);
        // Poisson ν / recuperare elastică (opțional)
        foreach (var (label, value) in Spider8DAQ.Core.Export.StrainAnalysisIndicators.BuildReportMetaRows(meta))
            Meta(label, value);
        Meta("Tip experiment", meta.ExperimentType);
        if (CylinderContourExport.ShouldAttempt(meta, session) && meta.CylinderContour is not null)
            Meta("Contur cilindru", meta.CylinderContour.ToStatusSummary());
        Meta("Preset", meta.ExperimentName);
        Meta("Locație / banc", ShortenLocation(meta.Location));
        Meta("Comentariu / obiectiv", meta.Comment);
        Meta("Start experiment", FormatExperimentStart(meta));
        Meta("Stop experiment", FormatExperimentEnd(meta));
        if (meta.EstimatedDurationMinutes > 0)
            Meta("Durată estimată [min]", meta.EstimatedDurationMinutes.ToString(CultureInfo.InvariantCulture));
        Meta("Timp înregistrare", SamplingRateInfo.FormatExperimentTime(session.Timestamps), force: true);
        Meta("Rată setată", rateSet);
        Meta("Rată efectivă",
            $"fs_ef ≈ {SamplingRateInfo.FormatHz(fsEf)} Hz · Δt_med ≈ {SamplingRateInfo.FormatDt(dtMean)} s",
            force: true);
        Meta("Canale active", activeCount > 0 ? activeCount.ToString(CultureInfo.InvariantCulture) : null);
        Meta("Senzori planificați", meta.PlannedSensors);
        Meta("Senzori / canale", meta.SensorSummary);
        Meta("Calibrare / note", meta.CalibrationNotes);
        Meta("Sursă CSV", session.SourcePath);
        Meta("Eșantioane", session.Timestamps.Count.ToString(CultureInfo.InvariantCulture), force: true);
        Meta("Canale semnal / coloane", $"{activeChannels} / {session.ChannelNames.Count}", force: true);
        Meta("Poză montaj (înainte)", hasBefore
            ? "Da — «Montaj»" + (string.IsNullOrWhiteSpace(meta.MontageBeforeCapturedLocal) ? "" : " · " + FormatCapturedLocal(meta.MontageBeforeCapturedLocal))
            : "Nu (opțional)");
        Meta("Observații montaj înainte", meta.MontageBeforeNotes);
        Meta("Poză probă (după)", hasAfter
            ? "Da — «Montaj»" + (string.IsNullOrWhiteSpace(meta.MontageAfterCapturedLocal) ? "" : " · " + FormatCapturedLocal(meta.MontageAfterCapturedLocal))
            : "Nu (opțional)");
        Meta("Observații probă după", meta.MontageAfterNotes);
        if (IsCleanDeviceEst(meta.DeviceEstHint))
            Meta("Stare dispozitiv (EST)", meta.DeviceEstHint);

        foreach (var (label, value) in IndustrialReportSections.BuildCommonMetaRows(
                     session, meta,
                     new IndustrialReportSections.Options
                     {
                         CsvPath = MeasurementFingerprint.ResolveExistingCsvPath(
                             csvPath, session.SourcePath)
                     }))
        {
            Meta(label, value, force: label.StartsWith("Disclaimer", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Zonă predare", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Calitate", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Semnătură", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Amprentă", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("Stare amprent", StringComparison.OrdinalIgnoreCase));
            if (IndustrialReportSections.IsFingerprintStatusRow(label))
            {
                var statusCell = ws.Cell(row - 1, 2);
                statusCell.Style.Font.FontColor = XLColor.FromHtml(
                    MeasurementFingerprint.StatusColorHex(value));
                statusCell.Style.Font.Bold = true;
            }
        }

        var metaLastRow = row - 1;

        // —— 2. Statistici ——
        var statsRow = metaLastRow + 2;
        ws.Cell(statsRow, 1).Value = "2. Statistici canale";
        StyleSectionTitle(ws, statsRow, 6);

        var headerRow = statsRow + 1;
        string[] headers = { "Canal", "N", "Min", "Max", "Mean", "P2P" };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        row = headerRow + 1;
        foreach (var s in session.Stats)
        {
            ws.Cell(row, 1).Value = s.Name;
            ws.Cell(row, 2).Value = s.Count;
            SetNumericOrBlank(ws.Cell(row, 3), s.Min);
            SetNumericOrBlank(ws.Cell(row, 4), s.Max);
            SetNumericOrBlank(ws.Cell(row, 5), s.Mean);
            SetNumericOrBlank(ws.Cell(row, 6), s.PeakToPeak);
            for (var c = 1; c <= 6; c++)
            {
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, c).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C5CED6");
                if (c >= 3)
                    ws.Cell(row, c).Style.NumberFormat.Format = "0.0000##";
            }
            if ((row - headerRow) % 2 == 0)
                ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F8FB");
            row++;
        }

        // —— 3. Cuprins foi ——
        var navRow = row + 2;
        ws.Cell(navRow, 1).Value = "3. Cuprins foi raport";
        StyleSectionTitle(ws, navRow, 6);

        var notes = new List<string?>
        {
            "«Raport» — identificare + statistici (această foaie, prima)",
            contourSheetPresent
                ? (contourImageOk > 0
                    ? "«Contur» — plan + elevație + secțiune + tabel uᵢ + mini F-cursă"
                      + (string.IsNullOrWhiteSpace(contourFootnote) ? "" : " · " + contourFootnote)
                    : "«Contur» — status / eroare: "
                      + (string.IsNullOrWhiteSpace(contourSkipReason) ? "graf indisponibil" : contourSkipReason))
                : null,
            deformCurveCount > 0
                ? $"«Curbe deformare» — {deformCurveCount} grafice uᵢ vs timp / cursă / forță + tabel uᵢ la Fmax"
                : null,
            hasPhoto ? "«Montaj» — poză înainte / după experiment" : null,
            meta.Channels is { Count: > 0 } ? "«Canale» — senzori și configurare pe canal" : null,
            overviewChartCount > 0 ? $"«Grafic» — overview ({overviewChartCount} panou/panouri pe unitate)" : null,
            "«Date» — eșantioane brute CSV",
            perChannel.Count > 0
                ? $"«Grafice canale» + {perChannel.Count} foi pe canal ({SessionPlotRenderer.PerChannelWidth}×{SessionPlotRenderer.PerChannelHeight})"
                : null,
            cwtChartCount > 0
                ? $"«CWT» — {cwtChartCount} scalogram(e) timp-frecvență (toată durata)"
                : null
        };
        var n = navRow + 1;
        foreach (var note in notes.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            ws.Range(n, 1, n, 6).Merge();
            ws.Cell(n, 1).Value = "• " + note;
            ws.Cell(n, 1).Style.Font.FontColor = XLColor.FromHtml("#3A4550");
            ws.Cell(n, 1).Style.Alignment.WrapText = true;
            n++;
        }

        var activeNames = SessionPlotRenderer.GetActiveChannelNames(session);
        if (activeNames.Count > 0)
        {
            ws.Range(n + 1, 1, n + 1, 6).Merge();
            ws.Cell(n + 1, 1).Value = "Canale active: " + string.Join(" · ", activeNames);
            ws.Cell(n + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(n + 1, 1).Style.Alignment.WrapText = true;
        }

        ws.Column(1).Width = 36;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 14;
        ExcelTextFit.Apply(ws, lastColumn: 6);
    }

    private static void WriteMontageSheet(XLWorkbook workbook, ProjectMeta meta)
    {
        var hasBefore = !string.IsNullOrWhiteSpace(meta.MontagePhotoPath) && File.Exists(meta.MontagePhotoPath);
        var hasAfter = !string.IsNullOrWhiteSpace(meta.MontagePhotoAfterPath) && File.Exists(meta.MontagePhotoAfterPath);
        if (!hasBefore && !hasAfter)
            return;

        var ws = workbook.Worksheets.Add("Montaj");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 8);
        ws.Range(contentRow, 1, contentRow, 8).Merge();
        ws.Cell(contentRow, 1).Value = "Documentare vizuală — înainte vs după experiment";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 16;
        ws.Cell(contentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;

        ws.Range(contentRow + 1, 1, contentRow + 1, 8).Merge();
        ws.Cell(contentRow + 1, 1).Value =
            "Stânga: montaj / schiță înainte de măsurare. Dreapta: starea probei după experiment.";
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

        // Headers
        ws.Cell(contentRow + 3, 1).Value = "ÎNAINTE de experiment";
        ws.Cell(contentRow + 3, 1).Style.Font.Bold = true;
        ws.Cell(contentRow + 3, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEAF4");
        ws.Range(contentRow + 3, 1, contentRow + 3, 4).Merge();

        ws.Cell(contentRow + 3, 5).Value = "DUPĂ experiment";
        ws.Cell(contentRow + 3, 5).Style.Font.Bold = true;
        ws.Cell(contentRow + 3, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0E6E4");
        ws.Range(contentRow + 3, 5, contentRow + 3, 8).Merge();

        var beforeStamp = FormatCapturedLocal(meta.MontageBeforeCapturedLocal);
        var afterStamp = FormatCapturedLocal(meta.MontageAfterCapturedLocal);

        ws.Range(contentRow + 4, 1, contentRow + 4, 4).Merge();
        ws.Cell(contentRow + 4, 1).Value = hasBefore
            ? $"{Path.GetFileName(meta.MontagePhotoPath)}" + (beforeStamp.Length > 0 ? $" · {beforeStamp}" : "")
            : "(fără poză — opțional)";
        ws.Cell(contentRow + 4, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");

        ws.Range(contentRow + 4, 5, contentRow + 4, 8).Merge();
        ws.Cell(contentRow + 4, 5).Value = hasAfter
            ? $"{Path.GetFileName(meta.MontagePhotoAfterPath)}" + (afterStamp.Length > 0 ? $" · {afterStamp}" : "")
            : "(fără poză — opțional)";
        ws.Cell(contentRow + 4, 5).Style.Font.FontColor = XLColor.FromHtml("#5A6570");

        ws.Range(contentRow + 5, 1, contentRow + 5, 4).Merge();
        ws.Cell(contentRow + 5, 1).Value = string.IsNullOrWhiteSpace(meta.MontageBeforeNotes)
            ? "Observații: —"
            : "Observații: " + meta.MontageBeforeNotes;
        ws.Cell(contentRow + 5, 1).Style.Alignment.WrapText = true;
        ws.Cell(contentRow + 5, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        ws.Range(contentRow + 5, 5, contentRow + 5, 8).Merge();
        ws.Cell(contentRow + 5, 5).Value = string.IsNullOrWhiteSpace(meta.MontageAfterNotes)
            ? "Observații: —"
            : "Observații: " + meta.MontageAfterNotes;
        ws.Cell(contentRow + 5, 5).Style.Alignment.WrapText = true;
        ws.Cell(contentRow + 5, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        const int maxW = 340;
        const int maxH = 360;
        var picRow = contentRow + 7;

        if (hasBefore)
        {
            try
            {
                var pic = ws.AddPicture(meta.MontagePhotoPath!).MoveTo(ws.Cell(picRow, 1));
                var scale = Math.Min(1.0, Math.Min(maxW / (double)Math.Max(1, pic.Width), maxH / (double)Math.Max(1, pic.Height)));
                pic.Scale(scale);
            }
            catch
            {
                ws.Cell(picRow, 1).Value = "Imagine înainte: eroare încărcare.";
            }
        }
        else
        {
            ws.Cell(picRow, 1).Value = "Nu a fost atașată poză înainte (opțional).";
        }

        if (hasAfter)
        {
            try
            {
                var pic = ws.AddPicture(meta.MontagePhotoAfterPath!).MoveTo(ws.Cell(picRow, 5));
                var scale = Math.Min(1.0, Math.Min(maxW / (double)Math.Max(1, pic.Width), maxH / (double)Math.Max(1, pic.Height)));
                pic.Scale(scale);
            }
            catch
            {
                ws.Cell(picRow, 5).Value = "Imagine după: eroare încărcare.";
            }
        }
        else
        {
            ws.Cell(picRow, 5).Value = "Nu a fost atașată poză după (opțional).";
        }

        for (var c = 1; c <= 8; c++)
            ws.Column(c).Width = 12;
        ExcelTextFit.Apply(ws, lastColumn: 8, sizeColumns: false);
    }

    private static string FormatCapturedLocal(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return iso;
    }

    private static void StyleSectionTitle(IXLWorksheet ws, int row, int cols)
    {
        ws.Range(row, 1, row, cols).Merge();
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 13;
        ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6EF");
        ws.Cell(row, 1).Style.Alignment.WrapText = true;
        ws.Row(row).Height = 22;
    }

    private static string? ShortenLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var t = location.Trim();
        // Keep lab reports readable — default UPET line is very long.
        if (t.Contains("Universitatea din Petroșani", StringComparison.OrdinalIgnoreCase) && t.Length > 80)
            return "Universitatea din Petroșani — Lab / banc (vezi proiect)";
        return t;
    }

    private static bool IsCleanDeviceEst(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint) || hint == "EST: —") return false;
        // Exclude status/journal lines accidentally stored in DeviceEstHint.
        if (hint.Contains("Experiment ", StringComparison.OrdinalIgnoreCase)) return false;
        if (hint.Contains("foto montaj", StringComparison.OrdinalIgnoreCase)) return false;
        return hint.Length <= 160;
    }

    private static void WriteChannelsSheet(XLWorkbook workbook, ProjectMeta meta)
    {
        var channels = meta.Channels ?? new List<ChannelReportInfo>();
        if (channels.Count == 0) return;

        var ws = workbook.Worksheets.Add("Canale");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 16);
        ws.Range(contentRow, 1, contentRow, 16).Merge();
        ws.Cell(contentRow, 1).Value =
            $"Canale active în experiment: {meta.ActiveChannelCount} — senzori și detalii de configurare";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 14;
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;

        var headerRow = contentRow + 2;
        string[] headers =
        {
            "CH#", "Canal", "Unitate", "Senzor", "Cod", "Categorie", "Tip", "Punte",
            "Capacitate", "Sensibilitate", "Scale", "Offset", "Zero", "Uexc [V]",
            "Range [mV/V]", "Filtru [Hz]", "fs canal", "Shunt [kΩ]", "Note senzor"
        };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        ws.Row(headerRow).Height = 32;

        var row = headerRow + 1;
        foreach (var ch in channels)
        {
            ws.Cell(row, 1).Value = ch.Index;
            ws.Cell(row, 2).Value = ch.Name;
            ws.Cell(row, 3).Value = ch.Unit;
            ws.Cell(row, 4).Value = ch.SensorDisplay;
            ws.Cell(row, 5).Value = ch.SensorCode ?? "";
            ws.Cell(row, 6).Value = ch.SensorCategory ?? "";
            ws.Cell(row, 7).Value = ch.TransducerType ?? (ch.IsMathChannel ? "Math" : "");
            ws.Cell(row, 8).Value = ch.Bridge;
            ws.Cell(row, 9).Value = ch.Capacity > 0 ? ch.Capacity : Blank.Value;
            ws.Cell(row, 10).Value = ch.Sensitivity > 0 ? ch.Sensitivity : Blank.Value;
            ws.Cell(row, 11).Value = ch.Scale;
            ws.Cell(row, 12).Value = ch.Offset;
            ws.Cell(row, 13).Value = ch.TareValue;
            ws.Cell(row, 14).Value = ch.ExcitationV > 0 ? ch.ExcitationV : Blank.Value;
            ws.Cell(row, 15).Value = ch.RangeMvPerV > 0 ? ch.RangeMvPerV : Blank.Value;
            ws.Cell(row, 16).Value = ch.FilterHz > 0 ? ch.FilterHz : Blank.Value;
            ws.Cell(row, 17).Value = ch.ChannelSampleRateHz > 0 ? ch.ChannelSampleRateHz : Blank.Value;
            ws.Cell(row, 18).Value = ch.ShuntKohm > 0 ? ch.ShuntKohm : Blank.Value;
            ws.Cell(row, 19).Value = ch.SensorNotes ?? (ch.IsMathChannel ? ch.MathExpression : "") ?? "";
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            ws.Cell(row, 4).Style.Alignment.WrapText = true;
            ws.Cell(row, 19).Style.Alignment.WrapText = true;
            ws.Cell(row, 19).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            for (var c = 1; c <= 19; c++)
            {
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, c).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C5CED6");
            }
            if ((row - headerRow) % 2 == 0)
                ws.Range(row, 1, row, 19).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F8FB");
            for (var c = 9; c <= 18; c++)
                ws.Cell(row, c).Style.NumberFormat.Format = "0.######";
            row++;
        }

        ws.SheetView.FreezeRows(headerRow);
        ExcelTextFit.Apply(ws, lastColumn: 19);
    }

    private static void WriteGraphSheet(
        XLWorkbook workbook,
        OfflineSession session,
        IReadOnlyList<(string UnitKey, string Title, string Path)> overviewPlots)
    {
        var ws = workbook.Worksheets.Add("Grafic");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 10);

        ws.Range(contentRow, 1, contentRow, 10).Merge();
        var title = ws.Cell(contentRow, 1);
        title.Value = overviewPlots.Count > 1
            ? "Overview — panouri separate pe unitate (fără scară Y comună N/bar)"
            : "Overview măsurătoare";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 16;
        title.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        title.Style.Alignment.WrapText = true;
        ws.Row(contentRow).Height = 24;

        var activeNames = SessionPlotRenderer.GetActiveChannelNames(session);
        var experimentTime = SamplingRateInfo.FormatExperimentTime(session.Timestamps);

        ws.Range(contentRow + 1, 1, contentRow + 1, 10).Merge();
        ws.Cell(contentRow + 1, 1).Value =
            $"Timp experiment: {experimentTime} · {session.Timestamps.Count:N0} eșantioane · " +
            $"{activeNames.Count} canale active · {overviewPlots.Count} panou/panouri overview";
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

        if (activeNames.Count > 0)
        {
            ws.Range(contentRow + 2, 1, contentRow + 2, 10).Merge();
            ws.Cell(contentRow + 2, 1).Value = string.Join(" · ", activeNames);
            ws.Cell(contentRow + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(contentRow + 2, 1).Style.Alignment.WrapText = true;
        }

        ws.Range(contentRow + 3, 1, contentRow + 3, 10).Merge();
        ws.Cell(contentRow + 3, 1).Value =
            "Livrabil principal: câte o foaie per canal (scară Y proprie). " +
            "Aici: overview grupat pe unitate. Date: foaia «Date».";
        ws.Cell(contentRow + 3, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 3, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 3, 1).Style.Alignment.WrapText = true;

        var row = contentRow + 5;
        foreach (var (unitKey, panelTitle, pngPath) in overviewPlots)
        {
            if (!File.Exists(pngPath)) continue;
            ws.Range(row, 1, row, 10).Merge();
            ws.Cell(row, 1).Value = string.IsNullOrEmpty(unitKey)
                ? panelTitle
                : $"{panelTitle} — scară Y doar [{unitKey}]";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 12;
            ws.AddPicture(pngPath)
                .MoveTo(ws.Cell(row + 1, 1))
                .Scale(0.88);
            ws.Row(row + 1).Height = 420;
            row += 28;
        }

        ws.Column(1).Width = 18;
        for (var c = 2; c <= 10; c++)
            ws.Column(c).Width = 12;
        ExcelTextFit.Apply(ws, lastColumn: 10, sizeColumns: false);
    }

    private static void WritePerChannelOverviewSheet(XLWorkbook workbook, IReadOnlyList<(string Name, string Path)> plots)
    {
        var ws = workbook.Worksheets.Add("Grafice canale");
        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 6);
        ws.Cell(contentRow, 1).Value =
            $"Grafice Y(t) per canal — {plots.Count} canale · PNG {SessionPlotRenderer.PerChannelWidth}×{SessionPlotRenderer.PerChannelHeight} · scară proprie";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 13;
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
        ws.Range(contentRow, 1, contentRow, 6).Merge();
        ws.Range(contentRow + 1, 1, contentRow + 1, 6).Merge();
        ws.Cell(contentRow + 1, 1).Value =
            "Fiecare canal are foaie proprie (nume = canal). Mai jos: previzualizare stivuită (aceleași PNG-uri).";
        ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

        var row = contentRow + 3;
        foreach (var (name, path) in plots)
        {
            if (!File.Exists(path)) continue;
            ws.Cell(row, 1).Value = name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            // ~0.72 keeps 1000×500 readable while stacking several on one sheet
            ws.AddPicture(path).MoveTo(ws.Cell(row + 1, 1)).Scale(0.72);
            row += 26;
        }
        ws.Column(1).Width = 24;
        ExcelTextFit.Apply(ws, lastColumn: 6, sizeColumns: false);
    }

    private static void WriteCwtSheet(
        XLWorkbook workbook,
        OfflineSession session,
        IReadOnlyList<(string Name, string Path)> cwtPlots,
        ProjectMeta meta)
    {
        var ws = workbook.Worksheets.Add("CWT");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;

        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 10);
        ws.Range(contentRow, 1, contentRow, 10).Merge();
        ws.Cell(contentRow, 1).Value = "Analiză timp-frecvență (CWT) — wavelet Morlet";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 16;
        ws.Cell(contentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
        ws.Row(contentRow).Height = 24;

        var fs = meta.SampleRateHz > 0
            ? meta.SampleRateHz
            : CwtPlotRenderer.EstimateSampleRate(session, 50);
        ws.Range(contentRow + 1, 1, contentRow + 1, 10).Merge();
        ws.Cell(contentRow + 1, 1).Value =
            $"Scalogramă |W(t,f)| · Timp [s] × Frecvență [Hz] · {cwtPlots.Count} canal(e) active · " +
            $"toată durata înregistrării · fs≈{fs:0.##} Hz · culoare = amplitudine CWT";
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

        var row = contentRow + 3;
        foreach (var (name, path) in cwtPlots)
        {
            if (!File.Exists(path)) continue;
            ws.Range(row, 1, row, 10).Merge();
            ws.Cell(row, 1).Value = $"CWT — {name}";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 12;
            // 1920×1080 PNG — scale ~0.55 keeps ~1050 px wide on sheet without soft Stretch
            ws.AddPicture(path)
                .MoveTo(ws.Cell(row + 1, 1))
                .Scale(0.55);
            ws.Row(row + 1).Height = 420;
            row += 30;
        }

        ws.Column(1).Width = 18;
        for (var c = 2; c <= 10; c++)
            ws.Column(c).Width = 12;
        ExcelTextFit.Apply(ws, lastColumn: 10, sizeColumns: false);
    }

    private static void WriteContourSheet(
        XLWorkbook workbook,
        string? pngPath,
        string? footnote,
        CylinderContourResult? result,
        string? skipReason = null)
    {
        var ws = workbook.Worksheets.Add("Contur");
        // After Raport (cover) — position 2.
        ws.Position = 2;
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;
        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 8);
        ws.Range(contentRow, 1, contentRow, 8).Merge();
        ws.Cell(contentRow, 1).Value = "Contur cilindru — plan + elevație + secțiune (R0 + Contur Fmax · S1…Sn · u_i)";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 16;
        ws.Cell(contentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
        ws.Row(contentRow).Height = 26;

        ws.Range(contentRow + 1, 1, contentRow + 1, 8).Merge();
        ws.Cell(contentRow + 1, 1).Value = AppendBarrelMapFootnote(footnote);
        ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;
        ws.Cell(contentRow + 1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var row = contentRow + 3;
        var hasImage = !string.IsNullOrWhiteSpace(pngPath) && File.Exists(pngPath);

        // Status row — always visible (OK or error).
        ws.Cell(row, 1).Value = "Status";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Range(row, 2, row, 8).Merge();
        if (hasImage && result is { IsValid: true })
        {
            ws.Cell(row, 2).Value = "OK — grafic Contur inclus (plan + elevație + secțiune)";
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#1B6B3A");
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
        }
        else
        {
            var err = string.IsNullOrWhiteSpace(skipReason)
                ? (result?.Error ?? "Contur indisponibil — verificați ContourConfig, tip experiment și Ø.")
                : skipReason;
            ws.Cell(row, 2).Value = err;
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#A04000");
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
        row += 2;

        if (result is { IsValid: true })
        {
            ws.Cell(row, 1).Value = "Indicator";
            ws.Cell(row, 2).Value = "Valoare";
            ws.Range(row, 1, row, 2).Style.Font.Bold = true;
            ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            ws.Range(row, 1, row, 2).Style.Font.FontColor = XLColor.White;
            row++;
            foreach (var (label, value) in result.BuildReportRows())
            {
                ws.Cell(row, 1).Value = label;
                ws.Cell(row, 2).Value = value;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Alignment.WrapText = true;
                ws.Range(row, 2, row, 8).Merge();
                ws.Cell(row, 2).Style.Alignment.WrapText = true;
                ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                row++;
            }
            row += 1;
        }
        else if (!string.IsNullOrWhiteSpace(skipReason) || result?.Error is not null)
        {
            ws.Cell(row, 1).Value = "Detaliu";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 2, row, 8).Merge();
            ws.Cell(row, 2).Value = skipReason ?? result?.Error ?? "";
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            row += 2;
        }

        if (hasImage)
        {
            try
            {
                // High-res Contur PNG (2400x1600) — keep ~1100 px display width in sheet.
                ws.AddPicture(pngPath!).MoveTo(ws.Cell(row, 1))
                    .Scale(CylinderContourPlotRenderer.ExcelEmbedScale);
                ws.Row(row).Height = 780;
            }
            catch (Exception ex)
            {
                ws.Cell(row, 1).Value = "Eroare inserare imagine Contur: " + ex.Message;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#A04000");
            }
        }
        else
        {
            ws.Range(row, 1, row, 8).Merge();
            ws.Cell(row, 1).Value =
                "Imaginea Contur lipsește pe această foaie. Corectați Status-ul de mai sus " +
                "(Ø, mapping senzori S1…Sn, canal cursă) și re-exportați Excel.";
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
        }

        ws.Column(1).Width = 38;
        ws.Column(2).Width = 24;
        for (var c = 3; c <= 8; c++)
            ws.Column(c).Width = 12;
        // Keep Contur PNG row/scale unchanged — only wrap + row-height for text.
        ExcelTextFit.Apply(ws, lastColumn: 8, sizeColumns: false);
    }

    /// <summary>
    /// Excel Contur footnote: industrial schema plus barrel-map honesty (not a 3D scan).
    /// </summary>
    private static string AppendBarrelMapFootnote(string? footnote)
    {
        var extra = CylinderContourPlotRenderer.BarrelMapLegendLine1 + " "
            + CylinderContourPlotRenderer.BarrelMapLegendLine2;
        var baseFn = string.IsNullOrWhiteSpace(footnote)
            ? "Schemă industrială: plan + elevație + secțiune · Contur Fmax · u_max · tabel u_i · mini F-cursă."
            : footnote.Trim();
        if (baseFn.Contains(CylinderContourPlotRenderer.BarrelMapLegendLine1, StringComparison.Ordinal)
            || baseFn.Contains(CylinderContourPlotRenderer.BarrelMapLegendLine2, StringComparison.Ordinal))
            return baseFn;
        return baseFn.TrimEnd('.') + ". " + extra;
    }

    /// <summary>
    /// Detailed sensor deformation curves: uᵢ vs t / cursă / F, mean curves, table uᵢ at Fmax.
    /// Placed immediately after Contur (position 3).
    /// </summary>
    private static void WriteDeformationCurvesSheet(
        XLWorkbook workbook,
        OfflineSession session,
        ProjectMeta meta,
        CylinderContourResult? result,
        CylinderDeformationCurveRenderer.CurvePack? curves)
    {
        var ws = workbook.Worksheets.Add("Curbe deformare");
        ws.Position = 3;
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;
        var contentRow = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 10);
        ws.Range(contentRow, 1, contentRow, 10).Merge();
        ws.Cell(contentRow, 1).Value =
            "Curbe deformare — u₁…uₙ pe senzorii radiali (vs timp, cursă, forță)";
        ws.Cell(contentRow, 1).Style.Font.Bold = true;
        ws.Cell(contentRow, 1).Style.Font.FontSize = 16;
        ws.Cell(contentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
        ws.Row(contentRow).Height = 26;

        ws.Range(contentRow + 1, 1, contentRow + 1, 10).Merge();
        ws.Cell(contentRow + 1, 1).Value =
            "Grafice multi-serie din canalele S1…Sn. Linia verticală roșie marchează indexul Contur / Fmax. " +
            "Schema spațială rămâne pe foaia «Contur».";
        ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
        ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;
        ws.Cell(contentRow + 1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var row = contentRow + 3;
        if (result is { IsValid: true })
        {
            ws.Cell(row, 1).Value = "uᵢ la Fmax / index contur";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 13;
            row += 1;

            ws.Cell(row, 1).Value = "Senzor";
            ws.Cell(row, 2).Value = "Canal";
            ws.Cell(row, 3).Value = "Unghi [°]";
            ws.Cell(row, 4).Value = "uᵢ [mm]";
            ws.Cell(row, 5).Value = "R = R₀+uᵢ [mm]";
            ws.Range(row, 1, row, 5).Style.Font.Bold = true;
            ws.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            ws.Range(row, 1, row, 5).Style.Font.FontColor = XLColor.White;
            row++;
            var inv = CultureInfo.InvariantCulture;
            foreach (var s in result.Sensors)
            {
                ws.Cell(row, 1).Value = $"S{s.SensorIndex}";
                ws.Cell(row, 2).Value = $"CH{s.ChannelIndex}";
                ws.Cell(row, 3).Value = s.AngleDeg;
                ws.Cell(row, 3).Style.NumberFormat.Format = "0.#";
                ws.Cell(row, 4).Value = s.RadialDisplacementMm;
                ws.Cell(row, 4).Style.NumberFormat.Format = "0.0000##";
                ws.Cell(row, 5).Value = s.RadiusMm;
                ws.Cell(row, 5).Style.NumberFormat.Format = "0.0000##";
                row++;
            }
            row++;
            ws.Cell(row, 1).Value = "Ovalitate [mm]";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = result.OvalityMm;
            ws.Cell(row, 2).Style.NumberFormat.Format = "0.0000##";
            row++;
            ws.Cell(row, 1).Value = "Index de bombare [-]";
            ws.Cell(row, 1).Style.Font.Bold = true;
            if (double.IsFinite(result.BarrelingIndex))
            {
                ws.Cell(row, 2).Value = result.BarrelingIndex;
                ws.Cell(row, 2).Style.NumberFormat.Format = "0.0000##";
            }
            else
            {
                ws.Cell(row, 2).Value = "—";
            }
            row++;
            ws.Cell(row, 1).Value = "Index contur";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 2, row, 8).Merge();
            ws.Cell(row, 2).Value =
                $"idx={result.SampleIndex} · {result.IndexRuleFootnoteRo}";
            ws.Cell(row, 2).Style.Alignment.WrapText = true;
            row += 2;
        }
        else
        {
            ws.Range(row, 1, row, 10).Merge();
            ws.Cell(row, 1).Value =
                "Fără rezultat Contur valid — curbele de deformare nu pot fi generate. " +
                "Verificați Ø, mapping S1…Sn și canalul de cursă, apoi re-exportați.";
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#A04000");
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
            row += 2;
        }

        var pngs = curves?.AllPngs ?? Array.Empty<(string, string)>();
        if (pngs.Count == 0)
        {
            ws.Range(row, 1, row, 10).Merge();
            ws.Cell(row, 1).Value =
                "Niciun grafic de curbă generat (lipsă canale senzor / eroare randare).";
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
        }
        else
        {
            foreach (var (title, path) in pngs)
            {
                if (!File.Exists(path)) continue;
                ws.Range(row, 1, row, 10).Merge();
                ws.Cell(row, 1).Value = title;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 12;
                ws.Row(row).Height = 18;
                try
                {
                    // 1600×900 → ~0.62 ≈ 990 px wide
                    ws.AddPicture(path).MoveTo(ws.Cell(row + 1, 1)).Scale(0.62);
                    ws.Row(row + 1).Height = 380;
                    row += 24;
                }
                catch (Exception ex)
                {
                    ws.Cell(row + 1, 1).Value = "Eroare inserare: " + ex.Message;
                    ws.Cell(row + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#A04000");
                    row += 3;
                }
            }
        }

        ws.Column(1).Width = 22;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 18;
        for (var c = 6; c <= 10; c++)
            ws.Column(c).Width = 12;
        ExcelTextFit.Apply(ws, lastColumn: 10, sizeColumns: false);
        _ = session;
        _ = meta;
    }

    /// <summary>One worksheet per active channel with embedded ScottPlot PNG (standalone, data-scaled).</summary>
    private static void WritePerChannelSheets(
        XLWorkbook workbook,
        OfflineSession session,
        IReadOnlyList<(string Name, string Path)> plots)
    {
        var used = new HashSet<string>(ReservedSheetNames, StringComparer.OrdinalIgnoreCase);
        var useIndex = session.Timestamps.Count < 2
            || (session.Timestamps[^1] - session.Timestamps[0]).TotalSeconds <= 1e-9;

        foreach (var (name, path) in plots)
        {
            if (!File.Exists(path)) continue;
            var sheetName = UniqueSheetName(name, used);
            used.Add(sheetName);

            var ws = workbook.Worksheets.Add(sheetName);
            ws.Style.Font.FontName = "Calibri";
            ws.Style.Font.FontSize = 11;

            var contentRow = ExcelReportHeader.ApplyCompact(ws, startRow: 1, mergeCols: 8);

            var unit = ExportLabels.ParseUnit(name);
            ws.Range(contentRow, 1, contentRow, 8).Merge();
            ws.Cell(contentRow, 1).Value = string.IsNullOrWhiteSpace(unit) ? name : $"{name}";
            ws.Cell(contentRow, 1).Style.Font.Bold = true;
            ws.Cell(contentRow, 1).Style.Font.FontSize = 14;
            ws.Cell(contentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#1B2430");
            ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
            ws.Row(contentRow).Height = 22;

            ws.Range(contentRow + 1, 1, contentRow + 1, 8).Merge();
            var xAxis = useIndex ? "index eșantion" : "t [s]";
            ws.Cell(contentRow + 1, 1).Value = string.IsNullOrWhiteSpace(unit)
                ? $"Y(t) · axa X = {xAxis} · scară Y = min/max canal ±{SessionPlotRenderer.AxisPadFraction:P0}"
                : $"Y(t) · Y [{unit}] · axa X = {xAxis} · scară Y doar pe acest canal (±{SessionPlotRenderer.AxisPadFraction:P0})";
            ws.Cell(contentRow + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(contentRow + 1, 1).Style.Font.Italic = true;
            ws.Cell(contentRow + 1, 1).Style.Alignment.WrapText = true;

            ws.Range(contentRow + 2, 1, contentRow + 2, 8).Merge();
            ws.Cell(contentRow + 2, 1).Value =
                $"Imagine {SessionPlotRenderer.PerChannelWidth}×{SessionPlotRenderer.PerChannelHeight} px — semnal constant: padding absolut pe |Y|.";
            ws.Cell(contentRow + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6570");
            ws.Cell(contentRow + 2, 1).Style.Font.FontSize = 10;
            ws.Cell(contentRow + 2, 1).Style.Alignment.WrapText = true;

            var plotRow = contentRow + 4;
            ws.AddPicture(path)
                .MoveTo(ws.Cell(plotRow, 1))
                .Scale(0.95);
            ws.Row(plotRow).Height = 380;
            ws.Column(1).Width = 18;
            for (var c = 2; c <= 8; c++)
                ws.Column(c).Width = 12;
            ExcelTextFit.Apply(ws, lastColumn: 8, sizeColumns: false);
        }
    }

    internal static string UniqueSheetName(string channelName, HashSet<string> used)
    {
        var baseName = SanitizeSheetName(channelName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Canal";

        if (!used.Contains(baseName))
            return baseName;

        for (var i = 2; i < 1000; i++)
        {
            var suffix = $" ({i})";
            var maxBase = Math.Max(1, 31 - suffix.Length);
            var candidate = (baseName.Length > maxBase ? baseName[..maxBase] : baseName) + suffix;
            if (!used.Contains(candidate))
                return candidate;
        }

        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>Excel sheet name: max 31 chars, no \ / ? * [ ].</summary>
    internal static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Canal";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (ch is '[' or ']')
                continue;
            if (ch is '\\' or '/' or '?' or '*' or ':' or '\'')
                sb.Append('-');
            else if (ch < 32)
                continue;
            else
                sb.Append(ch);
        }
        var s = sb.ToString().Trim();
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        if (s.Length == 0) s = "Canal";
        if (s.Length > 31) s = s[..31].TrimEnd();
        return s;
    }

    
    private static void WritePeakAtMarkSheet(XLWorkbook workbook, OfflineSession session)
    {
        try
        {
            var path = session.SourcePath ?? "";
            if (string.IsNullOrWhiteSpace(path) || path.Contains('#') || !System.IO.File.Exists(path))
                return;
            var marks = PeakAtMark.ParseMarksFromCsvLines(CsvSharedIO.EnumerateLines(path));
            if (marks.Count == 0) return;
            var rows = PeakAtMark.Compute(session, marks);
            if (rows.Count == 0) return;

            var ws = workbook.Worksheets.Add("Peak la MARK");
            var contentRow = ExcelReportHeader.ApplyCompact(ws, startRow: 1, mergeCols: 8);
            ws.Cell(contentRow, 1).Value = "Peak-at-MARK — valori la timestamp MARK (+ peak în fereastră ±5 eșantioane)";
            ws.Cell(contentRow, 1).Style.Font.Bold = true;
            ws.Cell(contentRow, 1).Style.Font.FontSize = 13;
            ws.Cell(contentRow, 1).Style.Alignment.WrapText = true;
            ws.Range(contentRow, 1, contentRow, 8).Merge();

            var headerRow = contentRow + 2;
            ws.Cell(headerRow, 1).Value = "UTC MARK";
            ws.Cell(headerRow, 2).Value = "Etichetă";
            ws.Cell(headerRow, 3).Value = "Index";
            for (var c = 0; c < session.ChannelNames.Count; c++)
            {
                ws.Cell(headerRow, 4 + c * 2).Value = session.ChannelNames[c] + " @MARK";
                ws.Cell(headerRow, 5 + c * 2).Value = session.ChannelNames[c] + " peak";
            }
            var header = ws.Range(headerRow, 1, headerRow, Math.Max(3, 3 + session.ChannelNames.Count * 2));
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Alignment.WrapText = true;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(headerRow).Height = 32;

            var r = headerRow + 1;
            foreach (var row in rows)
            {
                ws.Cell(r, 1).Value = row.MarkUtc.ToString("O");
                ws.Cell(r, 2).Value = row.Label;
                ws.Cell(r, 3).Value = row.SampleIndex;
                for (var c = 0; c < row.Values.Length; c++)
                {
                    var v = row.Values[c];
                    var p = c < row.PeakAbsInWindow.Length ? row.PeakAbsInWindow[c] : double.NaN;
                    if (!double.IsNaN(v)) ws.Cell(r, 4 + c * 2).Value = v;
                    if (!double.IsNaN(p)) ws.Cell(r, 5 + c * 2).Value = p;
                }
                r++;
            }
            ws.Columns().AdjustToContents(headerRow, Math.Min(r, 50));
            var lastCol = Math.Max(3, 3 + session.ChannelNames.Count * 2);
            ExcelTextFit.CapColumnWidths(ws, 1, lastCol);
            ExcelTextFit.Apply(ws, lastColumn: lastCol, lastRow: Math.Min(r, 80), sizeColumns: false);
        }
        catch { /* non-fatal */ }
    }
private static void SetNumericOrBlank(IXLCell cell, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            cell.Value = Blank.Value;
        else
            cell.Value = value;
    }

    private static string FormatExperimentStart(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentStartLocal)) return "";
        if (DateTime.TryParse(meta.ExperimentStartLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return meta.ExperimentStartLocal;
    }

    private static string FormatExperimentEnd(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentEndLocal)) return "";
        if (DateTime.TryParse(meta.ExperimentEndLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return meta.ExperimentEndLocal;
    }

    private static void WriteDataSheet(XLWorkbook workbook, OfflineSession session)
    {
        var ws = workbook.Worksheets.Add("Date");
        var colCount = Math.Max(3, session.ChannelNames.Count + 3);
        // Compact antet above column headers — does not change one-sample-per-row layout.
        var headerRow = ExcelReportHeader.ApplyCompact(ws, startRow: 1, mergeCols: Math.Max(colCount, 6));
        ws.Cell(headerRow, 1).Value = "Timp";
        ws.Cell(headerRow, 2).Value = "Sequence";
        ws.Cell(headerRow, 3).Value = SamplingRateInfo.RelativeTimeHeaderExcel;
        for (var c = 0; c < session.ChannelNames.Count; c++)
            ws.Cell(headerRow, c + 4).Value = session.ChannelNames[c];

        var header = ws.Range(headerRow, 1, headerRow, colCount);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1B2430");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Alignment.WrapText = true;

        var n = session.Timestamps.Count;
        var rowsToWrite = Math.Min(n, ExcelMaxDataRows);
        var dataStartRow = headerRow + 1;
        var tRel = SamplingRateInfo.RelativeSeconds(session.Timestamps);

        // Write one sample per row across columns. Do NOT use InsertData(object[,]):
        // ClosedXML enumerates multidimensional arrays as a flat IEnumerable, so each
        // field becomes its own row in column A.
        for (var i = 0; i < rowsToWrite; i++)
        {
            var row = dataStartRow + i;
            ws.Cell(row, 1).Value = AppClock.FormatTimeOnly(session.Timestamps[i]);
            ws.Cell(row, 2).Value = session.Sequences[i];
            SetNumericOrBlank(ws.Cell(row, 3), i < tRel.Length ? tRel[i] : double.NaN);
            for (var c = 0; c < session.Columns.Count; c++)
            {
                var v = i < session.Columns[c].Length ? session.Columns[c][i] : double.NaN;
                SetNumericOrBlank(ws.Cell(row, c + 4), v);
            }
        }

        var noteRow = dataStartRow + rowsToWrite + 1;
        var fsEf = SamplingRateInfo.EstimateEffectiveHz(session.Timestamps, 50);
        var dtMean = SamplingRateInfo.MeanDeltaSeconds(session.Timestamps);
        if (n > rowsToWrite)
        {
            ws.Cell(noteRow, 1).Value =
                $"Notă: Excel permite max. {ExcelMaxDataRows:N0} rânduri de date. " +
                $"Înregistrarea are {n:N0} eșantioane — folosiți CSV sursă pentru rest: {session.SourcePath}. " +
                $"Coloana Timp = HH:mm:ss.fff (ora PC). t [s] = relativ (t0=0). " +
                $"Data/durata: foaia Raport → Timp experiment. fs_ef≈{SamplingRateInfo.FormatHz(fsEf)} Hz · Δt_med≈{SamplingRateInfo.FormatDt(dtMean)} s.";
            ws.Cell(noteRow, 1).Style.Font.Italic = true;
            ws.Cell(noteRow, 1).Style.Font.FontColor = XLColor.FromHtml("#A04000");
        }
        else
        {
            ws.Cell(noteRow, 1).Value =
                $"Toate cele {n:N0} eșantioane exportate ({session.ChannelNames.Count} coloane canale). " +
                $"Coloana Timp = HH:mm:ss.fff (ora PC). t [s] = relativ (t0=0). " +
                $"Data/durata: foaia Raport → Timp experiment. " +
                $"fs_ef≈{SamplingRateInfo.FormatHz(fsEf)} Hz · Δt_med≈{SamplingRateInfo.FormatDt(dtMean)} s.";
            ws.Cell(noteRow, 1).Style.Font.Italic = true;
        }

        ws.Range(noteRow, 1, noteRow, Math.Min(colCount, 8)).Merge();
        ws.Cell(noteRow, 1).Style.Alignment.WrapText = true;
        ws.Cell(noteRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        ws.SheetView.FreezeRows(headerRow);
        // Never AdjustToContents / ExcelTextFit.Apply on the sample grid — 10k rows × N
        // channels freezes the UI (ClosedXML measures every cell). Widths from headers only.
        ApplyDataSheetColumnWidths(ws, session, colCount);
        ExcelTextFit.FitRowHeight(ws, 1, Math.Min(colCount, 8));
        ExcelTextFit.FitRowHeight(ws, headerRow, colCount);
        ExcelTextFit.FitRowHeight(ws, noteRow, Math.Min(colCount, 8));
    }

    private static void ApplyDataSheetColumnWidths(IXLWorksheet ws, OfflineSession session, int colCount)
    {
        ws.Column(1).Width = 16; // HH:mm:ss.fff
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        for (var c = 0; c < session.ChannelNames.Count; c++)
        {
            var name = session.ChannelNames[c] ?? "";
            var w = ExcelTextFit.EstimateColumnWidth(name, 11, bold: true);
            if (w < 10) w = 10;
            if (w > 28) w = 28;
            ws.Column(c + 4).Width = w;
        }

        for (var c = session.ChannelNames.Count + 4; c <= colCount; c++)
        {
            if (ws.Column(c).Width < 10)
                ws.Column(c).Width = 10;
        }
    }
}
