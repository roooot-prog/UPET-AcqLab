using System.Globalization;
using System.Text;

namespace Spider8DAQ.Core.Export;

public static class TxtExporter
{
    public static void Export(
        string path,
        IReadOnlyList<string> channelNames,
        IReadOnlyList<DateTime> timestamps,
        IReadOnlyList<long> sequences,
        IReadOnlyList<double[]> columnMajorValues)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine("# UPET AcqLab ASCII export");
        writer.WriteLine($"# Channels: {channelNames.Count}");
        writer.WriteLine($"# Samples: {timestamps.Count}");
        writer.Write("Timestamp\tSequence");
        foreach (var name in channelNames)
            writer.Write($"\t{name}");
        writer.WriteLine();

        for (var i = 0; i < timestamps.Count; i++)
        {
            writer.Write(timestamps[i].ToString("O", CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(sequences[i].ToString(CultureInfo.InvariantCulture));
            for (var c = 0; c < channelNames.Count; c++)
            {
                writer.Write('\t');
                var v = columnMajorValues[c][i];
                writer.Write(double.IsNaN(v) ? "NaN" : v.ToString("G17", CultureInfo.InvariantCulture));
            }
            writer.WriteLine();
        }
    }

    public static void ExportFromCsv(string csvPath, string txtPath)
    {
        var lines = CsvSharedIO.ReadAllLines(csvPath);
        if (lines.Length == 0) throw new InvalidDataException("Empty CSV.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(txtPath))!);
        using var writer = new StreamWriter(txtPath, false, Encoding.UTF8);
        writer.WriteLine("# UPET AcqLab ASCII export (from CSV)");
        foreach (var line in lines)
            writer.WriteLine(line.Replace(',', '\t'));
    }
}
