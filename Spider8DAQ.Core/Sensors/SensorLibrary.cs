using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Sensors;

public sealed class SensorDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Code { get; set; } = "";
    public string Category { get; set; } = SensorCategories.Custom;
    public string Name { get; set; } = "Sensor";
    public string Unit { get; set; } = "mV/V";
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; }
    public string TransducerType { get; set; } = "Generic";
    public string Notes { get; set; } = "";
    public double Sensitivity { get; set; } = 1.0;
    public double ExcitationV { get; set; } = 2.5;
    public double Capacity { get; set; }
    public string Bridge { get; set; } = "Full";
    public double FilterHz { get; set; } = 10;
    public double RangeMvPerV { get; set; } = 2;
    /// <summary>Preferred channel sample rate (catman Easy Sample/Filter).</summary>
    public int ChannelSampleRateHz { get; set; } = 50;
    /// <summary>R-Shunt in kΩ when known (Easy Sensor DB); 0 = unspecified.</summary>
    public double ShuntKohm { get; set; }
    /// <summary>
    /// Easy two-point Point1 electrical (mV/V) mapped to channel TareValue on Apply.
    /// Nominal catalog = 0; live cal may set e.g. -0.00864.
    /// </summary>
    public double ZeroElectricalMvPerV { get; set; }
}

public sealed class SensorLibrary
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public int CatalogVersion { get; set; }
    public List<SensorDefinition> Sensors { get; set; } = new();

    public IEnumerable<string> Categories =>
        Sensors.Select(s => string.IsNullOrWhiteSpace(s.Category) ? SensorCategories.Custom : s.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c);

    public IEnumerable<SensorDefinition> ByCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "(All)" || category == "(Toate)")
            return Sensors.OrderBy(s => s.Category).ThenBy(s => s.Code).ThenBy(s => s.Name);
        return Sensors
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Code)
            .ThenBy(s => s.Name);
    }

    public static SensorLibrary CreateDefault() => SensorCatalog.BuildLargeLibrary();

    public static async Task<SensorLibrary> LoadAsync(string path, CancellationToken ct = default)
    {
        var catalog = CreateDefault();
        if (!File.Exists(path))
        {
            await TrySaveAsync(path, catalog, ct);
            return catalog;
        }

        SensorLibrary? disk = null;
        Exception? lastReadError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                disk = await JsonSerializer.DeserializeAsync<SensorLibrary>(stream, Options, ct);
                lastReadError = null;
                break;
            }
            catch (IOException ex)
            {
                lastReadError = ex;
                await Task.Delay(80 * (attempt + 1), ct);
            }
            catch (JsonException)
            {
                // Corrupt JSON — regenerate below.
                disk = null;
                break;
            }
        }

        if (lastReadError is not null && disk is null)
            throw lastReadError;

        if (disk is null || disk.Sensors.Count == 0)
        {
            await TrySaveAsync(path, catalog, ct);
            return catalog;
        }

        foreach (var s in disk.Sensors)
        {
            if (string.IsNullOrWhiteSpace(s.Category)) s.Category = SensorCategories.Custom;
            if (string.IsNullOrWhiteSpace(s.Code))
                s.Code = $"{SensorCategories.CodePrefix(s.Category)}-{s.Id}";
        }

        // Upgrade when catalog version bumps, library is smaller, or catalog sensors are missing (e.g. new U2B).
        var missingCatalog = CatalogSensorsMissing(disk, catalog);
        if (disk.CatalogVersion < SensorCatalog.CatalogVersion
            || disk.Sensors.Count < catalog.Sensors.Count
            || missingCatalog)
        {
            foreach (var s in disk.Sensors)
            {
                var match = catalog.Sensors.FirstOrDefault(x =>
                    string.Equals(x.Id, s.Id, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(s.Code) && string.Equals(x.Code, s.Code, StringComparison.OrdinalIgnoreCase)));
                if (match is not null)
                {
                    // Catalog wins definitions on version bump (avoids stale Scale from old kN/N mistakes).
                    // Keep only user Offset (soft zero / DC offset).
                    match.Offset = s.Offset;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(s.Category) ||
                    !SensorCategories.All.Contains(s.Category))
                    s.Category = SensorCategories.Custom;
                catalog.Sensors.Add(s);
            }
            catalog.CatalogVersion = SensorCatalog.CatalogVersion;
            // Non-fatal: Program Files / locked file must not blank the UI.
            await TrySaveAsync(path, catalog, ct);
            return catalog;
        }

        return disk;
    }

    /// <summary>True when any embedded-catalog sensor Id/Code is absent from the on-disk library.</summary>
    private static bool CatalogSensorsMissing(SensorLibrary disk, SensorLibrary catalog)
    {
        foreach (var c in catalog.Sensors)
        {
            var found = disk.Sensors.Any(x =>
                string.Equals(x.Id, c.Id, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(c.Code) &&
                 string.Equals(x.Code, c.Code, StringComparison.OrdinalIgnoreCase)));
            if (!found) return true;
        }
        return false;
    }

    public static async Task SaveAsync(string path, SensorLibrary library, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        await using (var stream = new FileStream(
                         tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, library, Options, ct);
            await stream.FlushAsync(ct);
        }

        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Copy(tmp, path, overwrite: true);
                try { File.Delete(tmp); } catch { /* ignore */ }
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                await Task.Delay(60 * (attempt + 1), ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
                break;
            }
        }

        try { File.Delete(tmp); } catch { /* ignore */ }
        if (last is not null) throw last;
        throw new IOException("Nu s-a putut salva catalogul de senzori: " + path);
    }

    /// <summary>Best-effort save; never throws (used during upgrade / seed).</summary>
    public static async Task<bool> TrySaveAsync(string path, SensorLibrary library, CancellationToken ct = default)
    {
        try
        {
            await SaveAsync(path, library, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task ImportMergeAsync(string path, SensorLibrary target, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var imported = await JsonSerializer.DeserializeAsync<SensorLibrary>(stream, Options, ct)
                       ?? new SensorLibrary();
        foreach (var s in imported.Sensors)
        {
            if (string.IsNullOrWhiteSpace(s.Category)) s.Category = SensorCategories.Custom;
            var existing = target.Sensors.FirstOrDefault(x =>
                string.Equals(x.Id, s.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) target.Sensors.Add(s);
            else
            {
                existing.Category = s.Category;
                existing.Code = string.IsNullOrWhiteSpace(s.Code) ? existing.Code : s.Code;
                existing.Unit = s.Unit;
                existing.Scale = s.Scale;
                existing.Offset = s.Offset;
                existing.Sensitivity = s.Sensitivity;
                existing.ExcitationV = s.ExcitationV;
                existing.Capacity = s.Capacity;
                existing.Bridge = s.Bridge;
                existing.FilterHz = s.FilterHz;
                existing.RangeMvPerV = s.RangeMvPerV;
                existing.ChannelSampleRateHz = s.ChannelSampleRateHz > 0 ? s.ChannelSampleRateHz : existing.ChannelSampleRateHz;
                existing.ShuntKohm = s.ShuntKohm;
                existing.ZeroElectricalMvPerV = s.ZeroElectricalMvPerV;
                existing.Notes = s.Notes;
                existing.TransducerType = s.TransducerType;
            }
        }
    }

    public SensorDefinition? FindByName(string name)
        => Sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public SensorDefinition? FindByCode(string code)
        => Sensors.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
}
