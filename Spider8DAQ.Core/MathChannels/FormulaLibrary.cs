using System.Text.Json;



namespace Spider8DAQ.Core.MathChannels;



/// <summary>Bibliotecă formule personalizate.</summary>

public sealed class FormulaEntry

{

    public string Name { get; set; } = "Formula";

    /// <summary>CHn = index HW / nume grilă (CH0…CH7), nu 1-based.</summary>

    public string Expression { get; set; } = "(CH0-CH1)*1";

    public string Notes { get; set; } = "";

}



public sealed class FormulaLibrary

{

    public List<FormulaEntry> Formulas { get; set; } = new();



    public static FormulaLibrary CreateDefault() => new()

    {

        Formulas =

        {

            new() { Name = "Diferență CH0-CH1", Expression = "CH0-CH1", Notes = "Diferențial (index HW = nume grilă)" },

            new() { Name = "Diferență CH1-CH2", Expression = "CH1-CH2", Notes = "ex. presiune CH1 − forță CH2" },

            new() { Name = "Medie 2 canale", Expression = "(CH0+CH1)/2", Notes = "" },

            new() { Name = "Scale 2.5", Expression = "CH0*2.5", Notes = "Conversie mV/V → unități" },

            new() { Name = "Rosette Ex (simplu)", Expression = "CH0", Notes = "ε0 — folosește și op RosetteEx" },

            new() { Name = "Offset zero", Expression = "CH0-0", Notes = "Editează offsetul" }

        }

    };



    public static async Task<FormulaLibrary> LoadAsync(string path)

    {

        if (!File.Exists(path))

        {

            var created = CreateDefault();

            await created.SaveAsync(path);

            return created;

        }



        await using var fs = File.OpenRead(path);

        var lib = await JsonSerializer.DeserializeAsync<FormulaLibrary>(fs, JsonOpts()) ?? CreateDefault();

        if (lib.Formulas.Count == 0) lib = CreateDefault();

        return lib;

    }



    public async Task SaveAsync(string path)

    {

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        await using var fs = File.Create(path);

        await JsonSerializer.SerializeAsync(fs, this, JsonOpts());

    }



    private static JsonSerializerOptions JsonOpts() => new()

    {

        WriteIndented = true,

        PropertyNamingPolicy = JsonNamingPolicy.CamelCase

    };

}


