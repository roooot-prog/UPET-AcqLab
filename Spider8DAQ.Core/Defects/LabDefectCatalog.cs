namespace Spider8DAQ.Core.Defects;

/// <summary>Browsable library of typical Spider8 / lab mistakes with synthetic example signals.</summary>
public static class LabDefectCatalog
{
    public static IReadOnlyList<LabDefectDefinition> All { get; } =
    [
        new()
        {
            Id = "zero-uitat",
            Title = "Zero uitat",
            Category = "Tare / Zero",
            Severity = DefectSeverity.Warning,
            Summary = "Canalul are offset mare pe liber — probabil lipsește Zero după Apply senzor.",
            Symptoms = "Valoare DC mare în repaus; după Start totul e „decalat”; Zero recent absent.",
            LikelyCauses = "Apply senzor fără Zero; schimbare Scale; încărcare pe banc la pornire.",
            FixSteps = "1) Scoateți sarcina.\n2) Zero CH / Zero all (F9).\n3) Verificați citirea ~0.\n4) Abia apoi încărcați.",
            ExampleCaption = "Exemplu: offset ~constant pe liber (înainte de Zero)."
        },
        new()
        {
            Id = "polaritate-inversa",
            Title = "Polaritate inversă",
            Category = "Semn / cablare",
            Severity = DefectSeverity.Warning,
            Summary = "Forță/presiune apare predominant negativă — cablare sau Scale cu semn greșit.",
            Symptoms = "Valori negative la încărcare; min ≪ −max; compresie afișată invers.",
            LikelyCauses = "Polaritate punte; Scale negativ greșit; celulă montată invers.",
            FixSteps = "1) Click dreapta canal → Inversare polaritate (Scale × −1).\n2) Zero pe liber.\n3) Reîncărcați ușor și verificați semnul.",
            ExampleCaption = "Exemplu: sarcină crescută → semnal tot mai negativ."
        },
        new()
        {
            Id = "canal-gresit",
            Title = "Canal greșit / Rec pe canal mort",
            Category = "Mapare",
            Severity = DefectSeverity.Warning,
            Summary = "Înregistrați un canal plat, în timp ce alt canal On are semnal util.",
            Symptoms = "CSV pe CH greșit; grafic live pe alt canal; Rec bifat pe canal fără senzor.",
            LikelyCauses = "Apply senzor pe CH greșit; Rec uitat pe canalul bun; OMB mapare.",
            FixSteps = "1) Identificați canalul cu semnal pe grafic.\n2) Apply senzorul pe acel CH.\n3) Rec On pe canalul util, Off pe cele moarte.\n4) Record din nou.",
            ExampleCaption = "Exemplu: CH0 plat, CH1 cu semnal — Rec era pe CH0."
        },
        new()
        {
            Id = "drift",
            Title = "Drift / derapare lentă",
            Category = "Stabilitate",
            Severity = DefectSeverity.Info,
            Summary = "Semnalul se mișcă lent fără sarcină aparentă (termic, cablu, Zero vechi).",
            Symptoms = "Trend monoton după Zero; „plimbare” a zero-ului; histereză mare.",
            LikelyCauses = "Încălzire punte Half; cablu tras; Zero făcut sub sarcină; LPF lung.",
            FixSteps = "1) Așteptați 1–2 min după alimentare.\n2) Zero din nou pe liber.\n3) Verificați cablul DB15.\n4) Half-bridge: Exc V și compensare.",
            ExampleCaption = "Exemplu: rampă lentă după Zero (drift)."
        },
        new()
        {
            Id = "saturare",
            Title = "Saturare / plafon",
            Category = "Domeniu",
            Severity = DefectSeverity.Critical,
            Summary = "Semnalul atinge plafonul (Capacity / range) și rămâne „lipit”.",
            Symptoms = "Max plat pe interval lung; formă tăiată; Scale/Capacity nepotrivite.",
            LikelyCauses = "Sarcină > domeniu; Scale prea mare; range mV/V greșit; senzor greșit.",
            FixSteps = "1) Reduceți sarcina.\n2) Verificați Capacity senzorului.\n3) Recalculați Scale (Apply senzor).\n4) Schimbați range dacă e cazul.",
            ExampleCaption = "Exemplu: curbă tăiată la plafon (saturare)."
        },
        new()
        {
            Id = "semnal-plat",
            Title = "Semnal plat (senzor / cablu)",
            Category = "Cablare",
            Severity = DefectSeverity.Warning,
            Summary = "Canal On cu semnal aproape constant pe o fereastră susținută — tipic senzor deconectat sau cablu rupt.",
            Symptoms = "Linie orizontală; σ / range foarte mici; uneori DC „plutitor” ≠ 0 după Zero.",
            LikelyCauses = "DB15 scos; fir rupt; senzor neconectat; canal greșit On; punte deschisă.",
            FixSteps = "1) Verificați cablul / DB15.\n2) Confirmați senzorul pe canalul On.\n3) Apply senzor + Start, apoi Zero pe liber.\n4) Preflight / EST dacă tot e plat.",
            ExampleCaption = "Exemplu: semnal plat (fără dinamică) pe canal activ."
        },
        new()
        {
            Id = "cablu-desprins",
            Title = "Cablu desprins / semnal mort",
            Category = "Cablare",
            Severity = DefectSeverity.Critical,
            Summary = "Canal On dar semnal plat/zero sau gol — cablu, OMB sau senzor neconectat.",
            Symptoms = "Citire 0 constant; fără zgomot; LED/EST ok dar canal mort.",
            LikelyCauses = "DB15 scos; fir rupt; canal greșit On; punte deschisă.",
            FixSteps = "1) Verificați conectorul DB15.\n2) Un singur canal On de test.\n3) Apply senzor + Start.\n4) Preflight / EST.",
            ExampleCaption = "Exemplu: linie moartă (aproape zero, fără dinamică)."
        },
        new()
        {
            Id = "shunt-gresit",
            Title = "Șunt greșit / treaptă artificială",
            Category = "Calibrare",
            Severity = DefectSeverity.Warning,
            Summary = "Salt brusc tipic de șunt sau calibrare — confuz cu sarcină reală.",
            Symptoms = "Step abrupt fără încărcare mecanică; valoare rămâne pe palier.",
            LikelyCauses = "Shunt check activ; R_shunt greșit; Apply mid-stream.",
            FixSteps = "1) Nu confundați Shunt cu sarcină.\n2) Verificați R_shunt pe senzor.\n3) Zero după Shunt.\n4) Repetați măsurarea fără Shunt.",
            ExampleCaption = "Exemplu: salt tip șunt, apoi palier."
        },
        new()
        {
            Id = "presiune-fara-zero",
            Title = "Presiune fără Zero",
            Category = "Presiune / hidraulic",
            Severity = DefectSeverity.Warning,
            Summary = "Traductor de presiune pornit cu offset (atmosferă / Zero uitat).",
            Symptoms = "bar ≠ ~0 la atmosferă; masă kg greșită din P×A; Zero niciodată apăsat.",
            LikelyCauses = "P15/DC fără Zero; Scale aplicat după ce exista presiune; țeavă sub presiune.",
            FixSteps = "1) Deschideți la atmosferă.\n2) Zero pe canalul de presiune.\n3) Verificați ~0 bar.\n4) Apoi presiune→kg dacă e cazul.",
            ExampleCaption = "Exemplu: offset de presiune înainte de Zero."
        }
    ];

