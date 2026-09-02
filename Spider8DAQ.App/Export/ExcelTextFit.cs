using ClosedXML.Excel;

namespace Spider8DAQ.App.Export;

/// <summary>
/// ClosedXML AutoFit ignores merged cells (clipped text) or inflates the first merge column.
/// Wrap + explicit row height from the merged width, and size unmerged columns with a cap.
/// Never scans a full Date-style sample grid (thousands of rows) — that freezes the UI.
/// </summary>
internal static class ExcelTextFit
{
    public const double MaxColumnWidth = 72;
    public const double MinColumnWidth = 8;
    public const double MaxRowHeight = 400; // Excel max is 409 pt
    public const double PictureRowMinHeight = 70;

    /// <summary>
    /// Hard cap for wrap / AutoFit / row-height. Date sheets have 10k+ rows; scanning them
    /// with <c>Cell()</c> or <c>AdjustToContents</c> is catastrophic.
    /// </summary>
    public const int DefaultMaxScanRows = 400;

    /// <summary>
    /// Enable wrap on text, optionally AutoFit unmerged columns (capped), then grow row heights
    /// so wrapped / merged paragraphs are fully visible. Rows already taller than
    /// <see cref="PictureRowMinHeight"/> (plots, photos) are left unchanged.
    /// Uses <c>CellsUsed</c> so empty Date-grid cells are never materialized.
    /// </summary>
    public static void Apply(
        IXLWorksheet ws,
        int lastColumn,
        int? lastRow = null,
        bool sizeColumns = true,
        int widthScanMaxRows = 400)
    {
        var used = ws.RangeUsed();
        if (used is null) return;

        var usedLast = used.LastRow().RowNumber();
        var requested = lastRow ?? usedLast;
        var maxRow = Math.Min(Math.Max(1, requested), DefaultMaxScanRows);
        var maxCol = Math.Max(1, lastColumn);
        if (maxRow < 1) return;

        var pictureRows = PictureAnchorRows(ws);
        EnableWrapUsed(ws, maxCol, maxRow, pictureRows);

        if (sizeColumns)
            SizeUnmergedColumnsUsed(ws, maxCol, Math.Min(maxRow, Math.Max(1, widthScanMaxRows)), pictureRows);

        var rowsToFit = new HashSet<int>();
        foreach (var cell in ws.Range(1, 1, maxRow, maxCol).CellsUsed())
        {
            var r = cell.Address.RowNumber;
            if (ws.Row(r).Height >= PictureRowMinHeight || pictureRows.Contains(r))
                continue;
            rowsToFit.Add(r);
        }

        foreach (var r in rowsToFit)
            FitRowHeight(ws, r, maxCol);
    }

    /// <summary>Cap already-fitted columns (e.g. Peak after AdjustToContents).</summary>
    public static void CapColumnWidths(IXLWorksheet ws, int firstCol, int lastCol, double maxWidth = MaxColumnWidth)
    {
        for (var c = firstCol; c <= lastCol; c++)
        {
            if (ws.Column(c).Width > maxWidth)
                ws.Column(c).Width = maxWidth;
        }
    }

    public static void FitRowHeight(IXLWorksheet ws, int row, int lastColumn)
    {
        var needed = 0.0;
        foreach (var cell in ws.Range(row, 1, row, lastColumn).CellsUsed())
        {
            var c = cell.Address.ColumnNumber;
            var text = GetCellText(cell);
            if (string.IsNullOrEmpty(text))
                continue;

            if (cell.IsMerged())
            {
                var merge = cell.MergedRange();
                var origin = merge.RangeAddress.FirstAddress;
                if (origin.RowNumber != row || origin.ColumnNumber != c)
                    continue;
            }

            var width = DisplayWidthChars(ws, cell, lastColumn);
            if (width <= 0)
                continue;

            var fontSize = cell.Style.Font.FontSize > 0 ? cell.Style.Font.FontSize : 11;
            var lines = CountWrappedLines(text, width, fontSize, cell.Style.Font.Bold);
            var linePt = Math.Max(15.0, fontSize * 1.35);
            var h = lines * linePt + 4;
            if (h > needed)
                needed = h;
        }

        if (needed <= 0)
            return;

        needed = Math.Min(needed, MaxRowHeight);
        if (needed > ws.Row(row).Height)
            ws.Row(row).Height = needed;
    }

