using System.Text;

namespace Spider8DAQ.Core.Import;

/// <summary>
/// Best-effort reader for legacy HBM/catman binary blobs.
/// Classic .bin layouts vary by version; this extracts printable metadata and basic float runs when possible.
/// </summary>
public sealed class CatmanBinInfo
{
    public string Path { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Summary { get; init; } = "";
    public List<string> Strings { get; init; } = new();
    public List<double> PreviewValues { get; init; } = new();
}

public static class CatmanBinImporter
{
    public static CatmanBinInfo Inspect(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var strings = ExtractAsciiStrings(bytes, minLen: 4).Take(80).ToList();
        var floats = ExtractFloatRuns(bytes).Take(200).ToList();
        return new CatmanBinInfo
        {
            Path = path,
            SizeBytes = bytes.LongLength,
            Strings = strings,
            PreviewValues = floats,
            Summary = $"Size={bytes.Length} bytes; strings={strings.Count}; float-samples≈{floats.Count}. " +
                      "Full catman BIN decode depends on file version — use as metadata/preview viewer."
        };
    }

    private static IEnumerable<string> ExtractAsciiStrings(byte[] data, int minLen)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            if (b >= 32 && b <= 126) sb.Append((char)b);
            else
            {
                if (sb.Length >= minLen) yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length >= minLen) yield return sb.ToString();
    }

    private static IEnumerable<double> ExtractFloatRuns(byte[] data)
    {
        for (var i = 0; i + 4 <= data.Length; i += 4)
        {
            var v = BitConverter.ToSingle(data, i);
            if (!float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) < 1e6 && Math.Abs(v) > 1e-9)
                yield return v;
        }
    }
}
