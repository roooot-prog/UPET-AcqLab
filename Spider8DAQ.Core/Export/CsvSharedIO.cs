using System.Text;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Read CSV while a <see cref="CsvRecordingWriter"/> may still hold a write handle.
/// <see cref="File.ReadLines"/> / <see cref="File.ReadAllLines"/> use FileShare.Read, which
/// fails on Windows when another process has the file open for write.
/// </summary>
public static class CsvSharedIO
{
    /// <summary>Open for read with share that coexists with an active recorder.</summary>
    public static StreamReader OpenSharedReader(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    public static IEnumerable<string> EnumerateLines(string path)
    {
        using var reader = OpenSharedReader(path);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    public static IEnumerable<string> EnumerateDataLines(string path)
    {
        foreach (var line in EnumerateLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (IsCommentOrMeta(line)) continue;
            yield return line;
        }
    }

    public static string[] ReadAllLines(string path)
    {
        using var reader = OpenSharedReader(path);
        var list = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            list.Add(line);
        return list.ToArray();
    }

    /// <summary># meta / MARK lines; also tolerate a leading UTF-8 BOM if a caller bypasses StreamReader.</summary>
    public static bool IsCommentOrMeta(string line)
    {
        var t = line.AsSpan().TrimStart();
        if (t.Length > 0 && t[0] == '\uFEFF')
            t = t[1..].TrimStart();
        return t.Length > 0 && t[0] == '#';
    }
}