    private static void EnableWrapUsed(
        IXLWorksheet ws, int lastColumn, int lastRow, HashSet<int> pictureRows)
    {
        foreach (var cell in ws.Range(1, 1, lastRow, lastColumn).CellsUsed())
        {
            var r = cell.Address.RowNumber;
            if (ws.Row(r).Height >= PictureRowMinHeight || pictureRows.Contains(r))
                continue;

            // Numbers / dates on Date-like grids must not pay wrap + GetString per cell.
            if (cell.DataType is XLDataType.Number or XLDataType.Boolean
                or XLDataType.DateTime or XLDataType.TimeSpan)
                continue;

            var text = GetCellText(cell);
            if (string.IsNullOrEmpty(text))
                continue;

            if (cell.IsMerged())
            {
                var origin = cell.MergedRange().RangeAddress.FirstAddress;
                if (origin.RowNumber != r || origin.ColumnNumber != cell.Address.ColumnNumber)
                    continue;
            }

            cell.Style.Alignment.WrapText = true;
            if (text.Length > 24 || text.Contains('\n'))
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
    }

    private static void SizeUnmergedColumnsUsed(
        IXLWorksheet ws, int lastColumn, int scanLastRow, HashSet<int> pictureRows)
    {
        var widths = new double[lastColumn + 1];
        for (var c = 1; c <= lastColumn; c++)
            widths[c] = ws.Column(c).Width;

        foreach (var cell in ws.Range(1, 1, scanLastRow, lastColumn).CellsUsed())
        {
            var r = cell.Address.RowNumber;
            var c = cell.Address.ColumnNumber;
            if (c < 1 || c > lastColumn)
                continue;
            if (ws.Row(r).Height >= PictureRowMinHeight || pictureRows.Contains(r))
                continue;
            if (cell.IsMerged())
                continue;
            if (cell.DataType is XLDataType.Number or XLDataType.Boolean
                or XLDataType.DateTime or XLDataType.TimeSpan)
                continue;

            var text = GetCellText(cell);
            if (string.IsNullOrEmpty(text))
                continue;

            var fontSize = cell.Style.Font.FontSize > 0 ? cell.Style.Font.FontSize : 11;
            var w = EstimateColumnWidth(text, fontSize, cell.Style.Font.Bold);
            if (w > widths[c])
                widths[c] = w;
        }

        for (var c = 1; c <= lastColumn; c++)
        {
            var w = widths[c];
            if (w > MaxColumnWidth)
                w = MaxColumnWidth;
            else if (w < MinColumnWidth)
                w = Math.Max(ws.Column(c).Width, MinColumnWidth);
            ws.Column(c).Width = w;
        }
    }

    private static string GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return "";
        try
        {
            return cell.GetString();
        }
        catch
        {
            return "";
        }
    }

    private static double DisplayWidthChars(IXLWorksheet ws, IXLCell cell, int lastColumn)
    {
        if (cell.IsMerged())
        {
            var merge = cell.MergedRange();
            var first = merge.RangeAddress.FirstAddress.ColumnNumber;
            var last = Math.Min(merge.RangeAddress.LastAddress.ColumnNumber, lastColumn);
            var sum = 0.0;
            for (var c = first; c <= last; c++)
                sum += ws.Column(c).Width;
            return sum;
        }

        return ws.Column(cell.Address.ColumnNumber).Width;
    }

    internal static double EstimateColumnWidth(string text, double fontSize, bool bold)
    {
        var longest = 1;
        foreach (var para in SplitParagraphs(text))
        {
            if (para.Length > longest)
                longest = para.Length;
        }

        var scale = Math.Max(8.0, fontSize) / 11.0;
        var w = longest * scale * (bold ? 1.22 : 1.12) + 2.2;
        return w;
    }

    internal static int CountWrappedLines(string text, double widthChars, double fontSize, bool bold)
    {
        var scale = 11.0 / Math.Max(8.0, fontSize);
        var maxChars = Math.Max(8.0, widthChars * scale * (bold ? 0.86 : 0.92));
        var lines = 0;
        foreach (var para in SplitParagraphs(text))
            lines += CountWordWrapLines(para, maxChars);
        return Math.Max(1, lines);
    }

    private static HashSet<int> PictureAnchorRows(IXLWorksheet ws)
    {
        var rows = new HashSet<int>();
        try
        {
            foreach (var pic in ws.Pictures)
            {
                var row = pic.TopLeftCell?.Address.RowNumber ?? 0;
                if (row > 0)
                    rows.Add(row);
            }
        }
        catch
        {
            /* ClosedXML picture metadata optional */
        }

        return rows;
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private static int CountWordWrapLines(string para, double maxChars)
    {
        if (para.Length == 0)
            return 1;

        var words = para.Split(' ');
        var lineLen = 0.0;
        var lines = 1;
        foreach (var word in words)
        {
            var add = (lineLen <= 0 ? 0 : 1) + word.Length;
            if (lineLen > 0 && lineLen + add > maxChars)
            {
                lines++;
                lineLen = word.Length;
                if (lineLen > maxChars)
                {
                    var extra = (int)Math.Ceiling(lineLen / maxChars) - 1;
                    lines += Math.Max(0, extra);
                    lineLen %= maxChars;
                    if (lineLen <= 0)
                        lineLen = maxChars;
                }
            }
            else
            {
                lineLen += add;
            }
        }

        return Math.Max(1, lines);
    }
}
