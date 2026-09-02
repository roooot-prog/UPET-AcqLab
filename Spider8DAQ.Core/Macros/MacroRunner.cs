namespace Spider8DAQ.Core.Macros;

public enum MacroStepType
{
    TareAll,
    WaitMs,
    StartStreaming,
    StopStreaming,
    StartRecording,
    StopRecording,
    Status,
    LoopStart,
    LoopEnd,
    WaitTrigger,
    SetSampleRate,
    ApplyFilters,
    WaitOperator,
    Beep
}

public sealed class MacroStep
{
    public MacroStepType Type { get; set; }
    public int IntParam { get; set; }
    public string Text { get; set; } = "";
}

public sealed class MacroDefinition
{
    public string Name { get; set; } = "Macro";
    public List<MacroStep> Steps { get; set; } = new();
}

public sealed class MacroRunner
{
    public interface IMacroHost
    {
        Task TareAllAsync();
        Task StartStreamingAsync();
        Task StopStreamingAsync();
        Task StartRecordingAsync();
        Task StopRecordingAsync();
        void SetStatus(string message);
        bool IsConnected { get; }
        bool IsTriggerSatisfied { get; }
        Task SetSampleRateAsync(int hz);
        Task ApplyFiltersAsync(double filterHz);
        /// <summary>Blocks until the operator continues (UI button) or token cancels.</summary>
        Task WaitOperatorAsync(string message, CancellationToken ct);
        void Beep();
    }

    public async Task RunAsync(MacroDefinition macro, IMacroHost host, CancellationToken ct = default)
    {
        host.SetStatus($"Macro '{macro.Name}' pornit.");
        var steps = macro.Steps;
        var i = 0;
        var loopStack = new Stack<(int start, int remaining)>();

        while (i < steps.Count)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            switch (step.Type)
            {
                case MacroStepType.TareAll:
                    host.SetStatus("Macro: tare / zero");
                    await host.TareAllAsync();
                    i++;
                    break;
                case MacroStepType.WaitMs:
                    host.SetStatus($"Macro: așteptare {step.IntParam} ms");
                    await Task.Delay(Math.Max(0, step.IntParam), ct);
                    i++;
                    break;
                case MacroStepType.StartStreaming:
                    host.SetStatus("Macro: start măsurare");
                    await host.StartStreamingAsync();
                    i++;
                    break;
                case MacroStepType.StopStreaming:
                    host.SetStatus("Macro: stop măsurare");
                    await host.StopStreamingAsync();
                    i++;
                    break;
                case MacroStepType.StartRecording:
                    host.SetStatus("Macro: start înregistrare");
                    await host.StartRecordingAsync();
                    i++;
                    break;
                case MacroStepType.StopRecording:
                    host.SetStatus("Macro: stop înregistrare");
                    await host.StopRecordingAsync();
                    i++;
                    break;
                case MacroStepType.Status:
                    host.SetStatus(string.IsNullOrWhiteSpace(step.Text) ? "Macro checkpoint" : step.Text);
                    i++;
                    break;
                case MacroStepType.WaitTrigger:
                    host.SetStatus("Macro: aștept trigger…");
                    while (!host.IsTriggerSatisfied)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(20, ct);
                    }
                    i++;
                    break;
                case MacroStepType.SetSampleRate:
                    host.SetStatus($"Macro: rată eșantionare {step.IntParam} Hz");
                    await host.SetSampleRateAsync(Math.Max(1, step.IntParam));
                    i++;
                    break;
                case MacroStepType.ApplyFilters:
                    var fHz = step.IntParam > 0 ? step.IntParam : 10;
                    host.SetStatus($"Macro: filtre {fHz} Hz pe canale activate");
                    await host.ApplyFiltersAsync(fHz);
                    i++;
                    break;
                case MacroStepType.WaitOperator:
                    var msg = string.IsNullOrWhiteSpace(step.Text)
                        ? "Macro: aștept confirmare operator (Continuă macro)…"
                        : step.Text;
                    await host.WaitOperatorAsync(msg, ct);
                    i++;
                    break;
                case MacroStepType.Beep:
                    host.Beep();
                    host.SetStatus(string.IsNullOrWhiteSpace(step.Text) ? "Macro: semnal acustic" : step.Text);
                    i++;
                    break;
                case MacroStepType.LoopStart:
                    loopStack.Push((i + 1, Math.Max(1, step.IntParam)));
                    i++;
                    break;
                case MacroStepType.LoopEnd:
                    if (loopStack.Count > 0)
                    {
                        var (start, remaining) = loopStack.Pop();
                        remaining--;
                        if (remaining > 0)
                        {
                            loopStack.Push((start, remaining));
                            i = start;
                        }
                        else i++;
                    }
                    else i++;
                    break;
                default:
                    i++;
                    break;
            }
        }

        host.SetStatus($"Macro '{macro.Name}' finalizat.");
    }

    public static MacroDefinition CreateDefaultMeasureSequence(int settleMs = 1000, int recordMs = 5000)
        => new()
        {
            Name = "Tare → Wait → Record → Stop",
            Steps =
            {
                new() { Type = MacroStepType.StartStreaming },
                new() { Type = MacroStepType.WaitMs, IntParam = settleMs },
                new() { Type = MacroStepType.TareAll },
                new() { Type = MacroStepType.WaitMs, IntParam = 500 },
                new() { Type = MacroStepType.StartRecording },
                new() { Type = MacroStepType.WaitMs, IntParam = recordMs },
                new() { Type = MacroStepType.StopRecording },
                new() { Type = MacroStepType.StopStreaming },
                new() { Type = MacroStepType.Status, Text = "Secvență completă." }
            }
        };

    public static MacroDefinition CreateLabJobSequence(int sampleRateHz = 50, int filterHz = 10, int settleMs = 800, int recordMs = 10000)
        => new()
        {
            Name = "Job lab: rată → filtru → tare → record",
            Steps =
            {
                new() { Type = MacroStepType.SetSampleRate, IntParam = sampleRateHz },
                new() { Type = MacroStepType.ApplyFilters, IntParam = filterHz },
                new() { Type = MacroStepType.StartStreaming },
                new() { Type = MacroStepType.WaitMs, IntParam = settleMs },
                new() { Type = MacroStepType.WaitOperator, Text = "Verifică cablajul / senzorii, apoi Continuă macro." },
                new() { Type = MacroStepType.TareAll },
                new() { Type = MacroStepType.Beep },
                new() { Type = MacroStepType.StartRecording },
                new() { Type = MacroStepType.WaitMs, IntParam = recordMs },
                new() { Type = MacroStepType.StopRecording },
                new() { Type = MacroStepType.StopStreaming },
                new() { Type = MacroStepType.Status, Text = "Job lab finalizat — deschide DataViewer / Analysis." }
            }
        };

    public static IReadOnlyList<string> StepTypeNames { get; } =
        Enum.GetNames(typeof(MacroStepType));
}
