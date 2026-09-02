using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Professional multi-page PDF report:
/// page 1 = header + metadata + statistics + Y(t);
/// following pages = CWT scalograms (computed offline at export).
/// Hand-rolled PDF 1.4 (Helvetica) — no external PDF package.
/// </summary>
public static class PdfReportExporter
{
    // Letter page, ~18 mm margins
    private const double PageW = 612;
    private const double PageH = 792;
    private const double Margin = 50;
    private const double ContentW = PageW - 2 * Margin;

    /// <summary>
    /// Export PDF. <paramref name="jpegImagePaths"/> = overview Y(t) JPEGs (full duration);
    /// <paramref name="channelYtJpegPaths"/> = one Y(t) JPEG per active channel (full duration);
    /// <paramref name="cwtJpegImagePaths"/> = CWT scalogram JPEGs (full duration).
    /// Missing / bad images are skipped — export still succeeds.
    /// </summary>
    public static void Export(
        string path,
        OfflineSession session,
        ProjectMeta meta,
        IEnumerable<ChannelStats> stats,
        IEnumerable<string>? jpegImagePaths = null,
        IEnumerable<string>? cwtJpegImagePaths = null,
        IEnumerable<string>? channelYtJpegPaths = null,
        string? csvPath = null)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var statsList = stats?.ToList() ?? session.Stats?.ToList() ?? new List<ChannelStats>();

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

        foreach (var imgPath in (jpegImagePaths ?? Array.Empty<string>()).Where(File.Exists))
        {
            try
            {
                var bytes = File.ReadAllBytes(imgPath);
                if (!IsJpeg(bytes)) continue;
                var (w, h) = ReadJpegSize(bytes);
                images.Add((bytes, w, h, "yt"));
            }
            catch { /* skip bad plot */ }
        }

        foreach (var imgPath in (channelYtJpegPaths ?? Array.Empty<string>()).Where(File.Exists))
        {
            try
            {
                var bytes = File.ReadAllBytes(imgPath);
                if (!IsJpeg(bytes)) continue;
                var (w, h) = ReadJpegSize(bytes);
                images.Add((bytes, w, h, "yt_ch"));
            }
            catch { /* skip */ }
        }

