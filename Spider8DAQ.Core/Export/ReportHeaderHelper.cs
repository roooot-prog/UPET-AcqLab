using System.Text;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Shared antet (report header) for Excel / HTML / PDF exports:
/// UPET logo + institution + expert + software author. Keeps branding consistent across all report types.
/// Do not stamp author on contour drawings, channel plots, or the tricolor stripe.
/// </summary>
public static class ReportHeaderHelper
{
    public const string ExpertLine = "EXPERT: Șef lucr.dr.ing. VÎLCEANU Florin";
    public const string SoftwareAuthor = "drd. ing. Iucal Ilie";
    public const string AuthorLine = "Autor soft: drd. ing. Iucal Ilie";
    /// <summary>One-line antet for compact Excel sheets and later PDF pages.</summary>
    public const string CompactIdentityLine = ProductName + "  ·  " + ExpertLine + "  ·  " + AuthorLine;
    public const string UniversityName = "Universitatea din Petroșani";
    public const string FacultyName = "Facultatea de Inginerie Mecanică și Electrică";
    public const string DepartmentName = "Departamentul de Inginerie Mecanică, Industrială și Transporturi";
    public const string InstitutionLine = UniversityName + " · " + FacultyName;
    public const string ProductName = "UPET AcqLab";
    public const string LogoFileName = "upet-logo.png";

    /// <summary>Drapelul României (Legea 75/1994): albastru — galben — roșu, stânga → dreapta.</summary>
    public const string FlagBlueHex = "#002B7F";
    public const string FlagYellowHex = "#FCD116";
    public const string FlagRedHex = "#CE1126";

    public static readonly (double R, double G, double B) FlagBlueRgb = (0.000, 0.169, 0.498);
    public static readonly (double R, double G, double B) FlagYellowRgb = (0.988, 0.820, 0.086);
    public static readonly (double R, double G, double B) FlagRedRgb = (0.808, 0.067, 0.149);

    /// <summary>Rows reserved at the top of framed Excel sheets (logo + faculty/dept + expert + author).</summary>
    public const int ExcelHeaderRows = 5;

