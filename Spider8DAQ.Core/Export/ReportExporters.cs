using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

public static class AsciiExporter
{
    public static void ExportConfigurable(
        string path,
        OfflineSession session,
        string delimiter = "\t",
        bool includeHeader = true,
        string decimalSeparator = ".")
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        if (decimalSeparator == ",")
            culture.NumberFormat.NumberDecimalSeparator = ",";

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        if (includeHeader)
        {
            w.Write("Timestamp" + delimiter + "Sequence");
            foreach (var n in session.ChannelNames)
                w.Write(delimiter + n);
            w.WriteLine();
        }

        for (var i = 0; i < session.Timestamps.Count; i++)
        {
            w.Write(session.Timestamps[i].ToString("O", CultureInfo.InvariantCulture));
            w.Write(delimiter);
            w.Write(session.Sequences[i].ToString(CultureInfo.InvariantCulture));
            for (var c = 0; c < session.Columns.Count; c++)
            {
                w.Write(delimiter);
                var v = session.Columns[c][i];
                w.Write(double.IsNaN(v) ? "" : v.ToString("G17", culture));
            }
            w.WriteLine();
        }
    }
}

public static class DiaDemExporter
{
    /// <summary>Writes a simple DIAdem-friendly DAT header + ASCII body.</summary>
    public static void Export(string path, OfflineSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new StreamWriter(path, false, Encoding.ASCII);
        w.WriteLine("#BEGINCHANNELHEADER");
        w.WriteLine($"201,{session.ChannelNames.Count + 2}");
        w.WriteLine("200,Timestamp");
        w.WriteLine("200,Sequence");
        foreach (var n in session.ChannelNames)
            w.WriteLine($"200,{n}");
        w.WriteLine("#ENDCHANNELHEADER");
        w.WriteLine("#BEGINDATA");
        for (var i = 0; i < session.Timestamps.Count; i++)
        {
            w.Write(session.Timestamps[i].ToOADate().ToString(CultureInfo.InvariantCulture));
            w.Write('\t');
            w.Write(session.Sequences[i].ToString(CultureInfo.InvariantCulture));
            for (var c = 0; c < session.Columns.Count; c++)
            {
                w.Write('\t');
                var v = session.Columns[c][i];
                w.Write(double.IsNaN(v) ? "NaN" : v.ToString("G17", CultureInfo.InvariantCulture));
            }
            w.WriteLine();
        }
        w.WriteLine("#ENDDATA");
    }
}

