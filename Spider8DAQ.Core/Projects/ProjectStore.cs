using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Projects;

public static class ProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task SaveAsync(string path, ProjectFile project, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await using var stream = File.Create(full);
        await JsonSerializer.SerializeAsync(stream, project, Options, ct);
    }

    /// <summary>Synchronous save — safe to call from UI thread (no sync-over-async).</summary>
    public static void Save(string path, ProjectFile project)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, JsonSerializer.Serialize(project, Options));
    }

    public static async Task<ProjectFile> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, Options, ct);
        return project ?? throw new InvalidDataException("Invalid project file.");
    }
}
