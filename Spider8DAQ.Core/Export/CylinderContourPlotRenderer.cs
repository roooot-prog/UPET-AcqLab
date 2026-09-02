using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Industrial Contur schema (engineering drawing sheet), one PNG:
/// left Tabel u_i legend; plan | elevation (barreled silhouette colored by u) | paleta u | half-section
/// spaced on one row with a caption under each view;
/// mini load curve (sigma-eps / F-cursa / cursa-index) centered under the views with its own Fig. caption.
/// Elevation fill is measured u_i on the sensor ring (circumferential interpolation)
/// faded to u=0 at the platens (contact, no lateral sensor) — not a 3D scan
/// and not a left/right dual-sensor slab.
/// Panels auto-fit L/D (true geometry; radial u may be exaggerated).
/// Numbers live in the table — zero overlapping callouts on the figures.
/// Visual |u| kept in ~2-8% of R0 so the Fmax ring stays readable when swelling is tiny.
/// </summary>
public static class CylinderContourPlotRenderer
{
    public const int DefaultWidth = 2400;
    public const int DefaultHeight = 1600;

    /// <summary>
    /// ClosedXML picture scale so ~1100 px display width (2400×0.458 ≈ 1100).
    /// </summary>
    public const double ExcelEmbedScale = 1100.0 / DefaultWidth;

    /// <summary>Consolas size for the left Tabel u_i legend (readable on the 2400×1600 sheet).</summary>
    public const float TableFontSize = 12f;

    /// <summary>Elevation caption: side view colored by barreling, not a 3D scan.</summary>
    public const string ElevationCaption = "Fig. Elevatie - harta bombare";

    /// <summary>Short honesty note on the u colorbar (ASCII-safe).</summary>
    public const string BarrelMapHonestyNote =
        "culoare = u masurat pe inel; la platene u=0 (contact, fara senzor de u)";

    /// <summary>Left-legend line: colors from measured sensor u on the ring.</summary>
    public const string BarrelMapLegendLine1 = "culoare = u masurat pe inel";

    /// <summary>Left-legend line: platens have no lateral u sensor (contact).</summary>
    public const string BarrelMapLegendLine2 = "la platene u=0 (contact, fara senzor de u)";

    /// <summary>Palette-legend title beside the elevation Turbo strip (ASCII-safe).</summary>
    public const string PaletteLegendTitle = "Paleta u [mm]";

    /// <summary>Cool end of Turbo: small measured u (including platens at 0).</summary>
    public const string PaletteLegendCool = "rece = u mic masurat / platene u=0";

    /// <summary>Warm end of Turbo: large measured u on the ring.</summary>
    public const string PaletteLegendWarm = "cald = u mare masurat pe inel";

    /// <summary>How the fill is built (ASCII-safe; no dualitate).</summary>
    public const string PaletteLegendRing = "interpolare pe circumferinta intre senzori";

    /// <summary>Tick count on a tall palette strip (min, quarters, max).</summary>
    public const int PaletteLegendTickCount = 5;

    /// <summary>Target visual amplitude of max|u| as a fraction of R₀ (~5%, band 2–8%).</summary>
    public const double ExaggerationTargetFractionOfR0 = 0.05;

    /// <summary>Minimum visual |u|_max / R₀ so Contur Fmax does not sit on top of R₀.</summary>
    public const double MinVisualFractionOfR0 = 0.025;

    /// <summary>Maximum visual |u|_max / R₀ (industrial, not a thick fill).</summary>
    public const double MaxVisualFractionOfR0 = 0.08;

    /// <summary>Legacy hard cap (deformation-curve helper); Contur plot uses the 2–8% band instead.</summary>
    public const double MaxExaggerationFactor = 200.0;

    /// <summary>When false (default), skip ~50% F mid-load overlay.</summary>
    public static bool ShowMidLoadContour { get; set; }

    /// <summary>When false (default), omit true-scale Contur if it sits on top of R₀.</summary>
    public static bool ShowTrueScaleContour { get; set; }

    private static readonly ScottPlot.Color Ink = ScottPlot.Color.FromHex("#1A1A1A");
    private static readonly ScottPlot.Color Dim = ScottPlot.Color.FromHex("#555555");
    private static readonly ScottPlot.Color Muted = ScottPlot.Color.FromHex("#888888");
    private static readonly ScottPlot.Color Border = ScottPlot.Color.FromHex("#333333");
    private static readonly ScottPlot.Color Accent = ScottPlot.Color.FromHex("#2B5F8A"); // steel-blue Contur only
    private static readonly ScottPlot.Color TableBg = ScottPlot.Color.FromHex("#FAFAFA");
    private static readonly ScottPlot.Color PanelFrame = ScottPlot.Color.FromHex("#C5C5C5");
    private static readonly ScottPlot.Color LabelHalo = ScottPlot.Colors.White;
    private static readonly ScottPlot.Colormaps.Turbo BarrelUColormap = new();

    /// <summary>Arial (ScottPlot default) lacks box-drawing, subscripts, angle — those become tofu squares.</summary>
    private const string SansFont = "Segoe UI";
    private const string MonoFont = "Consolas";

    public static string? TrySavePng(
        OfflineSession session,
        ProjectMeta meta,
        string path,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int? cursorA = null,
        int? cursorB = null)
    {
        var result = CylinderContourExport.TryCompute(session, meta, cursorA, cursorB);
        if (result is null || !result.IsValid)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BuildPlot(result).SavePng(path, width, height);
        return path;
    }

    public static string? TrySaveJpeg(
        OfflineSession session,
        ProjectMeta meta,
        string path,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int quality = 92,
        int? cursorA = null,
        int? cursorB = null)
    {
        var result = CylinderContourExport.TryCompute(session, meta, cursorA, cursorB);
        if (result is null || !result.IsValid)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BuildPlot(result).SaveJpeg(path, width, height, quality);
        return path;
    }

