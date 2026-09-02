using System.Text.Json;
using System.Text.Json.Serialization;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Versioning;

public sealed class ProjectSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Label { get; set; } = "";
    public string Json { get; set; } = "";
}

public static class ProjectVersionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task SaveSnapshotAsync(string folder, ProjectFile project, string label, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var json = JsonSerializer.Serialize(project, Options);
        var snap = new ProjectSnapshot { Label = label, Json = json };
        var path = Path.Combine(folder, $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snap, Options), ct);
    }

    public static IReadOnlyList<string> ListSnapshots(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.GetFiles(folder, "snap_*.json").OrderByDescending(f => f).ToArray();
    }

    public static async Task<ProjectSnapshot?> LoadSnapshotAsync(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ProjectSnapshot>(text, Options);
    }

    public static string DiffJson(string left, string right)
    {
        var a = left.Replace("\r\n", "\n").Split('\n');
        var b = right.Replace("\r\n", "\n").Split('\n');
        var lines = new List<string>();
        var max = Math.Max(a.Length, b.Length);
        for (var i = 0; i < max; i++)
        {
            var la = i < a.Length ? a[i] : "";
            var lb = i < b.Length ? b[i] : "";
            if (la != lb)
            {
                if (!string.IsNullOrEmpty(la)) lines.Add("- " + la);
                if (!string.IsNullOrEmpty(lb)) lines.Add("+ " + lb);
            }
        }
        return lines.Count == 0 ? "(no differences)" : string.Join(Environment.NewLine, lines.Take(500));
    }
}