public static class HtmlReportExporter
{
    public static void Export(
        string path,
        OfflineSession session,
        ProjectMeta meta,
        IEnumerable<ChannelStats> stats,
        string? plotImagePath = null,
        string? csvPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var pack = ReportGraphPack.Build(session, meta.SampleRateHz > 0 ? meta.SampleRateHz : 50, meta);
        pack.SaveBesideReport(path);

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>UPET AcqLab Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:28px;color:#1b2430;background:#f7f9fb}");
            sb.AppendLine(".card{background:#fff;border:1px solid #d5dde5;border-radius:8px;padding:20px 24px;max-width:1100px;margin:0 auto;box-shadow:0 1px 3px rgba(0,0,0,.06)}");
            sb.AppendLine("h1{margin:0 0 4px;font-size:22px} h2{margin:22px 0 10px;font-size:16px;color:#1b2430}");
            sb.AppendLine(".sub{color:#5a6570;margin:0 0 16px} table{border-collapse:collapse;width:100%}");
            sb.AppendLine("td,th{border:1px solid #d5dde5;padding:7px 10px;text-align:left} th{background:#1b2430;color:#fff}");
            sb.AppendLine("tr:nth-child(even){background:#f5f8fb} img.plot{max-width:100%;height:auto;border:1px solid #d5dde5;border-radius:4px;margin-top:8px}");
            sb.AppendLine(ReportHeaderHelper.HtmlAntetCss);
            sb.AppendLine(".meta{width:100%;margin:0 0 8px} .meta td{border:1px solid #d5dde5;padding:7px 10px}");
            sb.AppendLine(".meta td:first-child{font-weight:600;width:180px;background:#e8eef4;color:#1b2430}");
            sb.AppendLine("h2.sec{margin:22px 0 10px;font-size:16px;padding-bottom:6px;border-bottom:2px solid #1b2430}");
            sb.AppendLine(".fp-badge{display:inline-block;padding:2px 8px;border-radius:4px;background:#f0f3f6}");
            sb.AppendLine("</style></head><body><div class='card'>");
            sb.Append(ReportHeaderHelper.BuildHtmlAntet("Raport măsurătoare"));
            sb.AppendLine("<h2 class='sec'>Date măsurătoare</h2>");
            sb.AppendLine("<table class='meta'>");
            void Meta(string k, string? v) => sb.AppendLine($"<tr><td>{Esc(k)}</td><td>{Esc(v)}</td></tr>");
            Meta("Autor soft", ReportHeaderHelper.SoftwareAuthor);
            Meta("Operator", meta.Operator);
            Meta("Sample / Probă", meta.SampleId);
            foreach (var (label, value) in Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta))
                Meta(label, value);
            // Specimen: SampleLengthMm / SampleMassG / SampleDimensionsSummary (0 = —)
            foreach (var (label, value) in SampleDimensions.BuildReportMetaRows(meta, emptyAsDash: true))
                Meta(label, value);
            // Poisson ν / recuperare elastică (opțional)
            foreach (var (label, value) in StrainAnalysisIndicators.BuildReportMetaRows(meta, emptyAsDash: false))
                Meta(label, value);
            Meta("Proiect", meta.ProjectName);
            Meta("Experiment / preset", meta.ExperimentName);
            Meta("Tip experiment", meta.ExperimentType);
            if (CylinderContourExport.ShouldAttempt(meta, session) && meta.CylinderContour is not null)
                Meta("Contur cilindru", meta.CylinderContour.ToStatusSummary());
            Meta("Start experiment", FormatExperimentStartHtml(meta));
            Meta("Stop experiment", FormatExperimentEndHtml(meta));
            if (meta.EstimatedDurationMinutes > 0)
                Meta("Durată estimată [min]", meta.EstimatedDurationMinutes.ToString());
            Meta("Poză montaj (înainte)", string.IsNullOrWhiteSpace(meta.MontagePhotoPath)
                ? "— (opțional)"
                : System.IO.Path.GetFileName(meta.MontagePhotoPath) + FormatCapturedHtml(meta.MontageBeforeCapturedLocal));
            Meta("Observații montaj înainte", string.IsNullOrWhiteSpace(meta.MontageBeforeNotes) ? "—" : meta.MontageBeforeNotes);
            Meta("Poză probă (după)", string.IsNullOrWhiteSpace(meta.MontagePhotoAfterPath)
                ? "— (opțional)"
                : System.IO.Path.GetFileName(meta.MontagePhotoAfterPath) + FormatCapturedHtml(meta.MontageAfterCapturedLocal));
            Meta("Observații probă după", string.IsNullOrWhiteSpace(meta.MontageAfterNotes) ? "—" : meta.MontageAfterNotes);
            Meta("Timp înregistrare", SamplingRateInfo.FormatExperimentTime(session.Timestamps));
            Meta("Locație / banc", meta.Location);
            Meta("Backend", meta.Backend);
            Meta("Rată setată Hz", meta.SampleRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Meta("Rată efectivă Hz", SamplingRateInfo.FormatHz(
                SamplingRateInfo.EstimateEffectiveHz(session.Timestamps, meta.SampleRateHz > 0 ? meta.SampleRateHz : 50)));
            Meta("Δt mediu s", SamplingRateInfo.FormatDt(SamplingRateInfo.MeanDeltaSeconds(session.Timestamps)));
            var activeCount = meta.ActiveChannelCount > 0 ? meta.ActiveChannelCount : meta.Channels?.Count ?? 0;
            Meta("Canale active (experiment)", activeCount > 0 ? activeCount.ToString() : "—");
            Meta("Senzori planificați", meta.PlannedSensors);
            Meta("Senzori (rezumat)", meta.Channels is { Count: > 0 }
                ? string.Join(", ", meta.Channels.Select(c => $"{c.Name}={c.SensorDisplay}"))
                : meta.SensorSummary);
            Meta("Calibrare", meta.CalibrationNotes);
            Meta("Comentariu / obiectiv", meta.Comment);
            Meta("Sursă", session.SourcePath);
            Meta("Eșantioane", session.Timestamps.Count.ToString());
            Meta("Grafice în raport", $"{pack.TotalGraphCount} (toată durata)");
            if (!string.IsNullOrWhiteSpace(meta.DeviceEstHint) && meta.DeviceEstHint != "EST: —")
                Meta("Stare EST", meta.DeviceEstHint);
            sb.AppendLine("</table>");

            var industrial = IndustrialReportSections.Build(
                session, meta,
                new IndustrialReportSections.Options
                {
                    CsvPath = MeasurementFingerprint.ResolveExistingCsvPath(
                        csvPath, session.SourcePath)
                });
            sb.Append(IndustrialReportSections.BuildHtmlSections(industrial, meta));

            void AppendPhoto(string title, string? photoPath, string? notes, string? capturedIso)
            {
                if (string.IsNullOrWhiteSpace(photoPath) || !System.IO.File.Exists(photoPath)) return;
                try
                {
                    var bytes = System.IO.File.ReadAllBytes(photoPath);
                    var ext = System.IO.Path.GetExtension(photoPath).ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".bmp" => "image/bmp",
                        _ => "image/jpeg"
                    };
                    var b64 = Convert.ToBase64String(bytes);
                    var stamp = FormatCapturedHtml(capturedIso);
                    sb.AppendLine($"<h2 class='sec'>{Esc(title)}{Esc(stamp)}</h2>");
                    if (!string.IsNullOrWhiteSpace(notes))
                        sb.AppendLine($"<p class='sub'><strong>Observații:</strong> {Esc(notes)}</p>");
                    sb.AppendLine($"<img class='plot' alt='{Esc(title)}' src='data:{mime};base64,{b64}'/>");
                }
                catch { /* optional */ }
            }

