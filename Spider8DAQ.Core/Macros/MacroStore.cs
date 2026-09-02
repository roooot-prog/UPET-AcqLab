using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Macros;

public static class MacroStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SaveAsync(string path, MacroDefinition macro)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(macro, Options));
    }

    public static async Task<MacroDefinition> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<MacroDefinition>(json, Options)
               ?? MacroRunner.CreateDefaultMeasureSequence();
    }
}
