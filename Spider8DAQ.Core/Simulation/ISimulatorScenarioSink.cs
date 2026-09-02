namespace Spider8DAQ.Core.Simulation;

/// <summary>Implemented by Simulator backends that accept an optional experiment hint.</summary>
public interface ISimulatorScenarioSink
{
    void SetScenarioHint(SimulatorScenarioHint? hint);
}
