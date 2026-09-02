using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Builds all report graphs for the <b>full recording duration</b>
/// (overview by unit, one Y(t) per active channel, CWT per channel, optional cylinder contour).
/// </summary>
public sealed class ReportGraphPack : IDisposable
{
    public string TempDirectory { get; }
    public IReadOnlyList<(string UnitKey, string Title, string Path)> OverviewPngs { get; private set; }
        = Array.Empty<(string, string, string)>();
    public IReadOnlyList<(string Name, string Path)> ChannelYtPngs { get; private set; }
        = Array.Empty<(string, string)>();
    public IReadOnlyList<(string Name, string Path)> CwtPngs { get; private set; }
        = Array.Empty<(string, string)>();
    public string? ContourPng { get; private set; }
    public string? ContourJpeg { get; private set; }
    public string? ContourFootnoteRo { get; private set; }
    /// <summary>Romanian reason when contour was requested but not generated.</summary>
    public string? ContourSkipReasonRo { get; private set; }
    /// <summary>uᵢ vs t / cursă / F PNGs for Contur reports.</summary>
    public IReadOnlyList<(string Title, string Path)> DeformationCurvePngs { get; private set; }
        = Array.Empty<(string, string)>();
    public IReadOnlyList<string> DeformationCurveJpegs { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> OverviewJpegs { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> ChannelYtJpegs { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> CwtJpegs { get; private set; } = Array.Empty<string>();

    private ReportGraphPack(string tempDirectory) => TempDirectory = tempDirectory;

    /// <summary>Generate full-duration graphs for every active channel in the session.</summary>
    public static ReportGraphPack Build(
        OfflineSession session,
        double fallbackSampleRateHz = 50,
        ProjectMeta? meta = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"upet_graphs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pack = new ReportGraphPack(dir);

        try
        {
            pack.OverviewPngs = SessionPlotRenderer.SaveUnitGroupedPngs(session, Path.Combine(dir, "overview"));
        }
        catch
        {
            pack.OverviewPngs = Array.Empty<(string, string, string)>();
        }

        try
        {
            pack.ChannelYtPngs = SessionPlotRenderer.SavePerChannelPngs(session, Path.Combine(dir, "yt"));
        }
        catch
        {
            pack.ChannelYtPngs = Array.Empty<(string, string)>();
        }

        try
        {
            pack.CwtPngs = CwtPlotRenderer.SavePerChannelPngs(
                session,
                Path.Combine(dir, "cwt"),
                fallbackSampleRateHz: fallbackSampleRateHz > 0 ? fallbackSampleRateHz : 50,
                windowSeconds: 0);
        }
        catch
        {
            pack.CwtPngs = Array.Empty<(string, string)>();
        }

        pack.TryBuildContour(session, meta);

        pack.OverviewJpegs = ConvertExistingToJpeg(pack.OverviewPngs.Select(p => p.Path), Path.Combine(dir, "overview_jpg"));
        pack.ChannelYtJpegs = ConvertExistingToJpeg(pack.ChannelYtPngs.Select(p => p.Path), Path.Combine(dir, "yt_jpg"));
        try
        {
            pack.CwtJpegs = CwtPlotRenderer.SavePerChannelJpegs(
                session,
                Path.Combine(dir, "cwt_jpg"),
                fallbackSampleRateHz: fallbackSampleRateHz > 0 ? fallbackSampleRateHz : 50,
                windowSeconds: 0).Select(p => p.Path).ToList();
        }
        catch
        {
            pack.CwtJpegs = Array.Empty<string>();
        }

        return pack;
    }

    private void TryBuildContour(OfflineSession session, ProjectMeta? meta)
    {
        ContourPng = null;
        ContourJpeg = null;
        ContourFootnoteRo = null;
        ContourSkipReasonRo = null;
        DeformationCurvePngs = Array.Empty<(string, string)>();
        DeformationCurveJpegs = Array.Empty<string>();
        if (meta is null) return;
        CylinderContourExport.MergeFromSession(meta, session);
        CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session);
        if (!CylinderContourExport.ShouldAttempt(meta, session))
            return;
        try
        {
            var result = CylinderContourExport.TryCompute(session, meta);
            if (result is null || !result.IsValid)
            {
                ContourSkipReasonRo = result?.Error ?? CylinderContourExport.DescribeSkipReason(meta, session);
                return;
            }
            ContourFootnoteRo = result.PlotFootnoteRo;
            var png = Path.Combine(TempDirectory, "contour", $"contour_{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            CylinderContourPlotRenderer.BuildPlot(result).SavePng(
                png, CylinderContourPlotRenderer.DefaultWidth, CylinderContourPlotRenderer.DefaultHeight);
            if (!File.Exists(png) || new FileInfo(png).Length <= 0)
            {
                ContourSkipReasonRo = "Contur cilindru: PNG generat gol (0 bytes).";
                return;
            }
            ContourPng = png;
            var jpg = Path.Combine(TempDirectory, "contour", $"contour_{Guid.NewGuid():N}.jpg");
            CylinderContourPlotRenderer.BuildPlot(result).SaveJpeg(
                jpg, CylinderContourPlotRenderer.DefaultWidth, CylinderContourPlotRenderer.DefaultHeight, 92);
            ContourJpeg = jpg;

            try
            {
                var curveDir = Path.Combine(TempDirectory, "contour", "curbe");
                var pack = CylinderDeformationCurveRenderer.TryBuildPngs(session, meta, result, curveDir);
                if (pack is not null)
                {
                    DeformationCurvePngs = pack.AllPngs.ToList();
                    DeformationCurveJpegs = ConvertExistingToJpeg(
                        pack.AllPngs.Select(p => p.Path),
                        Path.Combine(TempDirectory, "contour", "curbe_jpg"));
                }
            }
            catch { /* curves optional */ }
        }
        catch (Exception ex)
        {
            ContourPng = null;
            ContourJpeg = null;
            ContourSkipReasonRo = "Contur cilindru omis: " + ex.Message;
        }
    }

    /// <summary>
    /// Copies all PNGs next to the report as <c>{stem}_grafice\</c>
    /// (overview / Y(t) pe canal / CWT / contur — toată durata).
    /// </summary>
    public string? SaveBesideReport(string reportPath)
    {
        try
        {
            var full = Path.GetFullPath(reportPath);
            var dir = Path.GetDirectoryName(full) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(full);
            var outDir = Path.Combine(dir, stem + "_grafice");
            Directory.CreateDirectory(outDir);

            var n = 0;
            foreach (var (_, title, path) in OverviewPngs.Where(p => File.Exists(p.Path)))
            {
                var name = $"01_overview_{Sanitize(title)}.png";
                File.Copy(path, Path.Combine(outDir, name), overwrite: true);
                n++;
            }

            var i = 0;
            foreach (var (chName, path) in ChannelYtPngs.Where(p => File.Exists(p.Path)))
            {
                i++;
                var name = $"02_yt_{i:00}_{Sanitize(chName)}.png";
                File.Copy(path, Path.Combine(outDir, name), overwrite: true);
                n++;
            }

            i = 0;
            foreach (var (chName, path) in CwtPngs.Where(p => File.Exists(p.Path)))
            {
                i++;
                var name = $"03_cwt_{i:00}_{Sanitize(chName)}.png";
                File.Copy(path, Path.Combine(outDir, name), overwrite: true);
                n++;
            }

            if (!string.IsNullOrWhiteSpace(ContourPng) && File.Exists(ContourPng))
            {
                File.Copy(ContourPng, Path.Combine(outDir, "04_contour_cilindru.png"), overwrite: true);
                n++;
            }

            var di = 0;
            foreach (var (title, path) in DeformationCurvePngs.Where(p => File.Exists(p.Path)))
            {
                di++;
                File.Copy(path, Path.Combine(outDir, $"05_curbe_{di:00}_{Sanitize(title)}.png"), overwrite: true);
                n++;
            }

            return n > 0 ? outDir : null;
        }
        catch
        {
            return null;
        }
    }

    public int TotalGraphCount =>
        OverviewPngs.Count(p => File.Exists(p.Path))
        + ChannelYtPngs.Count(p => File.Exists(p.Path))
        + CwtPngs.Count(p => File.Exists(p.Path))
        + (!string.IsNullOrWhiteSpace(ContourPng) && File.Exists(ContourPng) ? 1 : 0)
        + DeformationCurvePngs.Count(p => File.Exists(p.Path));

    /// <summary>PNG bytes for embedding inside .upet / other containers.</summary>
    public IReadOnlyList<(string Role, string Name, byte[] PngBytes)> ToEmbeddedPngAssets()
    {
        var list = new List<(string, string, byte[])>();
        foreach (var (_, title, path) in OverviewPngs.Where(p => File.Exists(p.Path)))
        {
            try { list.Add(("overview", title, File.ReadAllBytes(path))); } catch { /* skip */ }
        }
        foreach (var (name, path) in ChannelYtPngs.Where(p => File.Exists(p.Path)))
        {
            try { list.Add(("yt", name, File.ReadAllBytes(path))); } catch { /* skip */ }
        }
        foreach (var (name, path) in CwtPngs.Where(p => File.Exists(p.Path)))
        {
            try { list.Add(("cwt", name, File.ReadAllBytes(path))); } catch { /* skip */ }
        }
        if (!string.IsNullOrWhiteSpace(ContourPng) && File.Exists(ContourPng))
        {
            try { list.Add(("contour", "Contur cilindru", File.ReadAllBytes(ContourPng))); } catch { /* skip */ }
        }
        foreach (var (title, path) in DeformationCurvePngs.Where(p => File.Exists(p.Path)))
        {
            try { list.Add(("deform_curve", title, File.ReadAllBytes(path))); } catch { /* skip */ }
        }
        return list;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, recursive: true);
        }
        catch { /* ignore */ }
    }

    private static IReadOnlyList<string> ConvertExistingToJpeg(IEnumerable<string> pngPaths, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var list = new List<string>();
        foreach (var png in pngPaths)
        {
            if (!File.Exists(png)) continue;
            try
            {
                var jpegBytes = ReportHeaderHelper.TryImageFileToJpegBytes(png, maxEdgePx: 1920);
                if (jpegBytes is null || jpegBytes.Length == 0) continue;
                var dest = Path.Combine(outDir, Path.GetFileNameWithoutExtension(png) + ".jpg");
                File.WriteAllBytes(dest, jpegBytes);
                list.Add(dest);
            }
            catch { /* skip */ }
        }
        return list;
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray();
        var t = new string(chars).Trim();
        if (t.Length > 48) t = t[..48];
        return string.IsNullOrWhiteSpace(t) ? "canal" : t;
    }
}
