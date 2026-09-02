using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Specimens;

/// <summary>Built-in catalog + operator custom cards; diacritics-insensitive search.</summary>
public sealed class SpecimenLibrary
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int CatalogVersion { get; set; } = SpecimenCatalog.CatalogVersion;
    /// <summary>
    /// Last picked catalog/custom id. Empty string is a valid "none" state
    /// (do not auto-select a card). Null on first run also means none.
    /// </summary>
    public string? LastSpecimenId { get; set; }
    public List<SpecimenCard> CustomCards { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyList<SpecimenCard> CatalogCards { get; private set; } = SpecimenCatalog.Build();

    public IEnumerable<SpecimenCard> AllCards()
    {
        foreach (var c in CatalogCards)
            yield return c;
        foreach (var c in CustomCards)
            yield return c;
    }

    public SpecimenCard? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return AllCards().FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Persist last pick. Empty / whitespace stores "none" (not the first catalog card).</summary>
    public void RememberLast(string? id, string? path = null)
    {
        LastSpecimenId = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
        Save(path);
    }

    /// <summary>Match name + aliases + class. Empty query returns all (catalog then custom).</summary>
    public IReadOnlyList<SpecimenCard> Search(string? query)
    {
        var q = Fold(query);
        var all = AllCards().ToList();
        if (q.Length == 0) return all;

        var hits = new List<SpecimenCard>();
        foreach (var c in all)
        {
            if (CardMatches(c, q))
                hits.Add(c);
        }
        return hits;
    }

    public void AddOrUpdateCustom(SpecimenCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Id))
            card.Id = "custom-" + Guid.NewGuid().ToString("N")[..10];
        card.IsCustom = true;
        if (string.IsNullOrWhiteSpace(card.Class))
            card.Class = SpecimenClasses.Personalizat;
        if (string.IsNullOrWhiteSpace(card.FormulaPack))
            card.FormulaPack = FormulaPacks.DefaultForClass(card.Class);
        if (string.IsNullOrWhiteSpace(card.Shape))
            card.Shape = SpecimenShapes.Cilindru;

        var i = CustomCards.FindIndex(x =>
            string.Equals(x.Id, card.Id, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) CustomCards[i] = card;
        else CustomCards.Add(card);
    }

    public static SpecimenLibrary Load(string? path = null)
    {
        path ??= AppPaths.SpecimenLibrary;
        var lib = new SpecimenLibrary();
        try
        {
            if (File.Exists(path))
            {
                var disk = JsonSerializer.Deserialize<SpecimenLibrary>(File.ReadAllText(path), JsonOpts);
                if (disk is not null)
                {
                    lib.LastSpecimenId = disk.LastSpecimenId;
                    lib.CustomCards = disk.CustomCards ?? new List<SpecimenCard>();
                    foreach (var c in lib.CustomCards)
                    {
                        c.IsCustom = true;
                        if (string.IsNullOrWhiteSpace(c.Class))
                            c.Class = SpecimenClasses.Personalizat;
                        if (string.IsNullOrWhiteSpace(c.FormulaPack))
                            c.FormulaPack = FormulaPacks.DefaultForClass(c.Class);
                    }
                }
            }
        }
        catch
        {
            /* keep defaults */
        }

        lib.CatalogCards = SpecimenCatalog.Build();
        lib.CatalogVersion = SpecimenCatalog.CatalogVersion;
        return lib;
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SpecimenLibrary;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                AppPaths.EnsureWritable(dir);
            var dto = new SpecimenLibrary
            {
                CatalogVersion = CatalogVersion,
                LastSpecimenId = LastSpecimenId,
                CustomCards = CustomCards
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch
        {
            /* non-fatal */
        }
    }

    public static bool CardMatches(SpecimenCard c, string foldedQuery)
    {
        if (foldedQuery.Length == 0) return true;
        if (ContainsFolded(c.NameRo, foldedQuery)) return true;
        if (ContainsFolded(c.Class, foldedQuery)) return true;
        if (ContainsFolded(c.FormulaPackLabel, foldedQuery)) return true;
        if (ContainsFolded(c.Id, foldedQuery)) return true;
        foreach (var a in c.Aliases)
        {
            if (ContainsFolded(a, foldedQuery)) return true;
        }
        return false;
    }

    public static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var n = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool ContainsFolded(string? hay, string foldedNeedle)
    {
        if (string.IsNullOrEmpty(hay) || foldedNeedle.Length == 0) return false;
        return Fold(hay).Contains(foldedNeedle, StringComparison.Ordinal);
    }
}