    public static LabDefectDefinition? Find(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Synthetic Y samples (≈2 s @ 50 Hz) illustrating the defect.</summary>
    public static double[] BuildExampleSignal(string defectId, int samples = 100)
    {
        samples = Math.Clamp(samples, 40, 400);
        var y = new double[samples];
        var rnd = new Random(defectId.GetHashCode());
        switch (defectId.ToLowerInvariant())
        {
            case "zero-uitat":
            case "presiune-fara-zero":
                for (var i = 0; i < samples; i++)
                    y[i] = 42.5 + 0.15 * Math.Sin(i * 0.2) + (rnd.NextDouble() - 0.5) * 0.05;
                break;
            case "polaritate-inversa":
                for (var i = 0; i < samples; i++)
                {
                    var load = Math.Max(0, (i - 20) / (double)(samples - 20));
                    y[i] = -load * 180 + (rnd.NextDouble() - 0.5) * 1.5;
                }
                break;
            case "canal-gresit":
                // Flat "recorded" channel (example focuses on dead channel shape).
                for (var i = 0; i < samples; i++)
                    y[i] = (rnd.NextDouble() - 0.5) * 0.02;
                break;
            case "drift":
                for (var i = 0; i < samples; i++)
                    y[i] = i * (8.0 / samples) + (rnd.NextDouble() - 0.5) * 0.08;
                break;
            case "saturare":
                for (var i = 0; i < samples; i++)
                {
                    var v = i < samples * 0.35 ? i * (220.0 / (samples * 0.35)) : 220;
                    y[i] = Math.Min(220, v) + (rnd.NextDouble() - 0.5) * 0.3;
                }
                break;
            case "semnal-plat":
                for (var i = 0; i < samples; i++)
                    y[i] = 12.4 + (rnd.NextDouble() - 0.5) * 0.008;
                break;
            case "cablu-desprins":
                for (var i = 0; i < samples; i++)
                    y[i] = (rnd.NextDouble() - 0.5) * 0.004;
                break;
            case "shunt-gresit":
                for (var i = 0; i < samples; i++)
                    y[i] = (i < samples / 3 ? 0.2 : 55) + (rnd.NextDouble() - 0.5) * 0.4;
                break;
            default:
                for (var i = 0; i < samples; i++)
                    y[i] = Math.Sin(i * 0.15) * 10;
                break;
        }

        return y;
    }
}
