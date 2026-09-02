namespace Spider8DAQ.App.ViewModels;

/// <summary>Display / plot layouts for UPET AcqLab (HBM Spider8 laboratory UI).</summary>
public static class DisplayLayouts
{
    public const string Yt = "Y(t)";
    public const string Yx = "Y(X)";
    public const string DualYt = "Dual Y(t)";
    public const string Poisson = "Poisson";
    public const string Numeric = "Numeric";
    public const string Bar = "Bar";
    public const string Fft = "FFT";
    public const string Cwt = "CWT";

    public static IReadOnlyList<string> All { get; } =
        [Yt, DualYt, Yx, Poisson, Numeric, Bar, Fft, Cwt];

    public static string Describe(string mode) => mode switch
    {
        Yt => "Y(t) — semnal vs timp, un panou mare (recomandat laborator).",
        DualYt => "Dual Y(t) — panouri grupate pe unitate (µm/m vs bar…); recomandat multi-senzor.",
        Yx => "Y(X) — caracteristică (ex. forță–deplasare).",
        Poisson => "Poisson (ν) — ε_t vs ε_l + Dual Y(t); ν aparent pe zona liniară (auto / cursoare).",
        Numeric => "Afișaj numeric mare (citiri live).",
        Bar => "Bare comparative pe canalele activate.",
        Fft => "Spectru FFT live pe canal selectat.",
        Cwt => "CWT live HQ — Morlet, Smooth, ~15–20 Hz (adaptiv), câte un scalogram per canal On.",
        _ => "Afișaj măsurare."
    };
}

public sealed class ExperimentPreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string PlotMode { get; init; } = DisplayLayouts.Yt;
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "\uE9D9";
}

public static class ExperimentCatalog
{
    public static IReadOnlyList<ExperimentPreset> All { get; } =
    [
        new()
        {
            Id = "static-force",
            Name = "Forță statică",
            PlotMode = DisplayLayouts.Numeric,
            Icon = "\uE9D9",
            Description = "Citiri stabile pe celule de sarcină; afișaj numeric + Y(t) opțional."
        },
        new()
        {
            Id = "force-disp",
            Name = "Forță–deplasare",
            PlotMode = DisplayLayouts.Yx,
            Icon = "\uE9F9",
            Description = "Caracteristică Y(X): forță pe Y, deplasare/LVDT pe X."
        },
        new()
        {
            Id = "fatigue",
            Name = "Ciclic / oboseală",
            PlotMode = DisplayLayouts.DualYt,
            Icon = "\uE9D2",
            Description = "Semnale vs timp pe două panouri; potrivită pentru cicluri lungi."
        },
        new()
        {
            Id = "vibration",
            Name = "Vibrații / FFT",
            PlotMode = DisplayLayouts.Fft,
            Icon = "\uE9E9",
            Description = "Spectru de frecvență live pentru accelerație sau semnal dinamic."
        },
        new()
        {
            Id = "cwt-tf",
            Name = "CWT timp-frecvență",
            PlotMode = DisplayLayouts.Cwt,
            Icon = "\uE9E9",
            Description = "Scalogramă CWT (Morlet) live pe fiecare canal On — strain + forță simultan."
        },
        new()
        {
            Id = "strain",
            Name = "Deformații / tensometrie",
            PlotMode = DisplayLayouts.Yt,
            Icon = "\uE8FD",
            Description = "Y(t) pentru mărci tensometrice și canale math (rozetă)."
        },
        new()
        {
            Id = "poisson",
            Name = "Poisson ν aparent",
            PlotMode = DisplayLayouts.Poisson,
            Icon = "\uE9F9",
            Description = "ε_t vs ε_l + ν aparent pe zona liniară (longitudinal / transversal)."
        },
        new()
        {
            Id = "monitor",
            Name = "Monitorizare multi-canal",
            PlotMode = DisplayLayouts.Bar,
            Icon = "\uE9D1",
            Description = "Bare comparative pentru multe canale simultan."
        },
        new()
        {
            Id = "hydraulic-press",
            Name = "Presă hidraulică",
            PlotMode = DisplayLayouts.Yt,
            Icon = "\uE7C5",
            Description = "Y(t) presiune (bar) + cadran kg — P15, aria piston 201,06 cm², Zero la aer."
        },
        new()
        {
            Id = "pressure",
            Name = "Presiune / proces",
            PlotMode = DisplayLayouts.Yt,
            Icon = "\uE945",
            Description = "Y(t) presiune + cadran live (bar sau kg cu aria piston cm²)."
        },
        new()
        {
            Id = "sensor-verify",
            Name = "Verificare senzor",
            PlotMode = DisplayLayouts.Yt,
            Icon = "\uE7BA",
            Description = "Y(t) + numeric pe canalul selectat — verifică On, Apply, punte și date live (P15/bar: 0 la repaus = OK)."
        }
    ];
}
