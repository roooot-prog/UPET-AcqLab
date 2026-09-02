using System.IO;
using ClosedXML.Excel;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App.Export;

/// <summary>
/// Applies the shared UPET antet (logo + expert + software author) to Excel worksheets via ClosedXML.
/// Horizontal band: high-res PNG logo left (aspect preserved), brand/institution/expert/author to the right.
/// </summary>
public static class ExcelReportHeader
{
    /// <summary>Display height in px; full PNG bytes are embedded — Excel scales for screen.</summary>
    private const int LogoDisplayHeightPx = 96;

    /// <summary>
    /// Writes logo (left) + product/institution/expert/author text starting at <paramref name="startRow"/>.
    /// Returns the first content row after the antet (startRow + <see cref="ReportHeaderHelper.ExcelHeaderRows"/>).
    /// </summary>
    public static int Apply(IXLWorksheet ws, int startRow = 1, int mergeCols = 8, bool embedLogo = true)
    {
        var logoPath = embedLogo ? ReportHeaderHelper.ResolveLogoPath() : null;
        var hasLogo = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath);

        var textCol = hasLogo ? 2 : 1;
        var endCol = Math.Max(mergeCols, textCol + 5);

        // White header band (no black fill behind logo)
        ws.Range(startRow, 1, startRow + ReportHeaderHelper.ExcelHeaderRows - 1, endCol)
            .Style.Fill.BackgroundColor = XLColor.White;

        var rowPt = 18.0; // 5 × 18 pt ≈ logo band
        ws.Row(startRow).Height = rowPt;
        ws.Row(startRow + 1).Height = rowPt;
        ws.Row(startRow + 2).Height = rowPt;
        ws.Row(startRow + 3).Height = rowPt;
        ws.Row(startRow + 4).Height = rowPt;

        if (hasLogo)
        {
            try
            {
                var (logoW, logoH) = ReportHeaderHelper.GetLogoDisplaySize(LogoDisplayHeightPx);
                // Embed original PNG bytes; Width/Height only set display size (no re-encode).
                var pic = ws.AddPicture(logoPath!);
                pic.MoveTo(ws.Cell(startRow, 1));
                pic.Width = logoW;
                pic.Height = logoH;
                ws.Column(1).Width = Math.Max(14, logoW / 7.0 + 1.0);
                // Optional narrow gutter was removed — text starts in column B beside logo
            }
            catch
            {
                hasLogo = false;
                textCol = 1;
            }
        }

        ws.Range(startRow, textCol, startRow, endCol).Merge();
        var brand = ws.Cell(startRow, textCol);
        brand.Value = ReportHeaderHelper.ProductName;
        brand.Style.Font.Bold = true;
        brand.Style.Font.FontSize = 16;
        brand.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        brand.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        brand.Style.Alignment.WrapText = true;

        ws.Range(startRow + 1, textCol, startRow + 1, endCol).Merge();
        var inst = ws.Cell(startRow + 1, textCol);
        inst.Value = ReportHeaderHelper.InstitutionLine;
        inst.Style.Font.FontSize = 10;
        inst.Style.Font.Italic = true;
        inst.Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        inst.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        inst.Style.Alignment.WrapText = true;

        ws.Range(startRow + 2, textCol, startRow + 2, endCol).Merge();
        var dept = ws.Cell(startRow + 2, textCol);
        dept.Value = ReportHeaderHelper.DepartmentName;
        dept.Style.Font.FontSize = 9;
        dept.Style.Font.Italic = true;
        dept.Style.Font.FontColor = XLColor.FromHtml("#5A6570");
        dept.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        dept.Style.Alignment.WrapText = true;

        ws.Range(startRow + 3, textCol, startRow + 3, endCol).Merge();
        var expert = ws.Cell(startRow + 3, textCol);
        expert.Value = ReportHeaderHelper.ExpertLine;
        expert.Style.Font.Bold = true;
        expert.Style.Font.FontSize = 12;
        expert.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        expert.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        expert.Style.Alignment.WrapText = true;

        ws.Range(startRow + 4, textCol, startRow + 4, endCol).Merge();
        var author = ws.Cell(startRow + 4, textCol);
        author.Value = ReportHeaderHelper.AuthorLine;
        author.Style.Font.Bold = true;
        author.Style.Font.FontSize = 10;
        author.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        author.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        author.Style.Alignment.WrapText = true;

        var ruleRow = startRow + ReportHeaderHelper.ExcelHeaderRows - 1;
        ws.Range(ruleRow, 1, ruleRow, endCol).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        ws.Range(ruleRow, 1, ruleRow, endCol).Style.Border.BottomBorderColor = XLColor.FromHtml("#1B2430");

        return startRow + ReportHeaderHelper.ExcelHeaderRows;
    }

    /// <summary>Compact one-line antet (product + expert + author) — dense data sheets (Date, Peak, per-channel).</summary>
    public static int ApplyCompact(IXLWorksheet ws, int startRow = 1, int mergeCols = 8)
    {
        ws.Range(startRow, 1, startRow, mergeCols).Merge();
        var cell = ws.Cell(startRow, 1);
        cell.Value = ReportHeaderHelper.CompactIdentityLine;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 10;
        cell.Style.Font.FontColor = XLColor.FromHtml("#1B2430");
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF4");
        cell.Style.Alignment.WrapText = true;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(startRow).Height = 22;
        return startRow + 1;
    }

    /// <summary>
    /// Official 4 pt Romania flag rule under the antet (cover sheets only).
    /// Returns the next content row.
    /// </summary>
    public static int ApplyTricolorStripe(IXLWorksheet ws, int row, int startCol, int endCol)
    {
        if (endCol < startCol) endCol = startCol;
        var n = endCol - startCol + 1;
        var third = Math.Max(1, n / 3);
        var blueEnd = startCol + third - 1;
        var yellowEnd = startCol + 2 * third - 1;
        if (yellowEnd < blueEnd) yellowEnd = blueEnd;
        if (yellowEnd >= endCol) yellowEnd = Math.Max(blueEnd, endCol - 1);

        ws.Row(row).Height = 5.0;
        Paint(startCol, blueEnd, ReportHeaderHelper.FlagBlueHex);
        Paint(blueEnd + 1, yellowEnd, ReportHeaderHelper.FlagYellowHex);
        Paint(yellowEnd + 1, endCol, ReportHeaderHelper.FlagRedHex);
        return row + 1;

        void Paint(int c0, int c1, string hex)
        {
            if (c1 < c0 || c0 < startCol) return;
            c1 = Math.Min(c1, endCol);
            ws.Range(row, c0, row, c1).Style.Fill.BackgroundColor = XLColor.FromHtml(hex);
            ws.Range(row, c0, row, c1).Style.Border.OutsideBorder = XLBorderStyleValues.None;
        }
    }
}
