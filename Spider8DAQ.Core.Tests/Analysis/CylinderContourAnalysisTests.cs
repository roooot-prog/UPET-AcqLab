using System.Globalization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class CylinderContourAnalysisTests
{
    [Fact]
    public void DefaultAngles_4Sensors_EvenlySpaced()
    {
        var a = CylinderContourConfig.DefaultAnglesDeg(4);
        Assert.Equal(new[] { 0.0, 90.0, 180.0, 270.0 }, a);
    }

    [Fact]
    public void DefaultAngles_8Sensors_EvenlySpaced()
    {
        var a = CylinderContourConfig.DefaultAnglesDeg(8);
        Assert.Equal(8, a.Length);
        Assert.Equal(0.0, a[0]);
        Assert.Equal(45.0, a[1]);
        Assert.Equal(315.0, a[^1]);
    }

    [Fact]
    public void Geometry_4Sensors_UniformBulge()
    {
        var session = BuildSession(
            force: new double[] { 1, 5, 10, 3 },
            stroke: new double[] { 0, 1, 2, 1.5 },
            circ: new[]
            {
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 }
            });

        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 50
        };

        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        Assert.Equal(2, r.SampleIndex); // max |F|=10
        Assert.Equal(ContourIndexRule.ForceAbsMax, r.IndexRule);
        Assert.Equal(4, r.Sensors.Count);
        Assert.All(r.Sensors, s => Assert.Equal(51.0, s.RadiusMm, 5));
        Assert.Equal(51.0, r.Sensors[0].XMm, 5); // 0°
        Assert.Equal(0.0, r.Sensors[0].YMm, 5);
        Assert.Equal(0.0, r.Sensors[1].XMm, 5); // 90°
        Assert.Equal(51.0, r.Sensors[1].YMm, 5);
        Assert.True(r.DeformedPolygon.Count == 5); // closed
        Assert.True(r.InitialContourPolygon.Count == 5);
        Assert.True(r.SmoothDeformedContour.Count > 8);
        // Smooth contour must stay outside / on R₀ for uniform positive bulge (no inward diamond).
        Assert.All(r.SmoothDeformedContour, p =>
        {
            var rr = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            Assert.True(rr >= 50.5 - 1e-6, $"smooth R={rr} expected ≥50.5");
        });
        Assert.Equal(0.0, r.OvalityMm, 5); // uniform u
        Assert.Equal(1.0, r.UMaxMm, 5);
        Assert.Equal(1.0, r.UMeanMm, 5);
        Assert.Contains("ArgMax |F|", r.IndexRuleFootnoteRo);
        Assert.Contains("R₀", r.PlotFootnoteRo);
        Assert.Contains(CylinderContourPlotRenderer.BarrelMapHonestyNote, r.PlotFootnoteRo);
        Assert.Contains(CylinderContourPlotRenderer.PaletteLegendRing, r.PlotFootnoteRo);
        Assert.DoesNotContain("stanga/dreapta", r.PlotFootnoteRo);
        Assert.DoesNotContain("plan median", r.PlotFootnoteRo);
        Assert.Contains(r.BuildReportRows(), row => row.Label.Contains("Ovalitate"));
    }

    [Fact]
    public void SmoothContour_PositiveU_BulgesOutsideR0_NotInwardDiamond()
    {
        var session = BuildSession(
            force: new double[] { 10 },
            stroke: new double[] { 1 },
            circ: new[]
            {
                new double[] { 2.0 },
                new double[] { 1.5 },
                new double[] { 2.5 },
                new double[] { 1.0 }
            });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 50
        };
        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        // Mid-angle sample (45°) of chord-diamond would fall inside R0; smooth must not.
        var mid = r.SmoothDeformedContour
            .Select(p => (R: Math.Sqrt(p.X * p.X + p.Y * p.Y), Ang: Math.Atan2(p.Y, p.X) * 180 / Math.PI))
            .OrderBy(t => Math.Abs(NormalizeAng(t.Ang) - 45))
            .First();
        Assert.True(mid.R > 50.0, $"45° smooth R={mid.R} must be > R0 (chord diamond would be inside)");
    }

    private static double NormalizeAng(double a)
    {
        a %= 360;
        if (a < 0) a += 360;
        return a;
    }

    [Fact]
    public void Ovality_IsMaxUMinusMinU()
    {
        var session = BuildSession(
            force: new double[] { 1, 10 },
            stroke: new double[] { 0, 1 },
            circ: new[]
            {
                new double[] { 0, 0.5 },
                new double[] { 0, 2.0 },
                new double[] { 0, 1.0 },
                new double[] { 0, 1.5 }
            });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 40
        };
        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        Assert.Equal(1.5, r.OvalityMm, 5); // 2.0 - 0.5
        Assert.Equal(2.0, r.UMaxMm, 5);
        Assert.Equal(0.5, r.UMinMm, 5);
        Assert.Equal(1.25, r.UMeanMm, 5);
        // max(R)-min(R) same as ovality
        Assert.Equal(r.Sensors.Max(s => s.RadiusMm) - r.Sensors.Min(s => s.RadiusMm), r.OvalityMm, 5);
    }

    [Fact]
    public void FlatSensor_WarningWhenOneChannelDoesNotGrow()
    {
        var n = 20;
        var growing = Enumerable.Range(0, n).Select(i => i * 0.1).ToArray();
        var flat = Enumerable.Repeat(0.01, n).ToArray();
        var session = BuildSession(
            force: Enumerable.Range(0, n).Select(i => (double)i).ToArray(),
            stroke: growing,
            circ: new[] { growing, growing, flat, growing });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 50
        };
        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(r.FlatSensorWarningRo));
        Assert.Contains("S3", r.FlatSensorWarningRo!);
    }

    [Fact]
    public void Geometry_8Sensors_CustomAngles()
    {
        var circ = Enumerable.Range(0, 8)
            .Select(i => new double[] { 0, i + 1.0 })
            .ToArray();
        var session = BuildSession(
            force: null,
            stroke: new double[] { 0, 1 },
            circ: circ);

        var cfg = CylinderContourConfig.CreateDefault(8, diameterMm: 100);
        cfg.ForceChannelIndex = null;
        cfg.StrokeChannelIndex = 0;
        cfg.SensorChannelIndices = Enumerable.Range(1, 8).ToList();

        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        Assert.Equal(8, r.Sensors.Count);
        Assert.Equal(50.0, r.InitialRadiusMm, 5);
        Assert.Equal(1, r.SampleIndex); // max |stroke|
        Assert.Equal(ContourIndexRule.StrokeAbsMax, r.IndexRule);
    }

    [Fact]
    public void SelectIndex_NoForce_UsesCursorB_WhenValidityZone()
    {
        var (idx, rule, note) = CylinderContourAnalysis.SelectContourIndex(
            forceColumn: null,
            strokeColumn: new double[] { 0, 1, 2, 3, 4 },
            circumferentialColumns: Array.Empty<IReadOnlyList<double>>(),
            sampleCount: 5,
            cursorA: 1,
            cursorB: 3);
        Assert.Equal(3, idx);
        Assert.Equal(ContourIndexRule.CursorB, rule);
        Assert.Contains("Cursor B", note);
    }

    [Fact]
    public void SelectIndex_NoForce_FullSpan_UsesStroke()
    {
        var (idx, rule, _) = CylinderContourAnalysis.SelectContourIndex(
            forceColumn: null,
            strokeColumn: new double[] { 0, -8, 2, 1 },
            circumferentialColumns: Array.Empty<IReadOnlyList<double>>(),
            sampleCount: 4,
            cursorA: 0,
            cursorB: 3);
        Assert.Equal(1, idx); // |−8|
        Assert.Equal(ContourIndexRule.StrokeAbsMax, rule);
    }

    [Fact]
    public void SelectIndex_NoForceNoStroke_UsesMeanRadial()
    {
        var circ = new IReadOnlyList<double>[]
        {
            new double[] { 0.1, 0.2, 2.0 },
            new double[] { 0.1, 0.2, 2.0 }
        };
        var (idx, rule, note) = CylinderContourAnalysis.SelectContourIndex(
            forceColumn: null,
            strokeColumn: null,
            circumferentialColumns: circ,
            sampleCount: 3,
            cursorA: 0,
            cursorB: 2);
        Assert.Equal(2, idx);
        Assert.Equal(ContourIndexRule.MaxMeanRadialBulge, rule);
        Assert.Contains("media |u_radial|", note);
    }

    [Fact]
    public void Compute_RequiresDiameterOrR0()
    {
        var session = BuildSession(
            force: new double[] { 1 },
            stroke: new double[] { 0 },
            circ: new[] { new double[] { 0 }, new double[] { 0 }, new double[] { 0 }, new double[] { 0 } });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            StrokeChannelIndex = 1,
            InitialRadiusMm = 0
        };
        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.False(r.IsValid);
        Assert.Contains("R₀", r.Error!);
    }

    [Fact]
    public void BarrelingIndex_IsUMeanOverAbsStroke()
    {
        var session = BuildSession(
            force: new double[] { 1, 10 },
            stroke: new double[] { 0, -2.0 },
            circ: new[]
            {
                new double[] { 0, 1.0 },
                new double[] { 0, 1.0 },
                new double[] { 0, 1.0 },
                new double[] { 0, 1.0 }
            });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 50
        };
        var r = CylinderContourAnalysis.Compute(cfg, session);
        Assert.True(r.IsValid);
        Assert.Equal(0.5, r.BarrelingIndex, 5); // 1.0 / 2.0
        Assert.Equal(1, r.UMaxSensorIndex); // all equal → first max keeps S1
        Assert.Contains(r.BuildReportRows(), row => row.Label.Contains("bombare", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("bombare=", r.ToSummaryRo());
    }

    [Fact]
    public void NoForce_IndexFootnoteSaysMaxCursa()
    {
        var (idx, rule, note) = CylinderContourAnalysis.SelectContourIndex(
            forceColumn: null,
            strokeColumn: new double[] { 0.1, 2.5, 1.0 },
            circumferentialColumns: Array.Empty<IReadOnlyList<double>>(),
            sampleCount: 3,
            cursorA: 0,
            cursorB: 2);
        Assert.Equal(1, idx);
        Assert.Equal(ContourIndexRule.StrokeAbsMax, rule);
        Assert.Contains("index = max cursă", note);
    }

    [Fact]
    public void AutoMap_PrefersRecMmChannels_EvenAngles()
    {
        var hints = new List<CylinderContourConfig.ChannelHint>
        {
            new() { Index = 0, Unit = "mm", Enabled = true, RecordEnabled = true, Name = "S1" },
            new() { Index = 1, Unit = "mm", Enabled = true, RecordEnabled = true, Name = "S2" },
            new() { Index = 2, Unit = "mm", Enabled = true, RecordEnabled = true, Name = "S3" },
            new() { Index = 3, Unit = "mm", Enabled = true, RecordEnabled = true, Name = "S4" },
            new() { Index = 4, Unit = "mm", Enabled = true, RecordEnabled = true, Name = "cursă" },
            new() { Index = 5, Unit = "N", Enabled = true, RecordEnabled = true, Name = "forță" },
            new() { Index = 6, Unit = "V", Enabled = true, RecordEnabled = false, Name = "skip" }
        };
        var cfg = CylinderContourConfig.AutoMapFromChannelHints(4, 100, hints);
        Assert.Equal(4, cfg.SensorCount);
        Assert.Equal(new[] { 0, 1, 2, 3 }, cfg.SensorChannelIndices);
        Assert.Equal(new[] { 0.0, 90.0, 180.0, 270.0 }, cfg.SensorAnglesDeg);
        Assert.Equal(4, cfg.StrokeChannelIndex);
        Assert.Equal(5, cfg.ForceChannelIndex);
    }

    [Fact]
    public void ContourVisualScale_KeepsFmaxRingReadable()
    {
        // Tiny u on lab-sized R₀ → exaggerate into ~2–8% band
        var k = CylinderContourPlotRenderer.ComputeContourVisualScale(
            50.0, new[] { 0.05, 0.04, 0.06, 0.03 });
        // target 0.05*50/0.06 ≈ 41.7
        Assert.True(k >= 10, $"expected readable exaggeration, got {k}");
        var frac = 0.06 * k / 50.0;
        Assert.InRange(frac, CylinderContourPlotRenderer.MinVisualFractionOfR0 * 0.9,
            CylinderContourPlotRenderer.MaxVisualFractionOfR0 * 1.1);
    }

    [Fact]
    public void ContourVisualScale_WorksWithHugeIntentionalR0()
    {
        // User-entered Ø≈99988 → R₀≈49994; micro swelling must still clear R₀ visually
        var k = CylinderContourPlotRenderer.ComputeContourVisualScale(
            49994.0, new[] { 0.01, 0.008, 0.012, 0.005 });
        Assert.False(double.IsNaN(k));
        var frac = 0.012 * k / 49994.0;
        Assert.InRange(frac, CylinderContourPlotRenderer.MinVisualFractionOfR0 * 0.9,
            CylinderContourPlotRenderer.MaxVisualFractionOfR0 * 1.1);
    }

    [Fact]
    public void ContourVisualScale_UniformOffsetWhenUNearZero()
    {
        var k = CylinderContourPlotRenderer.ComputeContourVisualScale(
            50.0, new[] { 0.0, 0.0, 0.0, 0.0 });
        Assert.True(double.IsNaN(k));
    }

    [Fact]
    public void SideTableText_HasNoBoxDrawingOrAngleGlyphs()
    {
        var result = new CylinderContourResult
        {
            IsValid = true,
            InitialRadiusMm = 50,
            Sensors = new[]
            {
                new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 0.1 },
                new ContourSensorPoint { SensorIndex = 2, AngleDeg = 90, RadialDisplacementMm = 0.2 }
            },
            UMaxMm = 0.2,
            UMinMm = 0.1,
            UMeanMm = 0.15,
            OvalityMm = 0.1,
            UMaxSensorIndex = 2,
            SampleIndex = 3,
            StrokeAtIndex = 1.5,
            InitialLengthMm = 80,
            DeformedLengthMm = 78.5,
            LengthSourceRo = "Start exp. (lungime)",
            InnerRadiusSourceRo = "solid (fara ID)",
            LoadCurveKind = "F vs cursa"
        };

        var text = CylinderContourPlotRenderer.FormatSideTable(
            result, useExaggerate: true, exaggerateK: 10,
            CultureInfo.InvariantCulture);

        Assert.DoesNotContain('\u2500', text); // box-drawing light horizontal
        Assert.DoesNotContain('\u2501', text);
        Assert.DoesNotContain('\u2220', text); // angle
        Assert.DoesNotContain('\u2014', text); // em-dash
        Assert.DoesNotContain('\u00D7', text); // multiply
        Assert.DoesNotContain('\u2080', text); // subscript 0
        Assert.DoesNotContain('\u1D62', text); // subscript i
        Assert.DoesNotContain('\u00B0', text); // degree
        Assert.Contains("Tabel u_i", text);
        Assert.Contains("S#     deg        u [mm]", text);
        Assert.Contains(new string('-', 26), text);
        Assert.Equal(2, text.Split('\n').Count(l => l.Trim('\r') == new string('-', 26)));
        Assert.Contains("cursa = 1.5 mm", text);
        Assert.Contains("bombare = -", text);
        Assert.Contains("D_capat = 100 mm", text);
        Assert.Contains("D_mijloc", text);
        Assert.Contains("Contur marit vizual x10", text);
        Assert.Contains(CylinderContourPlotRenderer.BarrelMapLegendLine1, text);
        Assert.Contains(CylinderContourPlotRenderer.BarrelMapLegendLine2, text);
        Assert.DoesNotContain("scan dens", text);
        Assert.DoesNotContain("stanga/dreapta", text);
        Assert.DoesNotContain("dualitate", text);

        // Sanitizer turns leftover box-drawing into ASCII hyphen
        Assert.Equal("ab-cd", CylinderContourPlotRenderer.SanitizePlotText("ab\u2500cd"));
        Assert.Equal("deg", CylinderContourPlotRenderer.SanitizePlotText("\u2220"));
    }

    [Fact]
    public void SheetLayout_TallSpecimen_PanelIsTallThinWithReadableWidth()
    {
        var L = CylinderContourPlotRenderer.ComputeSheetLayout(r0: 25, maxR: 26, l0: 200, rMid: 25);
        Assert.True(L.ElevHalfH > L.ElevHalfW, "tall specimen stays tall-thin at true L/D");
        var planW = 2 * L.PlanHalf;
        Assert.True(2 * L.ElevHalfW >= 0.38 * planW, "panel widened so it is not a lost sliver");
        Assert.Equal(L.ViewsTop, L.ElevCy + L.ElevHalfH, 5);
        AssertEqualPngAspect(L);
        AssertBandAndLegend(L);
        AssertPlateCentered(L);
    }

    [Fact]
    public void SheetLayout_DiscSpecimen_PanelIsShortWide_NotTallShaft()
    {
        var L = CylinderContourPlotRenderer.ComputeSheetLayout(r0: 50, maxR: 51, l0: 20, rMid: 50);
        Assert.True(L.ElevHalfW > L.ElevHalfH, "disc panel is short-wide");
        Assert.True(2 * L.ElevHalfH < 2 * L.PlanHalf * 0.75, "no tall empty shaft beside the plan");
        Assert.Equal(L.ViewsTop, L.ElevCy + L.ElevHalfH, 5);
        Assert.True(L.ElevCy > 0, "disc hangs from the shared top baseline");
        AssertEqualPngAspect(L);
        AssertBandAndLegend(L);
        AssertPlateCentered(L);
    }

    [Fact]
    public void SheetLayout_LeftLegend_CaptionsUnderViews_LoadPlotCenteredOnCaption()
    {
        var L = CylinderContourPlotRenderer.ComputeSheetLayout(r0: 50, maxR: 52, l0: 80, rMid: 51);
        Assert.True(L.TableRight < L.ViewsLeft, "Tabel u_i is the left legend, not under the views");
        Assert.True(L.TableTop >= L.ViewsTop - 1e-6, "legend aligned with the plan");
        Assert.True(L.PlanCaptionY < -L.PlanHalf, "Fig. Contur radial sits under the plan circle");
        Assert.True(L.PlanCaptionY > L.BandTop, "plan caption is above the mini-plot, not at the page footer");
        Assert.True(L.ElevCaptionY < L.ElevCy - L.ElevHalfH);
        Assert.True(L.SecCaptionY < L.SecCy - L.SecHalfH);
        var planW = 2 * L.PlanHalf;
        var captionClear = planW * 0.09;
        Assert.True(-L.PlanHalf - L.PlanCaptionY >= captionClear, "Fig. Contur radial clear of the plan frame");
        Assert.True(L.ElevCy - L.ElevHalfH - L.ElevCaptionY >= captionClear, "Fig. Elevatie clear of D_capat / panel");
        Assert.True(L.SecCy - L.SecHalfH - L.SecCaptionY >= captionClear, "Fig. Sectiune clear of ID note / panel");
        Assert.True(L.BandBottom - L.LoadCaptionY >= captionClear, "Fig. load caption clear of the plot box");
        Assert.True(L.TitleY > L.ViewsTop);
        var gapPlanElev = (L.ElevCx - L.ElevHalfW) - L.PlanHalf;
        Assert.True(gapPlanElev >= 0.12 * planW, "plan and elevation are spaced apart");
        var gapElevSec = (L.SecCx - L.SecHalfW) - (L.ElevCx + L.ElevHalfW);
        Assert.True(gapElevSec >= 0.12 * planW);
        Assert.True(L.BandTop < L.PlanCaptionY);
        var viewsW = L.ViewsRight - L.ViewsLeft;
        var loadW = L.LoadRight - L.LoadLeft;
        Assert.True(loadW > 0.35 * viewsW, "mini plot is readable");
        Assert.True(loadW < 0.75 * viewsW, "mini plot is compact, not flush-right across the band");
        Assert.True(L.LoadLeft >= L.ViewsLeft - 1e-6);
        Assert.True(L.LoadRight <= L.ViewsRight + 1e-6);
        Assert.True(L.TableRight < L.LoadLeft);
        Assert.Equal(12f, CylinderContourPlotRenderer.TableFontSize, 0);
        AssertEqualPngAspect(L);
        AssertBandAndLegend(L);
    }

    [Fact]
    public void FormatLoadFigureCaption_MatchesCurveKind()
    {
        Assert.Equal("Fig. cursa vs index", CylinderContourPlotRenderer.FormatLoadFigureCaption("cursa vs index"));
        Assert.Equal("Fig. sigma vs eps", CylinderContourPlotRenderer.FormatLoadFigureCaption("sigma vs eps"));
        Assert.Equal("Fig. F vs cursa", CylinderContourPlotRenderer.FormatLoadFigureCaption("F vs cursa"));
    }

    private static void AssertEqualPngAspect(ContourSheetLayout L)
    {
        var dataAspect = (L.FigRight - L.FigLeft) / (L.FigTop - L.FigBottom);
        var pngAspect = CylinderContourPlotRenderer.DefaultWidth / (double)CylinderContourPlotRenderer.DefaultHeight;
        Assert.InRange(dataAspect, pngAspect * 0.995, pngAspect * 1.005);
    }

    private static void AssertBandAndLegend(ContourSheetLayout L)
    {
        Assert.Equal(L.LoadLeft, L.BandLeft, 5);
        Assert.Equal(L.LoadRight, L.BandRight, 5);
        var loadCx = 0.5 * (L.LoadLeft + L.LoadRight);
        var viewsCx = 0.5 * (L.ViewsLeft + L.ViewsRight);
        Assert.Equal(loadCx, viewsCx, 5);
        Assert.True(L.LoadCaptionY < L.BandBottom, "Fig. load caption sits under the plot box");
        Assert.True(L.LoadCaptionY > L.FigBottom);
        Assert.True(L.TableRight < L.ViewsLeft);
        Assert.True(L.TableLeft < L.TableRight);
    }

    private static void AssertPlateCentered(ContourSheetLayout L)
    {
        var midPlate = 0.5 * (L.TableLeft + L.ViewsRight);
        var midFig = 0.5 * (L.FigLeft + L.FigRight);
        Assert.Equal(midPlate, midFig, 3);
        var marginL = L.TableLeft - L.FigLeft;
        var marginR = L.FigRight - L.ViewsRight;
        Assert.Equal(marginL, marginR, 3);
        Assert.True(marginL > 0);
    }

    [Fact]
    public void ResolveInitialLength_PrefersSampleLength()
    {
        var l0 = CylinderBarrelGeometry.ResolveInitialLengthMm(80, 12, 50, 2, out var nom, out var src);
        Assert.Equal(80, l0, 5);
        Assert.False(nom);
        Assert.Contains("lungime", src);
    }

    [Fact]
    public void ResolveInitialLength_FallsBackToThicknessThenNominal()
    {
        var fromT = CylinderBarrelGeometry.ResolveInitialLengthMm(0, 90, 50, 2, out var nomT, out _);
        Assert.Equal(90, fromT, 5);
        Assert.False(nomT);

        var nomL = CylinderBarrelGeometry.ResolveInitialLengthMm(0, 0, 50, 2, out var nom, out var src);
        Assert.True(nom);
        Assert.Equal(200, nomL, 5); // 2 * D0 = 4 * R0
        Assert.Contains("2*D0", src);
    }

    [Fact]
    public void DeformedHeight_SubtractsAbsStroke_AndClamps()
    {
        Assert.Equal(90, CylinderBarrelGeometry.DeformedHeightMm(100, 10), 5);
        Assert.Equal(90, CylinderBarrelGeometry.DeformedHeightMm(100, -10), 5);
        var floored = CylinderBarrelGeometry.DeformedHeightMm(100, 200);
        Assert.Equal(100 * CylinderBarrelGeometry.MinHeightFractionOfL0, floored, 5);
    }

    [Fact]
    public void BarrelOutline_EndsStayAtR0_MidIsR0PlusU()
    {
        var pts = CylinderBarrelGeometry.BuildBarrelOutline(50, 2, 2, 100, samples: 20);
        Assert.True(pts.Count > 8);
        var bottomRight = pts[0];
        Assert.Equal(50, bottomRight.X, 5);
        Assert.Equal(0, bottomRight.Y, 5);
        var mid = pts.OrderBy(p => Math.Abs(p.Y - 50)).First(p => p.X > 0);
        Assert.Equal(52, mid.X, 4);
    }

    [Fact]
    public void DisplacementAtHeight_EndsZero_MidEqualsU()
    {
        Assert.Equal(0, CylinderBarrelGeometry.DisplacementAtHeight(2, 0, 100), 8);
        Assert.Equal(0, CylinderBarrelGeometry.DisplacementAtHeight(2, 100, 100), 8);
        Assert.Equal(2, CylinderBarrelGeometry.DisplacementAtHeight(2, 50, 100), 8);
        Assert.True(CylinderBarrelGeometry.DisplacementAtHeight(2, 25, 100) < 2);
        Assert.True(CylinderBarrelGeometry.DisplacementAtHeight(2, 25, 100) > 0);
    }

    [Fact]
    public void BarrelFillBands_ShapeUsesVisualU_PlatensStayAtR0()
    {
        var bands = CylinderBarrelGeometry.BuildBarrelFillBands(
            r0Mm: 50,
            uRightVisMm: 4,
            uLeftVisMm: 2,
            heightMm: 100,
            bands: 20);
        Assert.Equal(20, bands.Count);

        var bottom = bands[0];
        Assert.Equal(50, bottom.XRight0, 3);

        var mid = bands.OrderBy(b => Math.Abs(b.YMid - 50)).First();
        Assert.True(mid.XRight0 > 53.5 || mid.XRight1 > 53.5, "silhouette uses visual u (R0+4)");
        Assert.True(mid.XLeft0 < -51.5 || mid.XLeft1 < -51.5);
    }

    [Fact]
    public void BarrelUColorRange_FromMeasuredSensors_IncludesPlatensZero()
    {
        var sensors = new[]
        {
            new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 1 },
            new ContourSensorPoint { SensorIndex = 2, AngleDeg = 180, RadialDisplacementMm = 8 },
            new ContourSensorPoint { SensorIndex = 3, AngleDeg = 90, RadialDisplacementMm = double.NaN }
        };
        CylinderBarrelGeometry.BarrelUColorRange(sensors, out var uLo, out var uHi);
        Assert.Equal(0, uLo, 8);
        Assert.Equal(8, uHi, 8);
    }

    [Fact]
    public void VisibleAzimuthDeg_LeftIsOpposite_RightIsViewPlane()
    {
        Assert.Equal(180, CylinderBarrelGeometry.VisibleAzimuthDeg(0, 0), 5);
        Assert.Equal(0, CylinderBarrelGeometry.VisibleAzimuthDeg(0, 1), 5);
        Assert.Equal(90, CylinderBarrelGeometry.VisibleAzimuthDeg(0, 0.5), 5);
        Assert.Equal(45, CylinderBarrelGeometry.VisibleAzimuthDeg(45, 1), 5);
        Assert.Equal(225, CylinderBarrelGeometry.VisibleAzimuthDeg(45, 0), 5);
    }

    [Fact]
    public void InterpolateU_SkipsNaN_DoesNotInventAcrossLargeGap()
    {
        var withNan = new[]
        {
            new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 2 },
            new ContourSensorPoint { SensorIndex = 2, AngleDeg = 90, RadialDisplacementMm = double.NaN },
            new ContourSensorPoint { SensorIndex = 3, AngleDeg = 180, RadialDisplacementMm = 4 }
        };
        Assert.True(CylinderBarrelGeometry.TryInterpolateUAtAngleDeg(withNan, 90, out var u90));
        Assert.Equal(3, u90, 5); // cosine midpoint of neighbors, not an invented 90 deg sample

        var gapped = new[]
        {
            new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 2 },
            new ContourSensorPoint { SensorIndex = 2, AngleDeg = 45, RadialDisplacementMm = 3 }
        };
        Assert.True(CylinderBarrelGeometry.TryInterpolateUAtAngleDeg(gapped, 20, out _));
        Assert.False(CylinderBarrelGeometry.TryInterpolateUAtAngleDeg(gapped, 180, out _));
    }

    [Fact]
    public void BarrelFillCells_ColorFromMeasuredRing_NotCannedHeightRamp()
    {
        var sensors = new[]
        {
            new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 8 },
            new ContourSensorPoint { SensorIndex = 2, AngleDeg = 180, RadialDisplacementMm = 1 }
        };
        var cells = CylinderBarrelGeometry.BuildBarrelFillCells(
            r0Mm: 50,
            uRightVisMm: 4,
            uLeftVisMm: 2,
            heightMm: 100,
            sensors,
            viewPlaneDeg: 0,
            bands: 20,
            columns: 16);
        Assert.NotEmpty(cells);

        static double XMid(BarrelFillCell c)
            => 0.25 * (c.XLeft0 + c.XRight0 + c.XLeft1 + c.XRight1);

        var midRight = cells
            .Where(c => Math.Abs(0.5 * (c.Y0 + c.Y1) - 50) < 8)
            .OrderByDescending(XMid)
            .First();
        Assert.InRange(midRight.U, 7.0, 8.1);

        var midLeft = cells
            .Where(c => Math.Abs(0.5 * (c.Y0 + c.Y1) - 50) < 8)
            .OrderBy(XMid)
            .First();
        Assert.InRange(midLeft.U, 0.8, 1.3);

        var botRight = cells
            .Where(c => 0.5 * (c.Y0 + c.Y1) < 8)
            .OrderByDescending(XMid)
            .First();
        Assert.True(Math.Abs(botRight.U) < 0.5, "platens fade to u=0 even on the hot azimuth");

        var midDistinct = cells
            .Where(c => Math.Abs(0.5 * (c.Y0 + c.Y1) - 50) < 8)
            .Select(c => Math.Round(c.U, 2))
            .Distinct()
            .Count();
        Assert.True(midDistinct >= 4, "circumferential interpolation, not left/right dualitate");
    }

    [Fact]
    public void BarrelFillBands_DefaultCountIsDense_AndOverlapsSeams()
    {
        Assert.InRange(CylinderBarrelGeometry.DefaultFillBandCount, 80, 200);
        var bands = CylinderBarrelGeometry.BuildBarrelFillBands(
            r0Mm: 50,
            uRightVisMm: 2,
            uLeftVisMm: 2,
            heightMm: 100);
        Assert.Equal(CylinderBarrelGeometry.DefaultFillBandCount, bands.Count);
        Assert.True(bands[1].Y0 < bands[0].Y1, "adjacent bands overlap to hide brick seams");
        Assert.Equal(0, bands[0].Y0, 8);
        Assert.Equal(100, bands[^1].Y1, 8);
        var mid = bands.OrderBy(b => Math.Abs(b.YMid - 50)).First();
        Assert.True(mid.XRight0 > 51.5 || mid.XRight1 > 51.5);
    }

    [Fact]
    public void ElevationCaption_IsBarrelMapNotScan()
    {
        Assert.Equal("Fig. Elevatie - harta bombare", CylinderContourPlotRenderer.ElevationCaption);
        Assert.Equal(
            "culoare = u masurat pe inel; la platene u=0 (contact, fara senzor de u)",
            CylinderContourPlotRenderer.BarrelMapHonestyNote);
        Assert.Equal(
            "culoare = u masurat pe inel",
            CylinderContourPlotRenderer.BarrelMapLegendLine1);
        Assert.Equal(
            "la platene u=0 (contact, fara senzor de u)",
            CylinderContourPlotRenderer.BarrelMapLegendLine2);
        Assert.DoesNotContain("scan dens", CylinderContourPlotRenderer.BarrelMapHonestyNote);
        Assert.DoesNotContain("stanga/dreapta", CylinderContourPlotRenderer.BarrelMapHonestyNote);
        Assert.DoesNotContain("dualitate", CylinderContourPlotRenderer.BarrelMapHonestyNote);
    }

    [Fact]
    public void PaletteLegend_HasDescriptionTicksAndAscii()
    {
        Assert.Equal("Paleta u [mm]", CylinderContourPlotRenderer.PaletteLegendTitle);
        Assert.Equal("rece = u mic masurat / platene u=0", CylinderContourPlotRenderer.PaletteLegendCool);
        Assert.Equal("cald = u mare masurat pe inel", CylinderContourPlotRenderer.PaletteLegendWarm);
        Assert.Equal(
            "interpolare pe circumferinta intre senzori",
            CylinderContourPlotRenderer.PaletteLegendRing);

        var desc = CylinderContourPlotRenderer.FormatPaletteLegendDescription();
        Assert.Contains(CylinderContourPlotRenderer.PaletteLegendCool, desc);
        Assert.Contains(CylinderContourPlotRenderer.PaletteLegendWarm, desc);
        Assert.Contains(CylinderContourPlotRenderer.PaletteLegendRing, desc);
        Assert.Contains('\n', desc);
        Assert.DoesNotContain("scan dens", desc);
        Assert.DoesNotContain("stanga/dreapta", desc);
        Assert.DoesNotContain("dualitate", desc);
        foreach (var ch in desc + CylinderContourPlotRenderer.PaletteLegendTitle)
            Assert.True(ch == '\n' || ch == '\r' || ch < 127, $"non-ASCII palette glyph U+{(int)ch:X4}");

        var ticks5 = CylinderContourPlotRenderer.PaletteTickValues(0, 0.079, 5);
        Assert.Equal(5, ticks5.Count);
        Assert.Equal(0, ticks5[0].UMm, 8);
        Assert.Equal(0, ticks5[0].Fraction, 8);
        Assert.Equal(0.0395, ticks5[2].UMm, 4);
        Assert.Equal(0.5, ticks5[2].Fraction, 8);
        Assert.Equal(0.079, ticks5[^1].UMm, 8);
        Assert.Equal(1, ticks5[^1].Fraction, 8);
        Assert.Equal("0", CylinderContourPlotRenderer.FormatPaletteTickLabel(0));
        Assert.Equal("0.079", CylinderContourPlotRenderer.FormatPaletteTickLabel(0.079));
        Assert.Equal("0.04", CylinderContourPlotRenderer.FormatPaletteTickLabel(0.04));

        var ticks3 = CylinderContourPlotRenderer.PaletteTickValues(0, 0.079, 3);
        Assert.Equal(3, ticks3.Count);
        Assert.Equal(0, ticks3[0].UMm, 8);
        Assert.Equal(0.0395, ticks3[1].UMm, 4);
        Assert.Equal(0.079, ticks3[2].UMm, 8);

        var collapsed = CylinderContourPlotRenderer.PaletteTickValues(0, 0, 5);
        Assert.Single(collapsed);
        Assert.Equal(0, collapsed[0].UMm, 8);
    }

    [Fact]
    public void SheetLayout_PaletteColumn_BetweenElevationAndSection()
    {
        var L = CylinderContourPlotRenderer.ComputeSheetLayout(r0: 50, maxR: 52, l0: 80, rMid: 51);
        var planW = 2 * L.PlanHalf;
        Assert.Equal(L.ElevCx + L.ElevHalfW, L.PaletteLeft, 6);
        Assert.Equal(L.SecCx - L.SecHalfW, L.PaletteRight, 6);
        Assert.True(L.PaletteRight > L.PaletteLeft);
        Assert.True(L.PaletteRight - L.PaletteLeft >= 0.28 * planW, "palette column is wide enough for strip + ticks + key");
        Assert.True(L.PaletteLeft >= L.ElevCx + L.ElevHalfW - 1e-9, "palette stays off the elevation / D_capat");
        Assert.True(L.PaletteRight <= L.SecCx - L.SecHalfW + 1e-9, "palette stays off the section");
        Assert.True(L.ElevCaptionY < L.ElevCy - L.ElevHalfH, "Fig. Elevatie stays under the panel, not on the palette");
        Assert.Equal(120, CylinderBarrelGeometry.DefaultFillBandCount);
    }

    [Fact]
    public void ResolveInnerRadius_SolidWhenMissing_TubeWhenIdSet()
    {
        var solid = CylinderBarrelGeometry.ResolveInnerRadiusMm(50, 0, out var sSrc);
        Assert.Equal(0, solid);
        Assert.Contains("solid", sSrc);

        var ri = CylinderBarrelGeometry.ResolveInnerRadiusMm(50, 40, out var tSrc);
        Assert.Equal(20, ri, 5);
        Assert.Contains("ID", tSrc);

        var tooBig = CylinderBarrelGeometry.ResolveInnerRadiusMm(50, 120, out _);
        Assert.Equal(0, tooBig);
    }

    [Fact]
    public void HalfSection_SolidHatchesToAxis_TubeKeepsWall()
    {
        var solid = CylinderBarrelGeometry.BuildHalfSectionWallPolygon(50, 2, 0, 100, 20);
        var midOut = solid.Where(p => p.X > 0).OrderBy(p => Math.Abs(p.Y - 50)).First();
        Assert.Equal(52, midOut.X, 4);
        Assert.Contains(solid, p => Math.Abs(p.X) < 1e-9);

        var hatchSolid = CylinderBarrelGeometry.BuildHalfSectionHatch(50, 2, 0, 100, 10);
        Assert.All(hatchSolid, h => Assert.Equal(0, h.X0, 5));
        var midH = hatchSolid.OrderBy(h => Math.Abs(h.Y - 50)).First();
        Assert.True(midH.X1 > 50);

        var tube = CylinderBarrelGeometry.BuildHalfSectionHatch(50, 2, 20, 100, 10);
        var midT = tube.OrderBy(h => Math.Abs(h.Y - 50)).First();
        Assert.True(midT.X0 > 15);
        Assert.True(midT.X1 > midT.X0);
    }

    [Fact]
    public void LoadCurveKind_ForceStroke_ThenFallbacks()
    {
        Assert.Equal("F vs cursa", CylinderBarrelGeometry.ResolveLoadCurveKind(true, true));
        Assert.Equal("F vs index", CylinderBarrelGeometry.ResolveLoadCurveKind(true, false));
        Assert.Equal("cursa vs index", CylinderBarrelGeometry.ResolveLoadCurveKind(false, true));
        Assert.Equal("", CylinderBarrelGeometry.ResolveLoadCurveKind(false, false));
        Assert.Equal("sigma vs eps", CylinderBarrelGeometry.ResolveLoadCurveKind(true, true, canStressStrain: true));
        Assert.Equal("F vs cursa", CylinderBarrelGeometry.ResolveLoadCurveKind(true, true, canStressStrain: false));
    }

    [Fact]
    public void Compute_FillsLengthForceSeries_AndLoadKind()
    {
        var session = BuildSession(
            force: new double[] { 1, 5, 10, 3 },
            stroke: new double[] { 0, 1, 2, 1.5 },
            circ: new[]
            {
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 },
                new double[] { 0, 0.5, 1.0, 0.8 }
            });
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 2, 3, 4, 5 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            ForceChannelIndex = 0,
            StrokeChannelIndex = 1,
            InitialRadiusMm = 50,
            InnerDiameterMm = 40
        };
        var r = CylinderContourAnalysis.Compute(cfg, session, sampleLengthMm: 80, sampleInnerDiameterMm: 0);
        Assert.True(r.IsValid);
        Assert.Equal(80, r.InitialLengthMm, 5);
        Assert.Equal(78, r.DeformedLengthMm, 5); // |cursă|=2 at Fmax idx 2
        Assert.False(r.LengthIsNominal);
        Assert.Equal(20, r.InnerRadiusMm, 5);
        Assert.Equal(4, r.ForceSeries.Count);
        Assert.Equal(4, r.StrokeSeries.Count);
        Assert.Equal("sigma vs eps", r.LoadCurveKind);
        Assert.Equal(10, r.ForceSeries[r.SampleIndex], 5);
    }

    private static OfflineSession BuildSession(
        double[]? force,
        double[] stroke,
        double[][] circ)
    {
        var n = stroke.Length;
        var session = new OfflineSession();
        session.Timestamps.AddRange(Enumerable.Range(0, n).Select(i => DateTime.UtcNow.AddSeconds(i * 0.02)));
        var cols = new List<double[]>();
        var names = new List<string>();
        if (force is not null)
        {
            cols.Add(force);
            names.Add("Force [N]");
        }
        cols.Add(stroke);
        names.Add("Stroke [mm]");
        for (var i = 0; i < circ.Length; i++)
        {
            cols.Add(circ[i]);
            names.Add($"S{i + 1} [mm]");
        }
        session.Columns.AddRange(cols);
        session.ChannelNames.AddRange(names);
        session.CursorA = 0;
        session.CursorB = Math.Max(0, n - 1);
        return session;
    }

    [Fact]
    public void CrossSectionArea_SolidAndTube()
    {
        var solid = CylinderBarrelGeometry.CrossSectionAreaMm2(50, 0);
        Assert.Equal(Math.PI * 2500, solid, 6);
        var tube = CylinderBarrelGeometry.CrossSectionAreaMm2(50, 20);
        Assert.Equal(Math.PI * (2500 - 400), tube, 6);
        Assert.Equal(0, CylinderBarrelGeometry.CrossSectionAreaMm2(0, 0));
    }

    [Fact]
    public void StressStrain_IsForceOverA0_AndAbsStrokeOverL0()
    {
        var force = new[] { 0.0, 1000.0, 2000.0 };
        var stroke = new[] { 0.0, 1.0, 2.0 };
        Assert.True(CylinderBarrelGeometry.TryBuildStressStrain(
            force, stroke, 50, 0, 80, out var eps, out var sigma));
        Assert.Equal(3, eps.Length);
        var a0 = Math.PI * 50 * 50;
        Assert.Equal(0, eps[0], 10);
        Assert.Equal(2.0 / 80.0, eps[2], 10);
        Assert.Equal(0, sigma[0], 10);
        Assert.Equal(2000.0 / a0, sigma[2], 8);
        Assert.False(CylinderBarrelGeometry.TryBuildStressStrain(
            force, stroke, 50, 0, 0, out _, out _));
    }

    [Fact]
    public void EndAndMidDiameters_UsesR0AndUmaxPlane()
    {
        var sensors = new[]
        {
            new ContourSensorPoint { SensorIndex = 1, AngleDeg = 0, RadialDisplacementMm = 0.4 },
            new ContourSensorPoint { SensorIndex = 2, AngleDeg = 180, RadialDisplacementMm = 0.2 }
        };
        CylinderBarrelGeometry.EndAndMidDiametersMm(
            50, sensors, uMeanMm: 0.3, uMaxAngleDeg: 0,
            out var dCap, out var dMid);
        Assert.Equal(100, dCap, 5);
        Assert.Equal(100.6, dMid, 5); // 2*R0 + 0.4 + 0.2
    }

    [Fact]
    public void SheetLayout_CentersGroup_AndFitsLdPanels()
    {
        var pngAspect = CylinderContourPlotRenderer.DefaultWidth
            / (double)CylinderContourPlotRenderer.DefaultHeight;

        var tall = CylinderContourPlotRenderer.ComputeSheetLayout(50, 55, l0: 200, rMid: 52);
        Assert.True(tall.ElevHalfH > tall.ElevHalfW, "tall specimen → tall-thin panel");
        Assert.Equal(tall.ViewsTop, tall.ElevCy + tall.ElevHalfH, 6);
        var tallFigAspect = (tall.FigRight - tall.FigLeft) / (tall.FigTop - tall.FigBottom);
        Assert.InRange(tallFigAspect, pngAspect * 0.98, pngAspect * 1.02);
        var plateCx = 0.5 * (tall.TableLeft + tall.ViewsRight);
        var figCx = 0.5 * (tall.FigLeft + tall.FigRight);
        Assert.InRange(figCx, plateCx - 1e-6, plateCx + 1e-6);
        Assert.True(tall.TableRight < tall.ViewsLeft);
        Assert.Equal(
            0.5 * (tall.LoadLeft + tall.LoadRight),
            0.5 * (tall.ViewsLeft + tall.ViewsRight),
            5);
        Assert.True(tall.BandRight <= tall.ViewsRight + 1e-6);
        Assert.True(tall.LoadCaptionY < tall.BandBottom);

        var disc = CylinderContourPlotRenderer.ComputeSheetLayout(50, 55, l0: 30, rMid: 52);
        Assert.True(disc.ElevHalfW > disc.ElevHalfH, "disc → short-wide panel");
        Assert.Equal(disc.ViewsTop, disc.ElevCy + disc.ElevHalfH, 6);
        Assert.True(disc.ElevCy > tall.ElevCy, "disc hangs from the shared top baseline");
    }
}
