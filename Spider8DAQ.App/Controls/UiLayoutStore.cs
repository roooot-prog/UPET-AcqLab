using System.IO;
using System.Text.Json;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App.Controls;

public sealed class CardLayoutEntry
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    /// <summary>When false, card starts hidden (e.g. Grafic live). Null = visible.</summary>
    public bool? Visible { get; set; }
    /// <summary>When true, card is docked on the minimized bottom bar.</summary>
    public bool? Minimized { get; set; }
}

public sealed class UiLayoutDocument
{
    /// <summary>1 = original rack; 2 = Canale DAQ taller so Operator plate + 8–9 rows fit without a v-scroll.</summary>
    public int Version { get; set; } = 1;
    public Dictionary<string, CardLayoutEntry> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Persists movable card positions under %LocalAppData%\UPETAcqLab\ui-layout.json.</summary>
public static class UiLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>3.3.92: Canale DAQ default height fits header + 8–9 rows + toolbar + right plate.</summary>
    public const int CurrentVersion = 2;

    public static string LayoutFilePath =>
        Path.Combine(UpetLicense.AppDataDirectory, "ui-layout.json");

    public static UiLayoutDocument Load()
    {
        UiLayoutDocument doc;
        var existed = false;
        try
        {
            var path = LayoutFilePath;
            if (File.Exists(path))
            {
                existed = true;
                var json = File.ReadAllText(path);
                doc = JsonSerializer.Deserialize<UiLayoutDocument>(json, JsonOptions) ?? new UiLayoutDocument();
            }
            else
                doc = new UiLayoutDocument();
        }
        catch
        {
            doc = new UiLayoutDocument();
        }

        var before = doc.Version;
        MigrateChannelsCardSize(doc);
        if (existed && doc.Version != before)
            Save(doc);
        return doc;
    }

    /// <summary>
    /// One-shot 3.3.92: enlarge a too-short Canale DAQ tile saved from 3.3.90/91 so the far-right scrollbar goes away.
    /// </summary>
    public static void MigrateChannelsCardSize(UiLayoutDocument doc)
    {
        if (doc.Version >= CurrentVersion) return;
        var defaults = DefaultLayouts();
        if (defaults.TryGetValue("measure.channels", out var defCh)
            && doc.Cards.TryGetValue("measure.channels", out var saved))
        {
            saved.Width = Math.Max(saved.Width, defCh.Width);
            saved.Height = Math.Max(saved.Height, defCh.Height);
        }

        if (doc.Cards.TryGetValue("measure.channels", out var ch)
            && doc.Cards.TryGetValue("measure.sensors", out var sen))
        {
            var bottom = ch.Top + ch.Height;
            if (sen.Top < bottom + 4)
                sen.Top = bottom + 4;
            if (doc.Cards.TryGetValue("measure.tools", out var tools)
                && tools.Top < sen.Top + sen.Height + 4)
                tools.Top = sen.Top + sen.Height + 4;
        }

        doc.Version = CurrentVersion;
    }

    public static void Save(UiLayoutDocument doc)
    {
        try
        {
            doc.Version = Math.Max(doc.Version, CurrentVersion);
            Directory.CreateDirectory(UpetLicense.AppDataDirectory);
            File.WriteAllText(LayoutFilePath, JsonSerializer.Serialize(doc, JsonOptions));
        }
        catch
        {
            /* non-fatal */
        }
    }

    public static void Clear()
    {
        try
        {
            var path = LayoutFilePath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            /* non-fatal */
        }
    }

    /// <summary>Rack-like defaults: left = canale/senzori/tools, center = plot, bottom = job/ghid.</summary>
    public static Dictionary<string, CardLayoutEntry> DefaultLayouts() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["measure.channels"] = new() { Left = 4, Top = 4, Width = 720, Height = 520 },
        ["measure.sensors"] = new() { Left = 4, Top = 528, Width = 440, Height = 268 },
        ["measure.tools"] = new() { Left = 4, Top = 800, Width = 440, Height = 188 },
        ["measure.plot"] = new() { Left = 728, Top = 4, Width = 740, Height = 556, Visible = false },
        ["measure.workflow"] = new() { Left = 728, Top = 564, Width = 500, Height = 188 },
    };
}