    /// <summary>Resolve loose logo next to the EXE (copied on build/publish).</summary>
    public static string? ResolveLogoPath()
    {
        var root = AppPaths.InstallRoot;
        var candidates = new[]
        {
            Path.Combine(root, "Assets", LogoFileName),
            Path.Combine(root, LogoFileName),
            Path.Combine(root, "Assets", "Images", "Resources", LogoFileName),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>HTML fragment: logo + product + expert + author (UTF-8). Logo embedded as data URI when present.</summary>
    public static string BuildHtmlAntet(string? reportTitle = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='antet'>");
        var logo = ResolveLogoPath();
        if (logo is not null)
        {
            try
            {
                var b64 = Convert.ToBase64String(File.ReadAllBytes(logo));
                sb.AppendLine($"<img class='logo' alt='UPET' src='data:image/png;base64,{b64}'/>");
            }
            catch
            {
                /* logo optional */
            }
        }
        sb.AppendLine("<div class='antet-text'>");
        sb.AppendLine($"<div class='brand'>{Esc(ProductName)}</div>");
        sb.AppendLine($"<div class='institution'>{Esc(InstitutionLine)}</div>");
        sb.AppendLine($"<div class='department'>{Esc(DepartmentName)}</div>");
        sb.AppendLine($"<div class='expert'>{Esc(ExpertLine)}</div>");
        sb.AppendLine($"<div class='author'>{Esc(AuthorLine)}</div>");
        if (!string.IsNullOrWhiteSpace(reportTitle))
            sb.AppendLine($"<div class='report-title'>{Esc(reportTitle)}</div>");
        sb.AppendLine("</div></div>");
        sb.AppendLine(HtmlTricolorBar);
        return sb.ToString();
    }

    /// <summary>Three equal bands: blue, yellow, red (left → right).</summary>
    public const string HtmlTricolorBar =
        "<div class='tricolor' aria-hidden='true'><span class='ro-b'></span><span class='ro-y'></span><span class='ro-r'></span></div>";

    /// <summary>CSS rules for <see cref="BuildHtmlAntet"/>.</summary>
    public static string HtmlAntetCss =>
        ".antet{display:flex;align-items:center;gap:18px;margin:0 0 10px;padding:0 0 12px;" +
        "border-bottom:none}" +
        ".antet img.logo{height:96px;width:auto;flex-shrink:0}" +
        ".antet-text .brand{font-size:20px;font-weight:700;color:#1b2430;margin:0}" +
        ".antet-text .institution{color:#5a6570;font-size:13px;margin:2px 0 0}" +
        ".antet-text .department{color:#5a6570;font-size:12px;margin:1px 0 6px}" +
        ".antet-text .expert{font-weight:700;font-size:14px;color:#1b2430;letter-spacing:.01em}" +
        ".antet-text .author{font-weight:600;font-size:12px;color:#1b2430;margin:2px 0 0}" +
        ".antet-text .report-title{margin-top:8px;font-size:16px;font-weight:600;color:#1b2430}" +
        ".tricolor{display:flex;height:4px;margin:0 0 18px;padding:0;width:100%}" +
        ".tricolor span{flex:1;display:block}" +
        ".tricolor .ro-b{background:" + FlagBlueHex + "}" +
        ".tricolor .ro-y{background:" + FlagYellowHex + "}" +
        ".tricolor .ro-r{background:" + FlagRedHex + "}";

    /// <summary>
    /// PDF content-stream operators for a 3.5 pt tricolor rule (y = bottom of stripe).
    /// Callers must reset fill color after if they rely on a default.
    /// </summary>
    public static string PdfTricolorStripe(double x, double y, double width, double height = 3.5)
    {
        var w = width / 3.0;
        var b = FlagBlueRgb;
        var ye = FlagYellowRgb;
        var r = FlagRedRgb;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return
            string.Format(inv, "{0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f\n", b.R, b.G, b.B, x, y, w, height) +
            string.Format(inv, "{0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f\n", ye.R, ye.G, ye.B, x + w, y, w, height) +
            string.Format(inv, "{0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f\n", r.R, r.G, r.B, x + 2 * w, y, w, height) +
            "0.11 0.14 0.19 rg\n";
    }

    /// <summary>
    /// Plain-text header lines for PDF (Helvetica / WinAnsi — diacritics folded where needed).
    /// </summary>
    public static IReadOnlyList<string> BuildPdfHeaderLines(string reportTitle)
    {
        return new[]
        {
            PdfSafe(ProductName + " — " + reportTitle),
            PdfSafe(InstitutionLine),
            PdfSafe(DepartmentName),
            PdfSafe(ExpertLine),
            PdfSafe(AuthorLine),
            ""
        };
    }

    /// <summary>
    /// Display size for the UPET logo preserving native pixel aspect ratio.
    /// Fixes height to <paramref name="targetHeightPx"/>; width = height × (nativeW / nativeH).
    /// Native upet-logo.png is 2364×1910 (~1.238); do not force a square.
    /// </summary>
    public static (int Width, int Height) GetLogoDisplaySize(int targetHeightPx = 52)
    {
        var h = Math.Max(1, targetHeightPx);
        if (!TryGetLogoPixelSize(out var nw, out var nh) || nh <= 0)
            return ((int)Math.Round(h * 1.2377), h); // known file aspect fallback
        var w = (int)Math.Round(h * (double)nw / nh);
        return (Math.Max(1, w), h);
    }

    /// <summary>Native pixel size of Assets/upet-logo.png (or null path → false).</summary>
    public static bool TryGetLogoPixelSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        var path = ResolveLogoPath();
        if (path is null) return false;
        try
        {
            using var input = File.OpenRead(path);
            using var codec = SkiaSharp.SKCodec.Create(input);
            if (codec is null) return false;
            width = codec.Info.Width;
            height = codec.Info.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>JPEG bytes of the UPET logo for PDF embedding; null if unavailable.</summary>
    /// <remarks>Composites onto white (PNG has transparency) and keeps high pixel density for sharp page draw.</remarks>
    public static byte[]? TryGetLogoJpegBytes(int maxEdgePx = 640)
    {
        var path = ResolveLogoPath();
        if (path is null) return null;
        try
        {
            return PngFileToJpegBytes(path, maxEdgePx);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Any image file (PNG/JPEG/…) → JPEG bytes for PDF embedding; null on failure.</summary>
    public static byte[]? TryImageFileToJpegBytes(string path, int maxEdgePx = 1280)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return PngFileToJpegBytes(path, maxEdgePx);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decode image via SkiaSharp, composite on white, re-encode as high-quality JPEG for PDF.</summary>
    internal static byte[] PngFileToJpegBytes(string path, int maxEdgePx)
    {
        using var input = File.OpenRead(path);
        using var codec = SkiaSharp.SKCodec.Create(input)
            ?? throw new InvalidOperationException("Cannot decode logo image.");
        using var bitmap = SkiaSharp.SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("Cannot decode logo bitmap.");

        var w = bitmap.Width;
        var h = bitmap.Height;
        var scale = 1f;
        var edge = Math.Max(w, h);
        var cap = Math.Max(120, maxEdgePx);
        if (edge > cap && edge > 0)
            scale = cap / (float)edge;

        var dw = Math.Max(1, (int)Math.Round(w * scale));
        var dh = Math.Max(1, (int)Math.Round(h * scale));

        // White background so transparent PNG padding does not become a black box in JPEG/PDF.
        var info = new SkiaSharp.SKImageInfo(dw, dh, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
        using var surface = SkiaSharp.SKSurface.Create(info)
            ?? throw new InvalidOperationException("Cannot create logo surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.White);
        using var resized = bitmap.Resize(new SkiaSharp.SKImageInfo(dw, dh), SkiaSharp.SKFilterQuality.High)
            ?? bitmap;
        canvas.DrawBitmap(resized, 0, 0);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95)
            ?? throw new InvalidOperationException("JPEG encode failed.");
        return data.ToArray();
    }

    /// <summary>Fold Romanian diacritics for PDF Type1 Helvetica (WinAnsi).</summary>
    public static string PdfSafe(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                'Ș' or 'ş' or 'š' => 'S',
                'ș' => 's',
                'Ț' or 'ţ' => 'T',
                'ț' => 't',
                'Ă' or 'Â' => 'A',
                'ă' or 'â' => 'a',
                'Î' => 'I',
                'î' => 'i',
                '–' or '—' => '-',
                '·' => '-',
                _ => ch
            });
        }
        return sb.ToString();
    }

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