            AppendPhoto("ÎNAINTE de experiment", meta.MontagePhotoPath, meta.MontageBeforeNotes, meta.MontageBeforeCapturedLocal);
            AppendPhoto("DUPĂ experiment", meta.MontagePhotoAfterPath, meta.MontageAfterNotes, meta.MontageAfterCapturedLocal);

            if (meta.Channels is { Count: > 0 })
            {
                sb.AppendLine("<h2 class='sec'>Canale active și senzori</h2>");
                sb.AppendLine("<table><tr>" +
                              "<th>CH#</th><th>Canal</th><th>Unit</th><th>Senzor</th><th>Cod</th>" +
                              "<th>Categorie</th><th>Punte</th><th>Cap.</th><th>Sens.</th>" +
                              "<th>Scale</th><th>Zero</th><th>Uexc</th><th>Range</th><th>Filtru</th><th>Note</th></tr>");
                foreach (var ch in meta.Channels)
                {
                    sb.AppendLine(
                        $"<tr><td>{ch.Index}</td><td>{Esc(ch.Name)}</td><td>{Esc(ch.Unit)}</td>" +
                        $"<td>{Esc(ch.SensorDisplay)}</td><td>{Esc(ch.SensorCode)}</td>" +
                        $"<td>{Esc(ch.SensorCategory)}</td><td>{Esc(ch.Bridge)}</td>" +
                        $"<td>{(ch.Capacity > 0 ? ch.Capacity.ToString("G6") : "")}</td>" +
                        $"<td>{(ch.Sensitivity > 0 ? ch.Sensitivity.ToString("G6") : "")}</td>" +
                        $"<td>{ch.Scale:G4}</td><td>{ch.TareValue:G4}</td>" +
                        $"<td>{(ch.ExcitationV > 0 ? ch.ExcitationV.ToString("G4") : "")}</td>" +
                        $"<td>{(ch.RangeMvPerV > 0 ? ch.RangeMvPerV.ToString("G4") : "")}</td>" +
                        $"<td>{(ch.FilterHz > 0 ? ch.FilterHz.ToString("G4") : "")}</td>" +
                        $"<td>{Esc(ch.SensorNotes)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<h2 class='sec'>Statistici canale</h2>");
            sb.AppendLine("<table><tr><th>Canal</th><th>N</th><th>Min</th><th>Max</th><th>Mean</th><th>P2P</th></tr>");
            foreach (var s in stats)
            {
                sb.AppendLine(
                    $"<tr><td>{Esc(s.Name)}</td><td>{s.Count}</td><td>{s.Min:G6}</td><td>{s.Max:G6}</td>" +
                    $"<td>{s.Mean:G6}</td><td>{s.PeakToPeak:G6}</td></tr>");
            }
            sb.AppendLine("</table>");

            void EmbedPng(string title, string? pngPath)
            {
                if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath)) return;
                try
                {
                    var b64 = Convert.ToBase64String(File.ReadAllBytes(pngPath));
                    sb.AppendLine($"<h2 class='sec'>{Esc(title)}</h2>");
                    sb.AppendLine($"<img class='plot' alt='{Esc(title)}' src='data:image/png;base64,{b64}'/>");
                }
                catch { /* skip */ }
            }