    public static ScottPlot.Plot BuildPlot(CylinderContourResult result)
    {
        var plot = new ScottPlot.Plot();
        var inv = CultureInfo.InvariantCulture;

        plot.FigureBackground.Color = ScottPlot.Colors.White;
        plot.DataBackground.Color = ScottPlot.Colors.White;
        try { plot.Font.Set(SansFont); } catch { /* keep default */ }
        plot.Grid.IsVisible = false;
        plot.Axes.Bottom.IsVisible = false;
        plot.Axes.Left.IsVisible = false;
        plot.Axes.Right.IsVisible = false;
        plot.Axes.Top.IsVisible = false;
        plot.Legend.IsVisible = false;
        try { plot.Layout.Frameless(); } catch { /* keep default padding */ }
        plot.Axes.Margins(0, 0);

        if (!result.IsValid || result.UndeformedCircle.Count == 0)
        {
            plot.Title("Contur cilindru - indisponibil");
            var err = plot.Add.Text(result.Error ?? "Fără date contur", 0, 0);
            StyleText(err, SansFont, 14, ScottPlot.Colors.DarkRed);
            err.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            return plot;
        }

        var r0 = result.InitialRadiusMm;
        var exaggerateK = ComputeContourVisualScale(
            r0, result.Sensors.Select(s => s.RadialDisplacementMm));
        var useExaggerate = exaggerateK > 1.05;
        var uniformOffset = double.IsNaN(exaggerateK);
        var kVis = uniformOffset ? 1.0 : (useExaggerate ? exaggerateK : 1.0);

        var visualSensors = BuildVisualSensors(result.Sensors, r0, kVis, uniformOffset);
        var visualSmooth = CylinderContourAnalysis.BuildSmoothPolarContour(visualSensors);

        var maxR = Math.Max(r0, visualSensors.Count > 0
            ? visualSensors.Max(s => Math.Abs(s.RadiusMm))
            : r0);
        if (visualSmooth.Count > 0)
            maxR = Math.Max(maxR, visualSmooth.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)));

        var rMid = Math.Max(r0, visualSensors.Count > 0
            ? visualSensors.Max(s => Math.Abs(s.RadiusMm))
            : r0);
        var sheet = ComputeSheetLayout(r0, maxR, result.InitialLengthMm, rMid);
        var diagramR = sheet.DiagramR;
        var labelOrbit = diagramR * 1.08;

        DrawBorder(plot, sheet.FigLeft, sheet.FigRight, sheet.FigBottom, sheet.FigTop);
        DrawPanelFrame(plot, -sheet.PlanHalf, sheet.PlanHalf, -sheet.PlanHalf, sheet.PlanHalf);
        DrawPanelFrame(
            plot,
            sheet.ElevCx - sheet.ElevHalfW, sheet.ElevCx + sheet.ElevHalfW,
            sheet.ElevCy - sheet.ElevHalfH, sheet.ElevCy + sheet.ElevHalfH);
        DrawPanelFrame(
            plot,
            sheet.SecCx - sheet.SecHalfW, sheet.SecCx + sheet.SecHalfW,
            sheet.SecCy - sheet.SecHalfH, sheet.SecCy + sheet.SecHalfH);

        // R0 — solid black, under Contur so the Fmax ring stays visible
        var circleXs = result.UndeformedCircle.Select(p => p.X).ToArray();
        var circleYs = result.UndeformedCircle.Select(p => p.Y).ToArray();
        var c0 = plot.Add.Scatter(circleXs, circleYs);
        c0.Color = Ink;
        c0.LineWidth = 1.6f;
        c0.MarkerSize = 0;

        if (ShowTrueScaleContour && useExaggerate)
        {
            var trueSmooth = result.SmoothDeformedContour.Count >= 8
                ? result.SmoothDeformedContour
                : result.DeformedPolygon;
            if (trueSmooth.Count >= 2)
            {
                var tipR = trueSmooth.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
                if (tipR > r0 * 1.03)
                {
                    var tx = trueSmooth.Select(p => p.X).ToArray();
                    var ty = trueSmooth.Select(p => p.Y).ToArray();
                    var trueLine = plot.Add.Scatter(tx, ty);
                    trueLine.Color = Muted;
                    trueLine.LineWidth = 0.9f;
                    trueLine.MarkerSize = 0;
                    trueLine.LinePattern = ScottPlot.LinePattern.Dotted;
                }
            }
        }

        if (ShowMidLoadContour && result.MidLoadSmoothContour.Count >= 8)
        {
            var mx = result.MidLoadSmoothContour.Select(p => p.X).ToArray();
            var my = result.MidLoadSmoothContour.Select(p => p.Y).ToArray();
            var midLine = plot.Add.Scatter(mx, my);
            midLine.Color = Dim;
            midLine.LineWidth = 1.0f;
            midLine.MarkerSize = 0;
            midLine.LinePattern = ScottPlot.LinePattern.DenselyDashed;
        }

        // Contur Fmax — thin dashed steel-blue outside R0 (drawn last among rings)
        if (visualSmooth.Count >= 2)
        {
            var cx = visualSmooth.Select(p => p.X).ToArray();
            var cy = visualSmooth.Select(p => p.Y).ToArray();
            var contur = plot.Add.Scatter(cx, cy);
            contur.Color = Accent;
            contur.LineWidth = 2.0f;
            contur.MarkerSize = 0;
            contur.LinePattern = ScottPlot.LinePattern.Dashed;
        }

        // Center crosshair
        var ch = r0 * 0.04;
        AddThinLine(plot, -ch, 0, ch, 0, Ink, 0.9f);
        AddThinLine(plot, 0, -ch, 0, ch, Ink, 0.9f);

        // Dimensions: D across TOP, R0 on LEFT — clear of sensors
        DrawIndustrialDimensions(plot, r0, inv);

        // Sensors: tick + leader + S# only (no u_i / referinta / boxes on drawing)
        DrawSensorLeaders(plot, result.Sensors, visualSensors, r0, labelOrbit, useExaggerate || uniformOffset);

        // One thin u_max direction tick (no arrowhead box, no label)
        DrawUMaxTick(plot, result, visualSensors, r0);

        DrawElevationView(
            plot, result, r0, kVis, uniformOffset,
            sheet.ElevCx, sheet.ElevCy, sheet.ElevHalfW, sheet.ElevHalfH, sheet, inv);

        DrawHalfSectionView(
            plot, result, r0, kVis, uniformOffset,
            sheet.SecCx, sheet.SecCy, sheet.SecHalfW, sheet.SecHalfH, inv);

        DrawLineLegend(plot, sheet);

        var kForNote = uniformOffset ? 0 : exaggerateK;
        DrawSideTable(
            plot, result, sheet,
            useExaggerate || uniformOffset, kForNote, inv, uniformOffset);

        DrawLoadInset(plot, result, sheet, inv);

        DrawCaptionBlock(plot, sheet);

        plot.Axes.SetLimits(sheet.FigLeft, sheet.FigRight, sheet.FigBottom, sheet.FigTop);
        try { plot.Axes.SquareUnits(); } catch { /* ignore */ }

        return plot;
    }

    private static void DrawBorder(ScottPlot.Plot plot, double left, double right, double bottom, double top)
    {
        var pad = Math.Min(right - left, top - bottom) * 0.01;
        var x0 = left + pad;
        var x1 = right - pad;
        var y0 = bottom + pad;
        var y1 = top - pad;
        var outline = plot.Add.Scatter(new[] { x0, x1, x1, x0, x0 }, new[] { y0, y0, y1, y1, y0 });
        outline.Color = Border;
        outline.LineWidth = 1.0f;
        outline.MarkerSize = 0;
    }

    private static void DrawPanelFrame(ScottPlot.Plot plot, double left, double right, double bottom, double top)
    {
        var outline = plot.Add.Scatter(
            new[] { left, right, right, left, left },
            new[] { bottom, bottom, top, top, bottom });
        outline.Color = PanelFrame;
        outline.LineWidth = 0.8f;
        outline.MarkerSize = 0;
    }

    /// <summary>
    /// Left Tabel u_i legend + three-view row + u palette column (elev–section) +
    /// mini load plot under the views.
    /// Elevation/section panels follow padded L/D (true geometry inside).
    /// Tall specimens get a minimum readable width + frame; discs shrink vertical
    /// padding instead of a tall empty shaft. Captions sit under each view
    /// (plan circle above "Fig. Contur radial"). The load curve is a compact box
    /// centered on its own "Fig. {kind}" caption — not flush-right above a
    /// geometry footer. Figure limits match DefaultWidth/Height so SquareUnits
    /// keeps the plan round and the plate (legend + views) centered.
    /// </summary>
    public static ContourSheetLayout ComputeSheetLayout(
        double r0,
        double maxR,
        double l0,
        double rMid = 0)
    {
        if (!(r0 > 0)) r0 = 1;
        if (!(maxR > 0)) maxR = r0;
        if (!(rMid > 0)) rMid = r0;

        var diagramR = Math.Max(maxR * 1.22, r0 * 1.22);
        var planHalf = diagramR * 1.22;
        var planSize = 2.0 * planHalf;
        var viewGap = planSize * 0.14;
        // Extra column between elevation and section: Turbo strip + ticks + description.
        var paletteCol = planSize * 0.32;

        // Same pad factors as TryMapBarrelPanel (dim lines, platens, F arrows).
        var specHalfW = Math.Max(rMid * 1.55, r0 * 0.8);
        var specHalfH = Math.Max((l0 > 1e-9 ? l0 : 2.0 * r0) * 0.58, r0 * 0.25);

        var maxElevH = planSize * 1.10;
        var maxElevW = planSize * 0.88;

        // Fit L×D into the max box; do not stretch one axis (that would fake L or D).
        var s = Math.Min(maxElevH / (2.0 * specHalfH), maxElevW / (2.0 * specHalfW));
        var elevW = 2.0 * specHalfW * s;
        var elevH = 2.0 * specHalfH * s;
        var labelFloorW = planSize * 0.42;
        if (elevW < labelFloorW)
            elevW = labelFloorW;

        var secH = elevH;
        var secW = Math.Clamp(elevW * 0.78, planSize * 0.28, elevW);

        var elevHalfW = elevW * 0.5;
        var elevHalfH = elevH * 0.5;
        var secHalfW = secW * 0.5;
        var secHalfH = secH * 0.5;

        var elevCx = planHalf + viewGap + elevHalfW;
        var paletteLeft = elevCx + elevHalfW;
        var paletteRight = paletteLeft + paletteCol;
        var secCx = paletteRight + secHalfW;

        var viewsLeft = -planHalf;
        var viewsRight = secCx + secHalfW;
        var rowHalf = 0.5 * Math.Max(planSize, Math.Max(elevH, secH));
        var viewsTop = rowHalf;
        var viewsBottom = -rowHalf;
        // Shared top baseline: short (disc) panels hang from viewsTop, not mid-row.
        var elevCy = viewsTop - elevHalfH;
        var secCy = viewsTop - secHalfH;

        // ~1.6× caption font (11pt) in data space; dim labels stay inside panels.
        var captionGap = planSize * 0.10;
        var planCaptionY = -planHalf - captionGap;
        var elevCaptionY = elevCy - elevHalfH - captionGap;
        var secCaptionY = secCy - secHalfH - captionGap;
        var captionFloor = Math.Min(planCaptionY, Math.Min(elevCaptionY, secCaptionY));

        var titleY = viewsTop + planSize * 0.10;
        var legendY = captionFloor - planSize * 0.065;

        var tableGap = planSize * 0.10;
        var tableW = planSize * 0.52;
        var tableRight = viewsLeft - tableGap;
        var tableLeft = tableRight - tableW;
        var tableTop = viewsTop;
        var tableBottom = captionFloor;

        // Compact load plot centered under the three views, caption glued under the box.
        var viewsW = viewsRight - viewsLeft;
        var loadBoxW = viewsW * 0.55;
        var loadCx = 0.5 * (viewsLeft + viewsRight);
        var loadLeft = loadCx - loadBoxW * 0.5;
        var loadRight = loadCx + loadBoxW * 0.5;
        var bandH = planSize * 0.38;
        var bandTop = legendY - planSize * 0.08;
        var bandBottom = bandTop - bandH;
        var loadCaptionY = bandBottom - planSize * 0.10;

        var contentLeft = tableLeft;
        var contentRight = viewsRight;
        var contentTop = titleY + planSize * 0.05;
        var contentBottom = loadCaptionY - planSize * 0.06;

        var contentW = contentRight - contentLeft;
        var contentH = contentTop - contentBottom;
        var pngAspect = DefaultWidth / (double)DefaultHeight;
        var minMargin = 0.03 * Math.Max(contentW, contentH);
        var innerW = contentW + 2.0 * minMargin;
        var innerH = contentH + 2.0 * minMargin;
        double viewW, viewH;
        if (innerW / innerH < pngAspect)
        {
            viewH = innerH;
            viewW = pngAspect * viewH;
        }
        else
        {
            viewW = innerW;
            viewH = viewW / pngAspect;
        }

        var cx = 0.5 * (contentLeft + contentRight);
        var cy = 0.5 * (contentBottom + contentTop);

        return new ContourSheetLayout
        {
            DiagramR = diagramR,
            PlanHalf = planHalf,
            ElevCx = elevCx,
            ElevCy = elevCy,
            ElevHalfW = elevHalfW,
            ElevHalfH = elevHalfH,
            SecCx = secCx,
            SecCy = secCy,
            SecHalfW = secHalfW,
            SecHalfH = secHalfH,
            ViewsLeft = viewsLeft,
            ViewsRight = viewsRight,
            ViewsBottom = viewsBottom,
            ViewsTop = viewsTop,
            BandLeft = loadLeft,
            BandRight = loadRight,
            BandBottom = bandBottom,
            BandTop = bandTop,
            TableLeft = tableLeft,
            TableRight = tableRight,
            TableTop = tableTop,
            TableBottom = tableBottom,
            LoadLeft = loadLeft,
            LoadRight = loadRight,
            LoadCaptionY = loadCaptionY,
            TitleY = titleY,
            PlanCaptionY = planCaptionY,
            ElevCaptionY = elevCaptionY,
            SecCaptionY = secCaptionY,
            LegendY = legendY,
            PaletteLeft = paletteLeft,
            PaletteRight = paletteRight,
            FigLeft = cx - viewW * 0.5,
            FigRight = cx + viewW * 0.5,
            FigBottom = cy - viewH * 0.5,
            FigTop = cy + viewH * 0.5
        };
    }

    private static void DrawCaptionBlock(ScottPlot.Plot plot, ContourSheetLayout sheet)
    {
        var mid = 0.5 * (sheet.TableLeft + sheet.ViewsRight);
        var title = plot.Add.Text(
            "Contur cilindru - plan + elevatie + sectiune",
            mid,
            sheet.TitleY);
        StyleText(title, SansFont, 14, Ink, bold: true);
        title.LabelAlignment = ScottPlot.Alignment.UpperCenter;

        var planLbl = plot.Add.Text("Fig. Contur radial", 0, sheet.PlanCaptionY);
        StyleText(planLbl, SansFont, 11, Ink, bold: true);
        planLbl.LabelAlignment = ScottPlot.Alignment.UpperCenter;

        var elevLbl = plot.Add.Text(ElevationCaption, sheet.ElevCx, sheet.ElevCaptionY);
        StyleText(elevLbl, SansFont, 11, Ink, bold: true);
        elevLbl.LabelAlignment = ScottPlot.Alignment.UpperCenter;

        var secLbl = plot.Add.Text("Fig. Sectiune", sheet.SecCx, sheet.SecCaptionY);
        StyleText(secLbl, SansFont, 11, Ink, bold: true);
        secLbl.LabelAlignment = ScottPlot.Alignment.UpperCenter;
    }

    private static void DrawLineLegend(ScottPlot.Plot plot, ContourSheetLayout sheet)
    {
        var x = (sheet.ViewsLeft + sheet.ViewsRight) * 0.5;
        var body = "--  R0 / L0 nedeformat    - - Contur / profil la Fmax    /// perete (sectiune)    |   directie u_max";
        var leg = plot.Add.Text(body, x, sheet.LegendY);
        StyleText(leg, SansFont, 9, Dim);
        leg.LabelAlignment = ScottPlot.Alignment.UpperCenter;
    }

    private static void DrawIndustrialDimensions(ScottPlot.Plot plot, double r0, CultureInfo inv)
    {
        var diam = r0 * 2.0;

        // Horizontal Ø at top of circle (y = +R₀ * 1.18) — above sensors
        var yDim = r0 * 1.18;
        AddThinLine(plot, -r0, yDim, r0, yDim, Dim, 0.9f);
        // End ticks
        var tick = r0 * 0.035;
        AddThinLine(plot, -r0, yDim - tick, -r0, yDim + tick, Dim, 0.9f);
        AddThinLine(plot, r0, yDim - tick, r0, yDim + tick, Dim, 0.9f);
        // Extension lines from circle to dim line
        AddThinLine(plot, -r0, r0 * 0.92, -r0, yDim, Muted, 0.7f);
        AddThinLine(plot, r0, r0 * 0.92, r0, yDim, Muted, 0.7f);

        // Off the vertical axis (x=0 / 90° leader) so the line cannot cut "D".
        var dLbl = plot.Add.Text($"D = {diam.ToString("0.###", inv)} mm", r0 * 0.32, yDim + r0 * 0.11);
        StyleText(dLbl, SansFont, 10, Dim, halo: true);
        dLbl.LabelAlignment = ScottPlot.Alignment.LowerCenter;

        // R₀ radius on the LEFT (180°) — clear of typical sensor labels
        var rx = -r0;
        AddThinLine(plot, 0, 0, rx, 0, Ink, 0.9f);
        // Arrowhead as small V
        var ah = r0 * 0.04;
        AddThinLine(plot, rx, 0, rx + ah, ah * 0.6, Ink, 0.9f);
        AddThinLine(plot, rx, 0, rx + ah, -ah * 0.6, Ink, 0.9f);

        // Outside the circle, above the radius line (not sitting on the axis).
        var rLbl = plot.Add.Text($"R0 = {r0.ToString("0.###", inv)} mm", -r0 * 1.14, r0 * 0.14);
        StyleText(rLbl, SansFont, 10, Ink, halo: true);
        rLbl.LabelAlignment = ScottPlot.Alignment.MiddleRight;
    }

    private static void DrawSensorLeaders(
        ScottPlot.Plot plot,
        IReadOnlyList<ContourSensorPoint> sensors,
        IReadOnlyList<ContourSensorPoint> visualSensors,
        double r0,
        double labelOrbit,
        bool useExaggerate)
    {
        for (var i = 0; i < sensors.Count; i++)
        {
            var s = sensors[i];
            var v = i < visualSensors.Count ? visualSensors[i] : s;
            var rad = s.AngleDeg * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);

            var x0 = r0 * cos;
            var y0 = r0 * sin;

            // Short outward tick from R₀
            var tickLen = r0 * 0.05;
            AddThinLine(plot, x0, y0, x0 + tickLen * cos, y0 + tickLen * sin, Dim, 1.1f);

            var m = plot.Add.Marker(x0, y0);
            m.Color = Ink;
            m.Size = 6;
            m.LineWidth = 1.2f;
            m.Shape = ScottPlot.MarkerShape.OpenCircle;

            // Tiny Contur tip mark (same ink, not rainbow)
            if (Math.Abs(s.RadialDisplacementMm) > 1e-12 || useExaggerate)
            {
                var tipMk = plot.Add.Marker(v.XMm, v.YMm);
                tipMk.Color = Accent;
                tipMk.Size = 3.5f;
                tipMk.Shape = ScottPlot.MarkerShape.FilledCircle;
            }

            // Leader from slightly outside R₀ to label orbit
            var lead0 = r0 * 1.08;
            var lead1 = labelOrbit;
            AddThinLine(
                plot,
                lead0 * cos, lead0 * sin,
                lead1 * cos, lead1 * sin,
                Muted, 0.8f);

            var sLabel = plot.Add.Text($"S{s.SensorIndex}", lead1 * cos * 1.06, lead1 * sin * 1.06);
            StyleText(sLabel, SansFont, 11, Ink, bold: true, halo: true);
            sLabel.LabelAlignment = AlignmentForAngle(s.AngleDeg);
        }
    }

    private static ScottPlot.Alignment AlignmentForAngle(double angleDeg)
    {
        // Normalize 0..360
        var a = angleDeg % 360.0;
        if (a < 0) a += 360.0;
        // Prefer outward-facing alignment so text sits outside the ring
        if (a >= 315 || a < 45) return ScottPlot.Alignment.MiddleLeft;
        if (a < 135) return ScottPlot.Alignment.LowerCenter;
        if (a < 225) return ScottPlot.Alignment.MiddleRight;
        return ScottPlot.Alignment.UpperCenter;
    }

    private static void DrawUMaxTick(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        IReadOnlyList<ContourSensorPoint> visualSensors,
        double r0)
    {
        ContourSensorPoint? tip = null;
        ContourSensorPoint? trueSensor = null;
        if (result.UMaxSensorIndex > 0)
        {
            trueSensor = result.Sensors.FirstOrDefault(s => s.SensorIndex == result.UMaxSensorIndex);
            tip = visualSensors.FirstOrDefault(s => s.SensorIndex == result.UMaxSensorIndex);
        }
        tip ??= visualSensors.OrderByDescending(s => s.RadialDisplacementMm).FirstOrDefault();
        trueSensor ??= result.Sensors.OrderByDescending(s => s.RadialDisplacementMm).FirstOrDefault();
        if (tip is null || trueSensor is null) return;
        if (Math.Abs(trueSensor.RadialDisplacementMm) < 1e-12) return;

        var rad = tip.AngleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var tipR = Math.Max(Math.Sqrt(tip.XMm * tip.XMm + tip.YMm * tip.YMm), r0 * 1.05);
        var fromR = r0 * 0.55;
        var toR = tipR + r0 * 0.08;
        // Thin ink tick — not a thick red arrow
        AddThinLine(plot, fromR * cos, fromR * sin, toR * cos, toR * sin, Dim, 1.2f);
        // Small end bar (perpendicular)
        var bar = r0 * 0.025;
        AddThinLine(
            plot,
            toR * cos - bar * sin, toR * sin + bar * cos,
            toR * cos + bar * sin, toR * sin - bar * cos,
            Dim, 1.0f);
    }

    private static void DrawElevationView(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        double r0,
        double kVis,
        bool uniformOffset,
        double cx,
        double cy,
        double halfW,
        double halfH,
        ContourSheetLayout sheet,
        CultureInfo inv)
    {
        if (!TryMapBarrelPanel(result, r0, kVis, uniformOffset, cx, cy, halfW, halfH, out var g))
        {
            var miss = plot.Add.Text("L0 nesetat - profil schematic omis", cx, cy);
            StyleText(miss, SansFont, 10, Dim);
            miss.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            return;
        }

        DrawBarrelUFill(plot, g, r0);
        DrawUColorBar(plot, sheet, g, inv);

        var rect = CylinderBarrelGeometry.BuildUndeformedRectangle(r0, g.L0);
        if (rect.Count >= 2)
        {
            var rx = rect.Select(p => g.Tx(p.X)).ToArray();
            var ry = rect.Select(p => g.Ty(p.Y)).ToArray();
            var rLine = plot.Add.Scatter(rx, ry);
            rLine.Color = Ink;
            rLine.LineWidth = 1.4f;
            rLine.MarkerSize = 0;
        }

        var barrel = CylinderBarrelGeometry.BuildBarrelOutline(r0, g.URightVis, g.ULeftVis, g.LVis);
        if (barrel.Count >= 2)
        {
            var bx = barrel.Select(p => g.Tx(p.X)).ToArray();
            var by = barrel.Select(p => g.Ty(p.Y)).ToArray();
            var bLine = plot.Add.Scatter(bx, by);
            bLine.Color = Accent;
            bLine.LineWidth = 2.0f;
            bLine.MarkerSize = 0;
            bLine.LinePattern = ScottPlot.LinePattern.Dashed;
        }

        DrawPressPlatens(plot, g, r0, halfSection: false);
        AddThinLine(plot, g.Tx(0), g.Ty(-g.L0 * 0.04), g.Tx(0), g.Ty(g.L0 * 1.04), Muted, 0.7f);

        DrawPressArrow(plot, g.Tx(0), g.Ty(g.L0) + halfH * 0.10, g.Ty(g.L0) + halfH * 0.02, down: true);
        DrawPressArrow(plot, g.Tx(0), g.Ty(0) - halfH * 0.10, g.Ty(0) - halfH * 0.02, down: false);
        var fTop = plot.Add.Text("F", g.Tx(0) + halfW * 0.16, g.Ty(g.L0) + halfH * 0.08);
        StyleText(fTop, SansFont, 9, Dim, halo: true);
        fTop.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
        var fBot = plot.Add.Text("F", g.Tx(0) + halfW * 0.16, g.Ty(0) - halfH * 0.08);
        StyleText(fBot, SansFont, 9, Dim, halo: true);
        fBot.LabelAlignment = ScottPlot.Alignment.MiddleLeft;

        var xL0 = g.Tx(-r0) - halfW * 0.22;
        AddThinLine(plot, xL0, g.Ty(0), xL0, g.Ty(g.L0), Dim, 0.9f);
        var tick = halfW * 0.04;
        AddThinLine(plot, xL0 - tick, g.Ty(0), xL0 + tick, g.Ty(0), Dim, 0.9f);
        AddThinLine(plot, xL0 - tick, g.Ty(g.L0), xL0 + tick, g.Ty(g.L0), Dim, 0.9f);
        AddThinLine(plot, g.Tx(-r0), g.Ty(0), xL0, g.Ty(0), Muted, 0.7f);
        AddThinLine(plot, g.Tx(-r0), g.Ty(g.L0), xL0, g.Ty(g.L0), Muted, 0.7f);
        var l0Txt = result.LengthIsNominal ? "L0 ~ " : "L0 = ";
        var l0Lbl = plot.Add.Text(l0Txt + g.L0.ToString("0.###", inv) + " mm", xL0 - tick * 3, g.Ty(g.L0 * 0.5));
        StyleText(l0Lbl, SansFont, 9, Dim, halo: true);
        l0Lbl.LabelAlignment = ScottPlot.Alignment.MiddleRight;
        l0Lbl.LabelRotation = 90;

        var xL = g.Tx(g.RMid) + halfW * 0.28;
        AddThinLine(plot, xL, g.Ty(0), xL, g.Ty(g.LVis), Dim, 0.9f);
        AddThinLine(plot, xL - tick, g.Ty(0), xL + tick, g.Ty(0), Dim, 0.9f);
        AddThinLine(plot, xL - tick, g.Ty(g.LVis), xL + tick, g.Ty(g.LVis), Dim, 0.9f);
        var lLbl = plot.Add.Text(
            "L = " + result.DeformedLengthMm.ToString("0.###", inv) + " mm",
            xL + tick * 3,
            g.Ty(g.LVis * 0.5));
        StyleText(lLbl, SansFont, 9, Accent, halo: true);
        lLbl.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
        lLbl.LabelRotation = 90;

        DrawUMaxHeightTick(plot, g, r0);
        DrawEndMidDiameters(plot, result, g, r0, inv);
    }

    /// <summary>
    /// Entire elevation silhouette as one body: each cell is measured u(θ) on the
    /// sensor ring, geometrically faded to u=0 at the platens. Not a left/right slab.
    /// </summary>
    private static void DrawBarrelUFill(ScottPlot.Plot plot, BarrelPanelGeom g, double r0)
    {
        var cells = CylinderBarrelGeometry.BuildBarrelFillCells(
            r0, g.URightVis, g.ULeftVis, g.LVis,
            g.Sensors, g.ViewPlaneDeg);
        if (cells.Count == 0)
            return;

        CylinderBarrelGeometry.BarrelUColorRange(g.Sensors, out var uLo, out var uHi);
        foreach (var c in cells)
        {
            var y0 = g.Ty(c.Y0);
            var y1 = g.Ty(c.Y1);
            AddFilledQuad(
                plot,
                g.Tx(c.XLeft0), y0,
                g.Tx(c.XRight0), y0,
                g.Tx(c.XRight1), y1,
                g.Tx(c.XLeft1), y1,
                ColorForUNorm(ColorNormU(c.U, uLo, uHi)));
        }
    }

    /// <summary>
    /// Same u(z) Turbo fill on the +X wall (cut face), inner generator to outer.
    /// Hatch stays on top so the section still reads as a cut, not a 3D scan.
    /// </summary>
    private static void DrawHalfSectionUFill(ScottPlot.Plot plot, BarrelPanelGeom g, double r0, double ri)
    {
        var bands = CylinderBarrelGeometry.BuildBarrelFillBands(
            r0, g.URightVis, g.ULeftVis, g.LVis,
            CylinderBarrelGeometry.DefaultFillBandCount);
        if (bands.Count == 0)
            return;

        CylinderBarrelGeometry.BarrelUColorRange(g.Sensors, out var uLo, out var uHi);
        var inner = ri > 0 && ri < r0 ? ri : 0.0;
        var uInnerVis = inner > 0 ? g.URightVis * (inner / r0) : 0.0;
        foreach (var b in bands)
        {
            var y0 = g.Ty(b.Y0);
            var y1 = g.Ty(b.Y1);
            var xo0 = g.Tx(b.XRight0);
            var xo1 = g.Tx(b.XRight1);
            var xi0 = g.Tx(inner > 0
                ? CylinderBarrelGeometry.RadiusAtHeight(inner, uInnerVis, b.Y0, g.LVis)
                : 0.0);
            var xi1 = g.Tx(inner > 0
                ? CylinderBarrelGeometry.RadiusAtHeight(inner, uInnerVis, b.Y1, g.LVis)
                : 0.0);
            var u = CylinderBarrelGeometry.DisplacementAtHeight(g.URightTrue, b.YMid, g.LVis);
            AddFilledQuad(plot, xi0, y0, xo0, y0, xo1, y1, xi1, y1, ColorForUNorm(ColorNormU(u, uLo, uHi)));
        }
    }

    /// <summary>
    /// Turbo palette legend in the column between elevation and section:
    /// enlarged vertical strip (same 120-band fill as the heatmap), ticks to the
    /// right of the strip (true u [mm], white halo), title + 3-line key under it.
    /// Stays off D_capat (inside the panel) and Fig. Elevatie (centered under elev).
    /// </summary>
    private static void DrawUColorBar(
        ScottPlot.Plot plot,
        ContourSheetLayout sheet,
        BarrelPanelGeom g,
        CultureInfo inv)
    {
        CylinderBarrelGeometry.BarrelUColorRange(g.Sensors, out var uLo, out var uHi);

        var colL = sheet.PaletteLeft;
        var colR = sheet.PaletteRight;
        var colW = colR - colL;
        if (colW < 1e-12)
        {
            colL = sheet.ElevCx + sheet.ElevHalfW;
            colR = sheet.SecCx - sheet.SecHalfW;
            colW = colR - colL;
        }
        if (colW < 1e-12)
            return;

        var pad = colW * 0.08;
        var cbW = Math.Clamp(colW * 0.28, colW * 0.18, colW * 0.36);
        var cbL = colL + pad;
        var cbR = cbL + cbW;
        var cbB = g.Ty(0);
        var cbT = g.Ty(g.LVis);
        if (cbT < cbB) (cbT, cbB) = (cbB, cbT);
        var h = cbT - cbB;
        if (h < 1e-12 || cbR <= cbL)
            return;

        // 3-line key under the strip, in the palette column (not on D_capat / Fig. Elevatie).
        var planSize = 2.0 * sheet.PlanHalf;
        var lineH = planSize * 0.030;
        var descH = lineH * 3.6;
        var floorY = sheet.LegendY + lineH * 0.35;
        var minStripH = h * 0.50;
        var neededB = floorY + descH + lineH * 0.20;
        if (cbB < neededB)
            cbB = Math.Min(neededB, cbT - minStripH);
        h = cbT - cbB;
        if (h < 1e-12)
            return;

        var n = CylinderBarrelGeometry.DefaultFillBandCount;
        var overlap = CylinderBarrelGeometry.FillBandOverlapFraction / n;
        for (var i = 0; i < n; i++)
        {
            var t0 = Math.Max(0, i / (double)n - overlap);
            var t1 = Math.Min(1, (i + 1) / (double)n + overlap);
            var y0 = cbB + t0 * h;
            var y1 = cbB + t1 * h;
            var c = ColorForUNorm((i + 0.5) / n);
            AddFilledQuad(plot, cbL, y0, cbR, y0, cbR, y1, cbL, y1, c);
        }

        AddThinLine(plot, cbL, cbB, cbR, cbB, Ink, 0.9f);
        AddThinLine(plot, cbR, cbB, cbR, cbT, Ink, 0.9f);
        AddThinLine(plot, cbR, cbT, cbL, cbT, Ink, 0.9f);
        AddThinLine(plot, cbL, cbT, cbL, cbB, Ink, 0.9f);

        var title = plot.Add.Text(PaletteLegendTitle, cbL, cbT + h * 0.045);
        StyleText(title, SansFont, 9, Ink, bold: true, halo: true);
        title.LabelAlignment = ScottPlot.Alignment.LowerLeft;

        var ticks = PaletteTickValues(uLo, uHi, h >= lineH * 6 ? PaletteLegendTickCount : 3);
        var tickGap = (cbR - cbL) * 0.40;
        foreach (var (frac, u) in ticks)
            DrawColorBarTick(plot, cbL, cbR, cbB + frac * h, u, inv, tickGap);

        var descY = cbB - lineH * 0.22;
        var desc = plot.Add.Text(FormatPaletteLegendDescription(), cbL, descY);
        StyleText(desc, SansFont, 7, Dim, halo: true);
        desc.LabelAlignment = ScottPlot.Alignment.UpperLeft;
        desc.LabelPadding = 5;
    }

    /// <summary>
    /// Palette key under the Turbo strip (ASCII, newlines). Title is drawn separately.
    /// </summary>
    public static string FormatPaletteLegendDescription()
        => PaletteLegendCool + "\n" + PaletteLegendWarm + "\n" + PaletteLegendRing;

    /// <summary>
    /// True-u tick positions along the strip, bottom (u min) to top (u max).
    /// Always includes min, mid, max; 5 ticks add the quarters when the bar is tall.
    /// </summary>
    public static IReadOnlyList<(double Fraction, double UMm)> PaletteTickValues(
        double uLo, double uHi, int count = PaletteLegendTickCount)
    {
        count = Math.Clamp(count, 2, 9);
        if (!(uHi > uLo) || double.IsNaN(uLo) || double.IsNaN(uHi))
            return new[] { (0.0, double.IsFinite(uLo) ? uLo : 0.0) };

        var list = new (double Fraction, double UMm)[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            list[i] = (t, uLo + t * (uHi - uLo));
        }
        return list;
    }

    public static string FormatPaletteTickLabel(double uMm, CultureInfo? culture = null)
        => uMm.ToString("0.###", culture ?? CultureInfo.InvariantCulture);

    private static void DrawColorBarTick(
        ScottPlot.Plot plot,
        double cbL, double cbR, double y,
        double u,
        CultureInfo inv,
        double tickLen)
    {
        AddThinLine(plot, cbR - tickLen, y, cbR, y, Ink, 0.9f);
        var xLbl = cbR + (cbR - cbL) * 0.28;
        var lbl = plot.Add.Text(FormatPaletteTickLabel(u, inv), xLbl, y);
        StyleText(lbl, MonoFont, 8, Ink, halo: true);
        lbl.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
        lbl.LabelPadding = 4;
    }

    private static double ColorNormU(double u, double uLo, double uHi)
    {
        var span = uHi - uLo;
        return span > 1e-15 ? (u - uLo) / span : 0.0;
    }

    private static ScottPlot.Color ColorForUNorm(double t)
        => BarrelUColormap.GetColor(Math.Clamp(t, 0, 1));

    private static void AddFilledQuad(
        ScottPlot.Plot plot,
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        ScottPlot.Color fill)
    {
        var poly = plot.Add.Polygon(new ScottPlot.Coordinates[]
        {
            new(x0, y0), new(x1, y1), new(x2, y2), new(x3, y3)
        });
        poly.FillColor = fill;
        poly.LineColor = fill;
        // Same-color stroke + overlapping bands hides 1 px polygon seams.
        poly.LineWidth = 1.0f;
    }

    private static void DrawPressArrow(ScottPlot.Plot plot, double x, double fromY, double toY, bool down)
    {
        AddThinLine(plot, x, fromY, x, toY, Dim, 1.1f);
        var head = Math.Abs(toY - fromY) * 0.35;
        var dir = down ? -1.0 : 1.0;
        // V head at the tip (toY)
        AddThinLine(plot, x, toY, x - head * 0.45, toY - dir * head, Dim, 1.0f);
        AddThinLine(plot, x, toY, x + head * 0.45, toY - dir * head, Dim, 1.0f);
    }

    /// <summary>
    /// Thick platen bars at y=0 and y=L (friction → ends stay near R0).
    /// </summary>
    private static void DrawPressPlatens(
        ScottPlot.Plot plot, BarrelPanelGeom g, double r0, bool halfSection)
    {
        var span = Math.Max(r0, g.RMid) * 1.20;
        var x0 = halfSection ? g.Tx(-span * 0.12) : g.Tx(-span);
        var x1 = g.Tx(span);
        AddThinLine(plot, x0, g.Ty(0), x1, g.Ty(0), Ink, 2.8f);
        AddThinLine(plot, x0, g.Ty(g.LVis), x1, g.Ty(g.LVis), Ink, 2.8f);
    }

    /// <summary>
    /// One pair of industrial diameters on elevation: D_capat (ends ≈ D0) and D_mijloc (mid bulge).
    /// Labels use true mm; dim lines follow the visual (possibly exaggerated) generators.
    /// </summary>
    private static void DrawEndMidDiameters(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        BarrelPanelGeom g,
        double r0,
        CultureInfo inv)
    {
        CylinderBarrelGeometry.EndAndMidDiametersMm(
            r0, result.Sensors, result.UMeanMm, result.UMaxAngleDeg,
            out var dCapat, out var dMijloc);

        var h = Math.Abs(g.Ty(g.L0) - g.Ty(0));
        // Cap vs height so a large-D disc does not fire ticks/labels out of the panel.
        var tick = Math.Min(Math.Max(h * 0.015, r0 * g.Scale * 0.04), h * 0.04);

        // Inside the panel, just above the bottom platen — not hanging onto the Fig. caption.
        var yCap = g.Ty(0) + h * 0.12;
        var xCapL = g.Tx(-r0);
        var xCapR = g.Tx(r0);
        AddThinLine(plot, xCapL, yCap, xCapR, yCap, Dim, 0.9f);
        AddThinLine(plot, xCapL, yCap - tick, xCapL, yCap + tick, Dim, 0.9f);
        AddThinLine(plot, xCapR, yCap - tick, xCapR, yCap + tick, Dim, 0.9f);
        AddThinLine(plot, xCapL, g.Ty(0), xCapL, yCap, Muted, 0.6f);
        AddThinLine(plot, xCapR, g.Ty(0), xCapR, yCap, Muted, 0.6f);
        var capLbl = plot.Add.Text(
            "D_capat = " + dCapat.ToString("0.###", inv) + " mm",
            (xCapL + xCapR) * 0.5 + (xCapR - xCapL) * 0.18,
            yCap + tick * 3.2);
        StyleText(capLbl, SansFont, 8, Dim, halo: true);
        capLbl.LabelAlignment = ScottPlot.Alignment.LowerCenter;

        var yMid = g.Ty(g.LVis * 0.58);
        var xMidL = g.Tx(-(r0 + g.ULeftVis));
        var xMidR = g.Tx(r0 + g.URightVis);
        AddThinLine(plot, xMidL, yMid, xMidR, yMid, Dim, 0.9f);
        AddThinLine(plot, xMidL, yMid - tick, xMidL, yMid + tick, Dim, 0.9f);
        AddThinLine(plot, xMidR, yMid - tick, xMidR, yMid + tick, Dim, 0.9f);
        // Above the dim line, in the right half — clear of the axis (through "=") and of L.
        var xAxis = g.Tx(0);
        var midLbl = plot.Add.Text(
            "D_mijloc = " + dMijloc.ToString("0.###", inv) + " mm",
            xAxis + (xMidR - xAxis) * 0.55,
            yMid + tick * 3.2);
        StyleText(midLbl, SansFont, 8, Accent, halo: true);
        midLbl.LabelAlignment = ScottPlot.Alignment.LowerCenter;
    }

    private readonly struct BarrelPanelGeom
    {
        public double L0 { get; init; }
        public double LVis { get; init; }
        public double R0 { get; init; }
        public double URightVis { get; init; }
        public double ULeftVis { get; init; }
        public double URightTrue { get; init; }
        public double ULeftTrue { get; init; }
        public IReadOnlyList<ContourSensorPoint>? Sensors { get; init; }
        public double ViewPlaneDeg { get; init; }
        public double RMid { get; init; }
        public double Scale { get; init; }
        public double Cx { get; init; }
        public double Cy { get; init; }
        public double XAlign { get; init; }
        public double Tx(double x) => Cx + (x - XAlign) * Scale;
        public double Ty(double y) => Cy + (y - L0 * 0.5) * Scale;
    }

    /// <summary>
    /// Fit undeformed L0 x D0 into a panel. +X is the u_max diametral plane
    /// (right generator); fill color is the whole visible face from measured u(θ).
    /// </summary>
    private static bool TryMapBarrelPanel(
        CylinderContourResult result,
        double r0,
        double kVis,
        bool uniformOffset,
        double cx,
        double cy,
        double halfW,
        double halfH,
        out BarrelPanelGeom g,
        bool centerHalfSection = false)
    {
        g = default;
        var l0 = result.InitialLengthMm;
        if (!(l0 > 0) || !(r0 > 0))
            return false;

        var absStroke = result.StrokeAtIndex is double st && double.IsFinite(st) ? Math.Abs(st) : 0.0;
        var kAx = CylinderBarrelGeometry.ComputeAxialVisualScale(l0, absStroke);
        var lVis = CylinderBarrelGeometry.DeformedHeightMm(l0, absStroke * kAx);

        var uMean = double.IsFinite(result.UMeanMm) ? result.UMeanMm : 0.0;
        var plane = double.IsFinite(result.UMaxAngleDeg) ? result.UMaxAngleDeg : 0.0;
        var uRightTrue = CylinderBarrelGeometry.UAtAngleDeg(result.Sensors, plane, uMean);
        var uLeftTrue = CylinderBarrelGeometry.UAtAngleDeg(result.Sensors, plane + 180.0, uMean);
        double uRightVis, uLeftVis;
        if (uniformOffset)
        {
            var uOff = MinVisualFractionOfR0 * r0;
            uRightVis = uOff;
            uLeftVis = uOff;
        }
        else
        {
            uRightVis = uRightTrue * kVis;
            uLeftVis = uLeftTrue * kVis;
        }

        var rMid = Math.Max(r0, Math.Max(r0 + uRightVis, r0 + uLeftVis));
        var localHalfW = rMid * 1.55;
        var localHalfH = l0 * 0.58;
        var scale = Math.Min(halfW / Math.Max(localHalfW, 1e-9), halfH / Math.Max(localHalfH, 1e-9));
        g = new BarrelPanelGeom
        {
            L0 = l0,
            LVis = lVis,
            R0 = r0,
            URightVis = uRightVis,
            ULeftVis = uLeftVis,
            URightTrue = uRightTrue,
            ULeftTrue = uLeftTrue,
            Sensors = result.Sensors,
            ViewPlaneDeg = plane,
            RMid = rMid,
            Scale = scale,
            Cx = cx,
            Cy = cy,
            XAlign = centerHalfSection ? rMid * 0.42 : 0.0
        };
        return true;
    }

    private static void DrawUMaxHeightTick(ScottPlot.Plot plot, BarrelPanelGeom g, double r0)
    {
        if (Math.Abs(g.URightVis) < 1e-12)
            return;
        var y = g.LVis * 0.5;
        var x0 = r0;
        var x1 = r0 + g.URightVis;
        AddThinLine(plot, g.Tx(x0), g.Ty(y), g.Tx(x1), g.Ty(y), Dim, 1.2f);
        var bar = r0 * 0.04;
        AddThinLine(plot, g.Tx(x1), g.Ty(y - bar), g.Tx(x1), g.Ty(y + bar), Dim, 1.0f);
    }

    private static void DrawHalfSectionView(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        double r0,
        double kVis,
        bool uniformOffset,
        double cx,
        double cy,
        double halfW,
        double halfH,
        CultureInfo inv)
    {
        if (!TryMapBarrelPanel(result, r0, kVis, uniformOffset, cx, cy, halfW, halfH, out var g, centerHalfSection: true))
            return;

        var ri = result.InnerRadiusMm;
        DrawHalfSectionUFill(plot, g, r0, ri);

        var wall = CylinderBarrelGeometry.BuildHalfSectionWallPolygon(r0, g.URightVis, ri, g.LVis);
        if (wall.Count >= 2)
        {
            var wx = wall.Select(p => g.Tx(p.X)).ToArray();
            var wy = wall.Select(p => g.Ty(p.Y)).ToArray();
            var wLine = plot.Add.Scatter(wx, wy);
            wLine.Color = Accent;
            wLine.LineWidth = 1.8f;
            wLine.MarkerSize = 0;
            wLine.LinePattern = ScottPlot.LinePattern.Dashed;
        }

        // Undeformed half-rectangle (cut face)
        AddThinLine(plot, g.Tx(0), g.Ty(0), g.Tx(r0), g.Ty(0), Ink, 1.2f);
        AddThinLine(plot, g.Tx(r0), g.Ty(0), g.Tx(r0), g.Ty(g.L0), Ink, 1.2f);
        AddThinLine(plot, g.Tx(r0), g.Ty(g.L0), g.Tx(0), g.Ty(g.L0), Ink, 1.2f);
        AddThinLine(plot, g.Tx(0), g.Ty(g.L0), g.Tx(0), g.Ty(0), Ink, 1.2f);

        var hatch = CylinderBarrelGeometry.BuildHalfSectionHatch(r0, g.URightVis, ri, g.LVis, 14);
        foreach (var (x0, y, x1) in hatch)
            AddThinLine(plot, g.Tx(x0), g.Ty(y), g.Tx(x1), g.Ty(y), Muted, 0.7f);

        DrawPressPlatens(plot, g, r0, halfSection: true);

        DrawUMaxHeightTick(plot, g, r0);

        var note = ri > 0
            ? "ID = " + (ri * 2.0).ToString("0.###", inv) + " mm"
            : "solid";
        var nLbl = plot.Add.Text(note, g.Tx(g.RMid) + r0 * g.Scale * 0.12, g.Ty(g.LVis * 0.42));
        StyleText(nLbl, SansFont, 8, Dim, halo: true);
        nLbl.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
    }

    private static void DrawLoadInset(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        ContourSheetLayout sheet,
        CultureInfo inv)
    {
        if (!TryBuildLoadCurve(result, out var xs, out var ys, out var xLabel, out var yLabel, out var title, out var markI))
            return;

        var boxL = sheet.LoadLeft;
        var boxR = sheet.LoadRight;
        var boxB = sheet.BandBottom;
        var boxT = sheet.BandTop;
        if (boxR - boxL < sheet.DiagramR * 0.25 || boxT - boxB < sheet.DiagramR * 0.25)
            return;

        var frame = plot.Add.Scatter(
            new[] { boxL, boxR, boxR, boxL, boxL },
            new[] { boxB, boxB, boxT, boxT, boxB });
        frame.Color = Border;
        frame.LineWidth = 0.9f;
        frame.MarkerSize = 0;

        var padX = (boxR - boxL) * 0.14;
        var padY = (boxT - boxB) * 0.16;
        var x0 = boxL + padX;
        var x1 = boxR - padX * 0.35;
        var y0 = boxB + padY;
        var y1 = boxT - padY;

        double minX = xs[0], maxX = xs[0], minY = ys[0], maxY = ys[0];
        for (var i = 1; i < xs.Length; i++)
        {
            if (xs[i] < minX) minX = xs[i];
            if (xs[i] > maxX) maxX = xs[i];
            if (ys[i] < minY) minY = ys[i];
            if (ys[i] > maxY) maxY = ys[i];
        }
        if (Math.Abs(maxX - minX) < 1e-15) { minX -= 1; maxX += 1; }
        if (Math.Abs(maxY - minY) < 1e-15) { minY -= 1; maxY += 1; }

        AddThinLine(plot, x0, y0, x1, y0, Dim, 0.9f);
        AddThinLine(plot, x0, y0, x0, y1, Dim, 0.9f);

        var px = new double[xs.Length];
        var py = new double[ys.Length];
        for (var i = 0; i < xs.Length; i++)
        {
            px[i] = x0 + (xs[i] - minX) / (maxX - minX) * (x1 - x0);
            py[i] = y0 + (ys[i] - minY) / (maxY - minY) * (y1 - y0);
        }
        var curve = plot.Add.Scatter(px, py);
        curve.Color = Accent;
        curve.LineWidth = 1.4f;
        curve.MarkerSize = 0;

        if (markI >= 0 && markI < px.Length)
        {
            AddThinLine(plot, px[markI], y0, px[markI], y1, Muted, 0.8f);
            var mk = plot.Add.Marker(px[markI], py[markI]);
            mk.Color = Ink;
            mk.Size = 7;
            mk.Shape = ScottPlot.MarkerShape.FilledCircle;
        }

        var ttl = plot.Add.Text(title, (boxL + boxR) * 0.5, boxT - (boxT - boxB) * 0.04);
        StyleText(ttl, SansFont, 8, Dim);
        ttl.LabelAlignment = ScottPlot.Alignment.UpperCenter;
        var xl = plot.Add.Text(xLabel, (x0 + x1) * 0.5, boxB + (boxT - boxB) * 0.06);
        StyleText(xl, SansFont, 7, Muted);
        xl.LabelAlignment = ScottPlot.Alignment.LowerCenter;
        if (!string.IsNullOrEmpty(yLabel))
        {
            var yl = plot.Add.Text(yLabel, boxL + (boxR - boxL) * 0.03, (y0 + y1) * 0.5);
            StyleText(yl, SansFont, 7, Muted);
            yl.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            yl.LabelRotation = 90;
        }
        var idxI = Math.Clamp(markI, 0, px.Length - 1);
        var idxLbl = plot.Add.Text(
            "idx " + result.SampleIndex.ToString(inv),
            px[idxI],
            py[idxI] + (y1 - y0) * 0.10);
        StyleText(idxLbl, SansFont, 7, Dim, halo: true);
        idxLbl.LabelAlignment = ScottPlot.Alignment.LowerCenter;

        var capX = 0.5 * (boxL + boxR);
        var figCap = plot.Add.Text(FormatLoadFigureCaption(title), capX, sheet.LoadCaptionY);
        StyleText(figCap, SansFont, 11, Ink, bold: true);
        figCap.LabelAlignment = ScottPlot.Alignment.UpperCenter;
    }

    /// <summary>Figure caption glued under the mini load plot, matching TryBuildLoadCurve title.</summary>
    public static string FormatLoadFigureCaption(string curveTitle)
        => "Fig. " + SanitizePlotText(curveTitle);

    private static bool TryBuildLoadCurve(
        CylinderContourResult result,
        out double[] xs,
        out double[] ys,
        out string xLabel,
        out string yLabel,
        out string title,
        out int markerIndex)
    {
        xs = Array.Empty<double>();
        ys = Array.Empty<double>();
        xLabel = "";
        yLabel = "";
        title = "";
        markerIndex = -1;

        var force = result.ForceSeries;
        var stroke = result.StrokeSeries;
        var n = Math.Max(force.Count, stroke.Count);
        if (n < 2)
            return false;

        const int maxPts = 360;
        var a0 = CylinderBarrelGeometry.CrossSectionAreaMm2(result.InitialRadiusMm, result.InnerRadiusMm);
        var l0 = result.InitialLengthMm;
        var canStressStrain = force.Count > 1 && stroke.Count > 1 && a0 > 0 && l0 > 0;

        IReadOnlyList<double> xSrc;
        IReadOnlyList<double> ySrc;
        if (canStressStrain)
        {
            var nSE = Math.Min(force.Count, stroke.Count);
            var eps = new double[nSE];
            var sig = new double[nSE];
            for (var i = 0; i < nSE; i++)
            {
                var s = stroke[i];
                var f = force[i];
                eps[i] = double.IsFinite(s) ? Math.Abs(s) / l0 : double.NaN;
                sig[i] = double.IsFinite(f) ? Math.Abs(f) / a0 : double.NaN;
            }
            xSrc = eps;
            ySrc = sig;
            xLabel = "eps [-]";
            yLabel = "sigma [MPa]";
            title = "sigma vs eps";
        }
        else
        {
            var kind = result.LoadCurveKind;
            if (string.IsNullOrEmpty(kind))
                kind = CylinderBarrelGeometry.ResolveLoadCurveKind(force.Count > 1, stroke.Count > 1);
            if (kind.Length == 0)
                return false;

            if (kind == "F vs cursa" || (force.Count > 1 && stroke.Count > 1))
            {
                xSrc = stroke;
                ySrc = force;
                xLabel = "cursa [mm]";
                yLabel = "F [N]";
                title = "F vs cursa";
            }
            else if (kind == "F vs index" || force.Count > 1)
            {
                xSrc = IndexAxis(force.Count);
                ySrc = force;
                xLabel = "index";
                yLabel = "F [N]";
                title = "F vs index";
            }
            else
            {
                xSrc = IndexAxis(stroke.Count);
                ySrc = stroke;
                xLabel = "index";
                yLabel = "cursa [mm]";
                title = "cursa vs index";
            }
        }

        var count = Math.Min(xSrc.Count, ySrc.Count);
        if (count < 2)
            return false;
        var keep = Math.Clamp(result.SampleIndex, 0, count - 1);
        DownsampleXY(xSrc, ySrc, count, maxPts, keep, out xs, out ys, out markerIndex);
        return xs.Length >= 2;
    }

    private static double[] IndexAxis(int n)
    {
        var a = new double[n];
        for (var i = 0; i < n; i++) a[i] = i;
        return a;
    }

    private static void DownsampleXY(
        IReadOnlyList<double> xSrc,
        IReadOnlyList<double> ySrc,
        int count,
        int maxPts,
        int keepIndex,
        out double[] xs,
        out double[] ys,
        out int markerIndex)
    {
        if (count <= maxPts)
        {
            xs = new double[count];
            ys = new double[count];
            var k = 0;
            for (var i = 0; i < count; i++)
            {
                var xv = xSrc[i];
                var yv = ySrc[i];
                if (double.IsNaN(xv) || double.IsInfinity(xv) || double.IsNaN(yv) || double.IsInfinity(yv))
                    continue;
                xs[k] = xv;
                ys[k] = yv;
                k++;
            }
            if (k < count)
            {
                Array.Resize(ref xs, k);
                Array.Resize(ref ys, k);
            }
            markerIndex = Math.Clamp(keepIndex, 0, Math.Max(0, xs.Length - 1));
            return;
        }

        var stride = Math.Max(1, (count - 1) / (maxPts - 2));
        var listX = new List<double>(maxPts);
        var listY = new List<double>(maxPts);
        markerIndex = 0;
        for (var i = 0; i < count; i += stride)
        {
            var xv = xSrc[i];
            var yv = ySrc[i];
            if (double.IsNaN(xv) || double.IsInfinity(xv) || double.IsNaN(yv) || double.IsInfinity(yv))
                continue;
            if (i == keepIndex) markerIndex = listX.Count;
            listX.Add(xv);
            listY.Add(yv);
        }
        if (keepIndex % stride != 0 && keepIndex < count)
        {
            var xv = xSrc[keepIndex];
            var yv = ySrc[keepIndex];
            if (!(double.IsNaN(xv) || double.IsInfinity(xv) || double.IsNaN(yv) || double.IsInfinity(yv)))
            {
                var insertAt = 0;
                while (insertAt < listX.Count && listX[insertAt] < xv) insertAt++;
                // Keep sample-index order: splice at keepIndex/stride.
                insertAt = Math.Min(listX.Count, keepIndex / stride + 1);
                listX.Insert(insertAt, xv);
                listY.Insert(insertAt, yv);
                markerIndex = insertAt;
            }
        }
        if (listX.Count == 0 || (count - 1) % stride != 0)
        {
            var last = count - 1;
            var xv = xSrc[last];
            var yv = ySrc[last];
            if (!(double.IsNaN(xv) || double.IsInfinity(xv) || double.IsNaN(yv) || double.IsInfinity(yv)))
            {
                if (listX.Count == 0 || listX[^1] != xv || listY[^1] != yv)
                {
                    listX.Add(xv);
                    listY.Add(yv);
                }
            }
        }
        xs = listX.ToArray();
        ys = listY.ToArray();
        markerIndex = Math.Clamp(markerIndex, 0, Math.Max(0, xs.Length - 1));
    }

    private static void DrawSideTable(
        ScottPlot.Plot plot,
        CylinderContourResult result,
        ContourSheetLayout sheet,
        bool useExaggerate,
        double exaggerateK,
        CultureInfo inv,
        bool uniformOffset = false)
    {
        var body = FormatSideTable(result, useExaggerate, exaggerateK, inv, uniformOffset);
        var tx = sheet.TableLeft + sheet.DiagramR * 0.03;
        var ty = sheet.TableTop - sheet.DiagramR * 0.02;
        var table = plot.Add.Text(body, tx, ty);
        StyleText(table, MonoFont, TableFontSize, Ink);
        table.LabelAlignment = ScottPlot.Alignment.UpperLeft;
        table.LabelBackgroundColor = TableBg;
        table.LabelBorderColor = Border;
        table.LabelBorderWidth = 1.2f;
        table.LabelPadding = 12;
    }

    /// <summary>
    /// ASCII-only table body for ScottPlot (Arial/default lacks box-drawing and subscripts).
    /// Separators are U+002D hyphen-minus generated at runtime — never U+2500.
    /// </summary>
    public static string FormatSideTable(
        CylinderContourResult result,
        bool useExaggerate,
        double exaggerateK,
        CultureInfo? culture = null,
        bool uniformOffset = false)
    {
        var inv = culture ?? CultureInfo.InvariantCulture;
        var sep = new string('-', 26); // U+002D only; do not type box-drawing in source
        var sb = new StringBuilder();
        sb.AppendLine("Tabel u_i");
        sb.AppendLine();
        sb.AppendLine("S#     deg        u [mm]");
        sb.AppendLine(sep);
        sb.AppendLine();
        foreach (var s in result.Sensors.OrderBy(x => x.SensorIndex))
        {
            var mark = s.SensorIndex == result.UMaxSensorIndex ? " *" : "";
            var u = s.RadialDisplacementMm.ToString("0.####", inv).PadLeft(10);
            var a = s.AngleDeg.ToString("0.#", inv).PadLeft(7);
            sb.AppendLine($"S{s.SensorIndex,-4}{a}  {u}{mark}");
            sb.AppendLine();
        }
        sb.AppendLine(sep);
        sb.AppendLine();
        if (double.IsFinite(result.UMaxMm) && result.UMaxSensorIndex > 0)
            sb.AppendLine($"* u_max = S{result.UMaxSensorIndex}  {result.UMaxMm.ToString("0.####", inv)} mm");
        if (double.IsFinite(result.UMinMm))
            sb.AppendLine($"  u_min = {result.UMinMm.ToString("0.####", inv)} mm");
        if (double.IsFinite(result.UMeanMm))
            sb.AppendLine($"  u_med = {result.UMeanMm.ToString("0.####", inv)} mm");
        sb.AppendLine($"  ovalitate = {result.OvalityMm.ToString("0.####", inv)} mm");
        if (double.IsFinite(result.BarrelingIndex))
            sb.AppendLine($"  bombare = {result.BarrelingIndex.ToString("0.####", inv)}");
        else
            sb.AppendLine("  bombare = -");
        sb.AppendLine($"  idx Fmax = {result.SampleIndex}");
        if (result.StrokeAtIndex is double st)
            sb.AppendLine($"  cursa = {st.ToString("0.###", inv)} mm");
        if (result.ForceAtIndex is double f)
            sb.AppendLine($"  F = {f.ToString("0.#", inv)}");
        if (result.InitialLengthMm > 0)
        {
            sb.AppendLine($"  L0 = {result.InitialLengthMm.ToString("0.###", inv)} mm");
            if (result.DeformedLengthMm > 0)
                sb.AppendLine($"  L  = {result.DeformedLengthMm.ToString("0.###", inv)} mm");
            if (result.StrokeAtIndex is double stDl && double.IsFinite(stDl))
                sb.AppendLine($"  dL = {Math.Abs(stDl).ToString("0.###", inv)} mm");
        }
        CylinderBarrelGeometry.EndAndMidDiametersMm(
            result.InitialRadiusMm, result.Sensors, result.UMeanMm, result.UMaxAngleDeg,
            out var dCapat, out var dMijloc);
        if (dCapat > 0)
            sb.AppendLine($"  D_capat = {dCapat.ToString("0.###", inv)} mm");
        if (dMijloc > 0)
            sb.AppendLine($"  D_mijloc = {dMijloc.ToString("0.###", inv)} mm");
        var a0 = CylinderBarrelGeometry.CrossSectionAreaMm2(result.InitialRadiusMm, result.InnerRadiusMm);
        if (a0 > 0)
            sb.AppendLine($"  A0 = {a0.ToString("0.##", inv)} mm2");
        if (result.InnerRadiusMm > 0)
            sb.AppendLine($"  ID = {(result.InnerRadiusMm * 2).ToString("0.###", inv)} mm");
        else
            sb.AppendLine("  ID = solid");
        if (!string.IsNullOrEmpty(result.LoadCurveKind))
            sb.AppendLine("  load: " + SanitizePlotText(result.LoadCurveKind));

        sb.AppendLine();
        sb.AppendLine("Note");
        sb.AppendLine();
        sb.AppendLine("plan + elevatie + sectiune");
        sb.AppendLine(BarrelMapLegendLine1);
        sb.AppendLine(BarrelMapLegendLine2);
        sb.AppendLine("S1 = referinta / fata");
        if (result.InitialLengthMm > 0)
            sb.AppendLine("L0 " + SanitizePlotText(result.LengthSourceRo));
        if (!string.IsNullOrWhiteSpace(result.InnerRadiusSourceRo))
            sb.AppendLine("sectiune " + SanitizePlotText(result.InnerRadiusSourceRo));
        if (uniformOffset)
            sb.AppendLine("Contur: offset vizual (u~0)");
        else if (useExaggerate)
            sb.AppendLine($"Contur marit vizual x{FormatK(exaggerateK, inv)}");
        else
            sb.AppendLine("Contur la scara reala");
        sb.AppendLine("(valori reale in tabel)");

        return SanitizePlotText(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Strip glyphs ScottPlot/Arial render as empty squares: box-drawing, angle, subscripts, multiply, dashes.
    /// Romanian letters (ă â î ș ț) are kept.
    /// </summary>
    public static string SanitizePlotText(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return s ?? "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\u2500': // box light
                case '\u2501': // box heavy
                case '\u2013': // en-dash
                case '\u2014': // em-dash
                case '\u2212': // minus
                case '\uFF0D': // fullwidth hyphen
                    sb.Append('-');
                    break;
                case '\u00D7': // multiply
                case '\u2715':
                case '\u2716':
                    sb.Append('x');
                    break;
                case '\u2220': // angle
                    sb.Append("deg");
                    break;
                case '\u00B0': // degree
                    sb.Append(" deg");
                    break;
                case '\u2080': // subscript 0
                    sb.Append('0');
                    break;
                case '\u2081':
                    sb.Append('1');
                    break;
                case '\u1D62': // subscript i
                    sb.Append('i');
                    break;
                case '\u00D8': // Oslash
                case '\u00F8':
                    sb.Append('D');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Scale so max|u| appears ~5% of R₀ (band 2–8%). No hard ×N cap — otherwise
    /// Contur collapses onto R₀ when R₀ is large or |u| is tiny.
    /// Returns NaN when all |u|≈0 (caller draws a uniform thin offset ring).
    /// </summary>
    public static double ComputeContourVisualScale(double r0Mm, IEnumerable<double> radialDisplacementsMm)
    {
        if (r0Mm <= 0)
            return 1.0;

        var maxAbs = 0.0;
        foreach (var u in radialDisplacementsMm)
        {
            if (double.IsNaN(u) || double.IsInfinity(u)) continue;
            var a = Math.Abs(u);
            if (a > maxAbs) maxAbs = a;
        }

        if (maxAbs < 1e-15)
            return double.NaN;

        var trueFrac = maxAbs / r0Mm;
        if (trueFrac >= MinVisualFractionOfR0 && trueFrac <= MaxVisualFractionOfR0)
            return 1.0;
        if (trueFrac > MaxVisualFractionOfR0)
            return MaxVisualFractionOfR0 * r0Mm / maxAbs;

        var k = ExaggerationTargetFractionOfR0 * r0Mm / maxAbs;
        if (k >= 20) return Math.Round(k);
        if (k >= 10) return Math.Round(k);
        return Math.Round(k, 1);
    }

    private static IReadOnlyList<ContourSensorPoint> BuildVisualSensors(
        IReadOnlyList<ContourSensorPoint> sensors,
        double r0,
        double k,
        bool uniformOffset = false)
    {
        var list = new List<ContourSensorPoint>(sensors.Count);
        var uniformU = uniformOffset ? MinVisualFractionOfR0 * r0 : 0.0;
        foreach (var s in sensors)
        {
            var uVis = uniformOffset ? uniformU : s.RadialDisplacementMm * k;
            var r = Math.Max(0, r0 + uVis);
            var rad = s.AngleDeg * Math.PI / 180.0;
            list.Add(new ContourSensorPoint
            {
                SensorIndex = s.SensorIndex,
                ChannelIndex = s.ChannelIndex,
                AngleDeg = s.AngleDeg,
                RadialDisplacementMm = uVis,
                RadiusMm = r,
                XMm = r * Math.Cos(rad),
                YMm = r * Math.Sin(rad)
            });
        }
        return list;
    }

    private static string FormatK(double k, CultureInfo inv)
        => k >= 20 ? k.ToString("0", inv) : k.ToString("0.#", inv);

    private static void StyleText(
        ScottPlot.Plottables.Text text,
        string fontName,
        float size,
        ScottPlot.Color color,
        bool bold = false,
        bool halo = false)
    {
        text.LabelFontName = fontName;
        text.LabelFontSize = size;
        text.LabelFontColor = color;
        text.LabelBold = bold;
        if (halo)
        {
            text.LabelBackgroundColor = LabelHalo;
            text.LabelPadding = 3;
        }
    }

    private static void AddThinLine(
        ScottPlot.Plot plot,
        double x1, double y1, double x2, double y2,
        ScottPlot.Color color,
        float width)
    {
        var g = plot.Add.Scatter(new[] { x1, x2 }, new[] { y1, y2 });
        g.Color = color;
        g.LineWidth = width;
        g.MarkerSize = 0;
    }
}

/// <summary>
/// Data-space layout for the Contur drawing sheet (left Tabel u_i legend + plan + elevation + section + mini plot).
/// The load plot is a compact box centered under the views, with Fig. {kind} immediately below.
/// Figure limits are padded to DefaultWidth/Height so SquareUnits keeps the plan circle round.
/// </summary>
public readonly struct ContourSheetLayout
{
    public double DiagramR { get; init; }
    public double PlanHalf { get; init; }
    public double ElevCx { get; init; }
    public double ElevCy { get; init; }
    public double ElevHalfW { get; init; }
    public double ElevHalfH { get; init; }
    public double SecCx { get; init; }
    public double SecCy { get; init; }
    public double SecHalfW { get; init; }
    public double SecHalfH { get; init; }
    public double ViewsLeft { get; init; }
    public double ViewsRight { get; init; }
    public double ViewsBottom { get; init; }
    public double ViewsTop { get; init; }
    public double BandLeft { get; init; }
    public double BandRight { get; init; }
    public double BandBottom { get; init; }
    public double BandTop { get; init; }
    public double TableLeft { get; init; }
    public double TableRight { get; init; }
    public double TableTop { get; init; }
    public double TableBottom { get; init; }
    public double LoadLeft { get; init; }
    public double LoadRight { get; init; }
    public double LoadCaptionY { get; init; }
    public double TitleY { get; init; }
    public double PlanCaptionY { get; init; }
    public double ElevCaptionY { get; init; }
    public double SecCaptionY { get; init; }
    public double LegendY { get; init; }
    /// <summary>Column between elevation and section for the u palette legend.</summary>
    public double PaletteLeft { get; init; }
    public double PaletteRight { get; init; }
    public double FigLeft { get; init; }
    public double FigRight { get; init; }
    public double FigBottom { get; init; }
    public double FigTop { get; init; }
}