        foreach (var imgPath in (cwtJpegImagePaths ?? Array.Empty<string>()).Where(File.Exists))
        {
            try
            {
                var bytes = File.ReadAllBytes(imgPath);
                if (!IsJpeg(bytes)) continue;
                var (w, h) = ReadJpegSize(bytes);
                images.Add((bytes, w, h, "cwt"));
            }
            catch { /* skip bad CWT */ }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(meta.MontagePhotoPath))
            {
                var montageJpeg = ReportHeaderHelper.TryImageFileToJpegBytes(meta.MontagePhotoPath, 1600);
                if (montageJpeg is { Length: > 0 } && IsJpeg(montageJpeg))
                {
                    var (mw, mh) = ReadJpegSize(montageJpeg);
                    images.Add((montageJpeg, mw, mh, "montage_before"));
                }
            }
            if (!string.IsNullOrWhiteSpace(meta.MontagePhotoAfterPath))
            {
                var afterJpeg = ReportHeaderHelper.TryImageFileToJpegBytes(meta.MontagePhotoAfterPath, 1600);
                if (afterJpeg is { Length: > 0 } && IsJpeg(afterJpeg))
                {
                    var (mw, mh) = ReadJpegSize(afterJpeg);
                    images.Add((afterJpeg, mw, mh, "montage_after"));
                }
            }
        }
        catch { /* montage optional */ }

        var logoCount = images.Count(i => i.Role == "logo") > 0 ? 1 : 0;
        var pageContents = BuildAllPages(session, meta, statsList, images, logoCount, csvPath);

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

        // pagesId = after all page objects
        var pagesId = offsets.Count + 1 + contentIds.Count;
        var catalogId = pagesId + 1;

        var pageIds = new List<int>();
        for (var p = 0; p < contentIds.Count; p++)
        {
            var pageId = ObjStart();
            pageIds.Add(pageId);
            W($"{pageId} 0 obj\n<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {PageW:0} {PageH:0}] /Contents {contentIds[p]} 0 R /Resources << /Font << /F1 {fontReg} 0 R /F2 {fontBold} 0 R >> /XObject <<{xobjects} >> >> >>\nendobj\n");
        }

        ObjStart(); // pagesId
        var kids = string.Join(" ", pageIds.Select(id => $"{id} 0 R"));
        W($"{pagesId} 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageIds.Count} >>\nendobj\n");

        ObjStart(); // catalogId
        W($"{catalogId} 0 obj\n<< /Type /Catalog /Pages {pagesId} 0 R >>\nendobj\n");

        var xref = ms.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
            W($"{off:D10} 00000 n \n");
        W($"trailer\n<< /Size {offsets.Count + 1} /Root {catalogId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(fullPath, ms.ToArray());
    }

    private static List<string> BuildAllPages(
        OfflineSession session,
        ProjectMeta meta,
        IReadOnlyList<ChannelStats> stats,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int logoCount,
        string? csvPath = null)
    {
        var pages = new List<string>();
        var ytIndices = new List<int>();
        var ytChIndices = new List<int>();
        var cwtIndices = new List<int>();
        var montageIndices = new List<int>();
        for (var i = 0; i < images.Count; i++)
        {
            if (images[i].Role == "yt") ytIndices.Add(i);
            else if (images[i].Role == "yt_ch") ytChIndices.Add(i);
            else if (images[i].Role == "cwt") cwtIndices.Add(i);
            else if (images[i].Role is "montage" or "montage_before" or "montage_after")
                montageIndices.Add(i);
        }

        pages.Add(BuildFirstPage(session, meta, stats, images, logoCount, ytIndices, csvPath));

        if (meta.Channels is { Count: > 0 })
            pages.Add(BuildChannelsPage(meta));

        if (montageIndices.Count > 0)
            pages.Add(BuildMontagePage(meta, images, montageIndices));

        if (ytChIndices.Count > 0)
        {
            var remainingYt = new Queue<int>(ytChIndices);
            var firstYt = true;
            while (remainingYt.Count > 0)
            {
                pages.Add(BuildYtChannelPage(images, remainingYt, firstYt));
                firstYt = false;
            }
        }

        if (cwtIndices.Count == 0)
            return pages;

        // Pack CWT images onto one or more pages with a clear section title.
        var remaining = new Queue<int>(cwtIndices);
        var firstCwtPage = true;
        while (remaining.Count > 0)
        {
            pages.Add(BuildCwtPage(images, remaining, firstCwtPage));
            firstCwtPage = false;
        }

        return pages;
    }

    private static string BuildYtChannelPage(
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        Queue<int> remaining,
        bool writeSectionTitle)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        if (writeSectionTitle)
        {
            WriteText(sb, "F2", 14, left, y, "Grafice Y(t) pe canal — durata completa");
            y -= 8;
            StrokeHLine(sb, left, left + 280, y);
            y -= 16;
            WriteText(sb, "F1", 9, left, y,
                Pdf("Fiecare canal activ pe toata durata inregistrarii (decimat pentru afisare)."));
            y -= 18;
        }

        var placed = 0;
        while (remaining.Count > 0 && y > Margin + 80)
        {
            var i = remaining.Peek();
            var (_, pw, ph, _) = images[i];
            var maxW = ContentW;
            var maxH = Math.Min(320.0, y - Margin - 10);
            if (maxH < 90) break;
            var scale = Math.Min(maxW / pw, maxH / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var imgY = y - dh;
            sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{i} Do Q"));
            y = imgY - 14;
            remaining.Dequeue();
            placed++;
            if (placed >= 2) break; // max 2 channel plots per page
        }

        if (placed == 0 && remaining.Count > 0)
            remaining.Dequeue();

        return sb.ToString();
    }

    private static string BuildFirstPage(
        OfflineSession session,
        ProjectMeta meta,
        IReadOnlyList<ChannelStats> stats,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        int logoCount,
        IReadOnlyList<int> ytIndices,
        string? csvPath = null)
    {
        var sb = new StringBuilder(16_384);
        var y = PageH - Margin;
        var left = Margin;
        var right = PageW - Margin;

        double logoDw = 0, logoDh = 0;
        if (logoCount > 0)
        {
            var (lw, lh, _) = (images[0].W, images[0].H, images[0].Role);
            var targetH = 90.0;
            var scale = targetH / Math.Max(1, lh);
            logoDw = lw * scale;
            logoDh = lh * scale;
            var logoY = y - logoDh;
            sb.AppendLine(Inv($"q {logoDw:0.##} 0 0 {logoDh:0.##} {left:0.##} {logoY:0.##} cm /Im0 Do Q"));
        }

        var textLeft = left + (logoDw > 0 ? logoDw + 14 : 0);
        var headerTop = y - 4;
        var textBlockH = 68.0;
        var textStartY = logoDh > 0
            ? y - (logoDh - textBlockH) / 2 - 12
            : headerTop;

        WriteText(sb, "F2", 16, textLeft, textStartY,
            Pdf(ReportHeaderHelper.ProductName + " — Raport masuratoare"));
        WriteText(sb, "F1", 9, textLeft, textStartY - 14,
            Pdf(ReportHeaderHelper.InstitutionLine));
        WriteText(sb, "F1", 9, textLeft, textStartY - 26,
            Pdf(ReportHeaderHelper.DepartmentName));
        WriteText(sb, "F2", 11, textLeft, textStartY - 40,
            Pdf(ReportHeaderHelper.ExpertLine));
        WriteText(sb, "F1", 9, textLeft, textStartY - 52,
            Pdf(ReportHeaderHelper.AuthorLine));
        WriteText(sb, "F1", 9, textLeft, textStartY - 66,
            Pdf($"Generat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));

        y = Math.Min(y - Math.Max(logoDh, textBlockH + 8), textStartY - 74) - 10;
        sb.Append(ReportHeaderHelper.PdfTricolorStripe(left, y - 1.5, right - left, 3.5));
        y -= 16;

        WriteText(sb, "F2", 12, left, y, "Date masuratoare");
        y -= 8;
        StrokeHLine(sb, left, left + 140, y);
        y -= 14;

        var metaRows = BuildMetaRows(session, meta, csvPath);
        var labelW = 118.0;
        var rowH = 16.0;
        foreach (var (label, value) in metaRows)
        {
            if (y < Margin + 160) break; // leave room for stats + plot
            var isFpStatus = IndustrialReportSections.IsFingerprintStatusRow(label);
            var display = isFpStatus
                ? MeasurementFingerprint.FormatStatusForPdf(value)
                : value;
            var wrapped = Wrap(Pdf(display), ContentW - labelW, 9);
            var h = Math.Max(rowH, wrapped.Count * 11.0 + 4);
            FillRect(sb, left, y - h + 4, labelW, h, 0.91, 0.93, 0.95);
            StrokeRect(sb, left, y - h + 4, ContentW, h);
            WriteText(sb, "F2", 9, left + 4, y - 11, Pdf(label));
            var ty = y - 11;
            (double r, double g, double b)? rgb = isFpStatus
                ? MeasurementFingerprint.StatusColorRgb(value)
                : null;
            foreach (var line in wrapped)
            {
                WriteText(sb, "F1", 9, left + labelW + 4, ty, line, fillRgb: rgb);
                ty -= 11;
            }
            y -= h;
        }

        y -= 16;

        WriteText(sb, "F2", 12, left, y, "Statistici canale");
        y -= 8;
        StrokeHLine(sb, left, left + 120, y);
        y -= 14;

        string[] headers = { "Canal", "N", "Min", "Max", "Mean", "P2P" };
        double[] colW = { 150, 50, 70, 70, 70, 70 };
        var sumW = colW.Sum();
        for (var i = 0; i < colW.Length; i++)
            colW[i] = colW[i] / sumW * ContentW;

        var headH = 16.0;
        FillRect(sb, left, y - headH + 3, ContentW, headH, 0.11, 0.14, 0.19);
        var hx = left;
        for (var c = 0; c < headers.Length; c++)
        {
            WriteText(sb, "F2", 9, hx + 3, y - 9, headers[c], white: true);
            hx += colW[c];
        }
        y -= headH;

        var rowIdx = 0;
        // Cap stats rows so Y(t) always has room on page 1
        foreach (var s in stats.Take(12))
        {
            if (y < Margin + 140) break;
            var rh = 14.0;
            if (rowIdx % 2 == 1)
                FillRect(sb, left, y - rh + 3, ContentW, rh, 0.96, 0.97, 0.98);
            StrokeRect(sb, left, y - rh + 3, ContentW, rh);
            var cells = new[]
            {
                Trunc(Pdf(s.Name), 28),
                s.Count.ToString(CultureInfo.InvariantCulture),
                Fmt(s.Min),
                Fmt(s.Max),
                Fmt(s.Mean),
                Fmt(s.PeakToPeak)
            };
            var cx = left;
            for (var c = 0; c < cells.Length; c++)
            {
                WriteText(sb, "F1", 8, cx + 3, y - 9, cells[c]);
                cx += colW[c];
            }
            y -= rh;
            rowIdx++;
        }

        y -= 16;

        if (ytIndices.Count > 0 && y > Margin + 60)
        {
            WriteText(sb, "F2", 12, left, y, "Diagrama Y(t) — durata completa");
            y -= 8;
            StrokeHLine(sb, left, left + 100, y);
            y -= 12;

            foreach (var i in ytIndices)
            {
                var (_, pw, ph, _) = images[i];
                var maxW = ContentW;
                var maxH = Math.Min(240.0, y - Margin - 10);
                if (maxH < 60) break;
                var scale = Math.Min(maxW / pw, maxH / ph);
                var dw = pw * scale;
                var dh = ph * scale;
                var imgY = y - dh;
                sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{i} Do Q"));
                y = imgY - 12;
            }
        }

        return sb.ToString();
    }

    private static string BuildCwtPage(
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        Queue<int> remaining,
        bool writeSectionTitle)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        var right = PageW - Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        if (writeSectionTitle)
        {
            // ASCII-safe (Helvetica) — matches Excel meaning without diacritics
            WriteText(sb, "F2", 13, left, y, "Analiza timp-frecventa (CWT - Morlet)");
            y -= 8;
            StrokeHLine(sb, left, right, y);
            y -= 12;
            WriteText(sb, "F1", 9, left, y,
                "Scalograma |W(t,f)| — calculata offline la export (ScaleCount=96). Un panou / canal activ.");
            y -= 18;
        }
        else
        {
            WriteText(sb, "F2", 11, left, y, "Analiza timp-frecventa (CWT - Morlet) — continuare");
            y -= 8;
            StrokeHLine(sb, left, left + 280, y);
            y -= 14;
        }

        // Prefer one large scalogram per page when possible; pack 2 if space allows.
        var placed = 0;
        while (remaining.Count > 0)
        {
            var i = remaining.Peek();
            var (_, pw, ph, _) = images[i];
            var maxW = ContentW;
            // First image on titled page can be taller; subsequent share remaining height
            var maxH = placed == 0
                ? Math.Min(420.0, y - Margin - 16)
                : Math.Min(300.0, y - Margin - 16);
            if (maxH < 80)
                break;

            var scale = Math.Min(maxW / pw, maxH / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var imgY = y - dh;
            sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{i} Do Q"));
            y = imgY - 14;
            remaining.Dequeue();
            placed++;

            // Keep at most two per page for readability
            if (placed >= 2)
                break;
        }

        // Safety: if nothing fit (shouldn't happen), drop one to avoid infinite loop
        if (placed == 0 && remaining.Count > 0)
            remaining.Dequeue();

        return sb.ToString();
    }

    private static List<(string Label, string Value)> BuildMetaRows(
        OfflineSession session, ProjectMeta meta, string? csvPath = null)
    {
        var activeCount = meta.ActiveChannelCount > 0
            ? meta.ActiveChannelCount
            : meta.Channels?.Count ?? 0;
        var sensorNames = meta.Channels is { Count: > 0 }
            ? string.Join(", ", meta.Channels.Select(c => $"{c.Name}={c.SensorDisplay}"))
            : (meta.SensorSummary ?? "");

        var rows = new List<(string, string)>
        {
            ("Operator", meta.Operator ?? ""),
            ("Sample / Proba", meta.SampleId ?? ""),
        };
        rows.AddRange(Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta));
        // Specimen: SampleLengthMm / SampleMassG / SampleDimensionsSummary (0 = —)
        rows.AddRange(SampleDimensions.BuildReportMetaRows(meta, emptyAsDash: true));
        // Poisson ν / recuperare elastică (opțional)
        rows.AddRange(StrainAnalysisIndicators.BuildReportMetaRows(meta, emptyAsDash: false));
        rows.AddRange(new (string, string)[]
        {
            ("Locatie", meta.Location ?? ""),
            ("Proiect", meta.ProjectName ?? ""),
            ("Experiment", meta.ExperimentName ?? ""),
            ("Tip experiment", meta.ExperimentType ?? ""),
            ("Start experiment", FormatExperimentStart(meta)),
            ("Stop experiment", FormatExperimentEnd(meta)),
        });
        if (meta.EstimatedDurationMinutes > 0)
        {
            rows.Add(("Durata est. min",
                meta.EstimatedDurationMinutes.ToString(CultureInfo.InvariantCulture)));
        }
        rows.AddRange(new (string, string)[]
        {
            ("Poză montaj (înainte)", string.IsNullOrWhiteSpace(meta.MontagePhotoPath)
                ? "—"
                : Path.GetFileName(meta.MontagePhotoPath) + FormatPhotoMetaSuffix(meta.MontageBeforeCapturedLocal)),
            ("Obs. montaj înainte", meta.MontageBeforeNotes ?? ""),
            ("Poză probă (după)", string.IsNullOrWhiteSpace(meta.MontagePhotoAfterPath)
                ? "—"
                : Path.GetFileName(meta.MontagePhotoAfterPath) + FormatPhotoMetaSuffix(meta.MontageAfterCapturedLocal)),
            ("Obs. probă după", meta.MontageAfterNotes ?? ""),
            ("Timp experiment", SamplingRateInfo.FormatExperimentTime(session.Timestamps)),
            ("Backend", meta.Backend ?? ""),
            ("Rata setata Hz", meta.SampleRateHz.ToString(CultureInfo.InvariantCulture)),
            ("Rata efectiva Hz", SamplingRateInfo.FormatHz(
                SamplingRateInfo.EstimateEffectiveHz(session.Timestamps, meta.SampleRateHz > 0 ? meta.SampleRateHz : 50))),
            ("Dt mediu s", SamplingRateInfo.FormatDt(SamplingRateInfo.MeanDeltaSeconds(session.Timestamps))),
            ("Canale active", activeCount > 0 ? activeCount.ToString(CultureInfo.InvariantCulture) : "—"),
            ("Senzori planificati", meta.PlannedSensors ?? ""),
            ("Senzori (rezumat)", sensorNames),
            ("Calibrare", meta.CalibrationNotes ?? ""),
            ("Comentariu", meta.Comment ?? ""),
            ("Sursa", session.SourcePath ?? ""),
            ("Esantioane", session.Timestamps.Count.ToString(CultureInfo.InvariantCulture)),
        });
        if (!string.IsNullOrWhiteSpace(meta.DeviceEstHint) && meta.DeviceEstHint != "EST: —")
            rows.Add(("Stare EST", meta.DeviceEstHint));

        foreach (var row in IndustrialReportSections.BuildCommonMetaRows(
                     session, meta,
                     new IndustrialReportSections.Options
                     {
                         CsvPath = MeasurementFingerprint.ResolveExistingCsvPath(
                             csvPath, session.SourcePath)
                     },
                     emptyAsDash: true))
            rows.Add(row);

        return rows;
    }

    private static string FormatExperimentStart(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentStartLocal)) return "—";
        if (DateTime.TryParse(meta.ExperimentStartLocal, null,
                DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return meta.ExperimentStartLocal;
    }

    private static string FormatExperimentEnd(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentEndLocal)) return "—";
        if (DateTime.TryParse(meta.ExperimentEndLocal, null,
                DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return meta.ExperimentEndLocal;
    }

    private static string BuildMontagePage(
        ProjectMeta meta,
        IReadOnlyList<(byte[] Jpeg, int W, int H, string Role)> images,
        IReadOnlyList<int> montageIndices)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        WriteText(sb, "F2", 14, left, y, "Documentare vizuala — inainte / dupa");
        y -= 8;
        StrokeHLine(sb, left, left + 260, y);
        y -= 16;
        WriteText(sb, "F1", 9, left, y,
            Pdf("Montaj inainte si starea probei dupa — pozele sunt optionale."));
        y -= 18;

        foreach (var i in montageIndices)
        {
            var (_, pw, ph, role) = images[i];
            var isAfter = role.Contains("after", StringComparison.OrdinalIgnoreCase);
            var caption = isAfter ? "DUPA experiment" : "INAINTE de experiment";
            var stamp = isAfter
                ? FormatPhotoMetaSuffix(meta.MontageAfterCapturedLocal)
                : FormatPhotoMetaSuffix(meta.MontageBeforeCapturedLocal);
            var notes = isAfter ? meta.MontageAfterNotes : meta.MontageBeforeNotes;
            WriteText(sb, "F2", 11, left, y, Pdf(caption + stamp));
            y -= 12;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                WriteText(sb, "F1", 9, left, y, Pdf("Obs.: " + notes));
                y -= 12;
            }

            var maxW = ContentW;
            var maxH = Math.Min(260.0, y - Margin - 24);
            if (maxH < 80) break;
            var scale = Math.Min(maxW / pw, maxH / ph);
            var dw = pw * scale;
            var dh = ph * scale;
            var imgY = y - dh;
            sb.AppendLine(Inv($"q {dw:0.##} 0 0 {dh:0.##} {left:0.##} {imgY:0.##} cm /Im{i} Do Q"));
            y = imgY - 18;
        }

        return sb.ToString();
    }

    private static string FormatPhotoMetaSuffix(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var dt))
            return " · " + dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return " · " + iso;
    }

    private static string BuildChannelsPage(ProjectMeta meta)
    {
        var sb = new StringBuilder(8_192);
        var y = PageH - Margin;
        var left = Margin;
        y = WriteLaterPageIdentityStrip(sb, left, y);

        WriteText(sb, "F2", 14, left, y, "Canale active si senzori");
        y -= 8;
        StrokeHLine(sb, left, left + 220, y);
        y -= 16;
        WriteText(sb, "F1", 9, left, y,
            Pdf($"Numar canale active in experiment: {meta.ActiveChannelCount}"));
        y -= 18;

        foreach (var ch in meta.Channels)
        {
            if (y < Margin + 40) break;
            WriteText(sb, "F2", 10, left, y, Pdf($"CH{ch.Index} — {ch.Name} [{ch.Unit}]"));
            y -= 13;
            var detail = ch.OneLineSummary();
            // Drop leading "Name [unit]: " already shown
            var colon = detail.IndexOf(':');
            if (colon > 0 && colon < detail.Length - 1)
                detail = detail[(colon + 1)..].Trim();
            foreach (var line in Wrap(Pdf(detail), ContentW - 8, 8).Take(3))
            {
                if (y < Margin + 28) break;
                WriteText(sb, "F1", 8, left + 8, y, line);
                y -= 11;
            }
            y -= 6;
        }

        if (!string.IsNullOrWhiteSpace(meta.SensorSummary) && meta.Channels.Count == 0)
        {
            foreach (var line in Wrap(Pdf(meta.SensorSummary), ContentW, 8).Take(8))
            {
                WriteText(sb, "F1", 8, left, y, line);
                y -= 11;
            }
        }

        return sb.ToString();
    }

    /// <summary>Identity strip on later pages that already have a header (not stamped on plot images).</summary>
    private static double WriteLaterPageIdentityStrip(StringBuilder sb, double left, double y)
    {
        WriteText(sb, "F1", 8, left, y, Pdf(ReportHeaderHelper.CompactIdentityLine));
        return y - 14;
    }

    private static string Pdf(string? s) => ReportHeaderHelper.PdfSafe(s ?? "");

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
            if (lines.Count >= 4)
            {
                lines[^1] = Trunc(lines[^1] + " " + t, maxChars);
                return lines;
            }
        }
        lines.Add(t);
        return lines;
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
        sb.AppendLine("1.2 w");
        sb.AppendLine(Inv($"{x0:0.##} {y:0.##} m {x1:0.##} {y:0.##} l S"));
    }

    private static void StrokeRect(StringBuilder sb, double x, double y, double w, double h)
    {
        sb.AppendLine("0.77 0.81 0.84 RG");
        sb.AppendLine("0.6 w");
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
