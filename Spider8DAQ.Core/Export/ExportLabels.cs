namespace Spider8DAQ.Core.Export;

/// <summary>Channel labels for CSV headers and export axis/legend text.</summary>
public static class ExportLabels
{
    public static string ChannelHeader(string name, string? unit)
    {
        if (string.IsNullOrWhiteSpace(name)) return "CH";
        var n = name.Trim();
        if (string.IsNullOrWhiteSpace(unit)) return n;
        var u = unit.Trim();
        if (n.Contains(u, StringComparison.OrdinalIgnoreCase)) return n;
        if (n.Contains('[', StringComparison.Ordinal) && n.Contains(']', StringComparison.Ordinal))
            return n;
        return $"{n} [{u}]";
    }

    public static string? ParseUnit(string channelHeader)
    {
        var start = channelHeader.LastIndexOf('[');
        var end = channelHeader.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        return channelHeader[(start + 1)..end].Trim();
    }

    public static string BuildYAxisLabel(IReadOnlyList<string> channelNames)
    {
        var units = channelNames
            .Select(ParseUnit)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return units.Count switch
        {
            0 => "Y",
            1 => $"Y [{units[0]}]",
            _ => "Y (unități mixte)"
        };
    }

    public static bool IsActiveColumn(double[] col) =>
        col.Any(v => !double.IsNaN(v) && !double.IsInfinity(v));
}