            sb.AppendLine("<h2 class='sec'>Grafice — toată durata înregistrării</h2>");
            sb.AppendLine("<p class='sub'>Overview pe unitate, Y(t) pe fiecare canal activ, CWT" +
                          (string.IsNullOrWhiteSpace(pack.ContourPng) ? "" : ", schemă contur cilindru") +
                          (pack.DeformationCurvePngs.Count > 0 ? " și curbe deformare uᵢ" : "") +
                          ". Fișiere PNG și în folderul <code>*_grafice</code> lângă acest HTML.</p>");

            // Contur FIRST among graphs — unmissable.
            if (!string.IsNullOrWhiteSpace(pack.ContourPng) && File.Exists(pack.ContourPng))
            {
                var fn = string.IsNullOrWhiteSpace(pack.ContourFootnoteRo) ? "" : " — " + pack.ContourFootnoteRo;
                EmbedPng("Contur cilindru (plan + elevație + secțiune · R0 + Fmax)" + fn, pack.ContourPng);
            }
            else if (!string.IsNullOrWhiteSpace(pack.ContourSkipReasonRo))
            {
                sb.AppendLine("<h2 class='sec'>Contur cilindru</h2>");
                sb.AppendLine($"<p class='sub' style='color:#a04000'><strong>Status:</strong> {Esc(pack.ContourSkipReasonRo)}</p>");
            }

            foreach (var (title, p) in pack.DeformationCurvePngs)
                EmbedPng($"Curbe deformare — {title}", p);

            foreach (var (_, title, p) in pack.OverviewPngs)
                EmbedPng($"Overview — {title}", p);
            foreach (var (name, p) in pack.ChannelYtPngs)
                EmbedPng($"Y(t) — {name} (durată completă)", p);
            foreach (var (name, p) in pack.CwtPngs)
                EmbedPng($"CWT — {name} (durată completă)", p);

            // Legacy single plot fallback if pack empty
            if (pack.TotalGraphCount == 0 && !string.IsNullOrWhiteSpace(plotImagePath) && File.Exists(plotImagePath))
                EmbedPng("Diagramă Y(t)", plotImagePath);

            sb.AppendLine($"<p class='sub' style='margin-top:24px'>UPET AcqLab · {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
            sb.AppendLine("</div></body></html>");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        finally
        {
            // pack disposed by using
        }
    }

    private static string FormatExperimentStartHtml(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentStartLocal)) return "—";
        if (DateTime.TryParse(meta.ExperimentStartLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return meta.ExperimentStartLocal;
    }

    private static string FormatExperimentEndHtml(ProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(meta.ExperimentEndLocal)) return "—";
        if (DateTime.TryParse(meta.ExperimentEndLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return meta.ExperimentEndLocal;
    }

    private static string FormatCapturedHtml(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return " · " + dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return " · " + iso;
    }

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
