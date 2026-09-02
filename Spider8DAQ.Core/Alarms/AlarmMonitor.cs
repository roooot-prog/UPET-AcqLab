namespace Spider8DAQ.Core.Alarms;

public enum AlarmState
{
    Normal,
    Active,
    Latched
}

public sealed class AlarmChannelState
{
    public int ChannelIndex { get; set; }
    public string ChannelName { get; set; } = "";
    public AlarmState State { get; set; } = AlarmState.Normal;
    public double LastValue { get; set; } = double.NaN;
    public DateTime? ActivatedUtc { get; set; }
    public string Message { get; set; } = "";
}

public sealed class AlarmEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public int ChannelIndex { get; init; }
    public string ChannelName { get; init; } = "";
    public string Message { get; init; } = "";
    public bool Acknowledged { get; set; }
}

public sealed class AlarmMonitor
{
    private readonly Dictionary<int, AlarmChannelState> _states = new();
    private readonly List<AlarmEvent> _events = new();

    public double Hysteresis { get; set; } = 0.05;
    public bool LatchEnabled { get; set; } = true;
    public IReadOnlyList<AlarmEvent> Events => _events;

    public event EventHandler<AlarmEvent>? AlarmFired;

    public AlarmChannelState Evaluate(
        int index,
        string name,
        double value,
        bool enabled,
        double low,
        double high)
    {
        if (!_states.TryGetValue(index, out var st))
        {
            st = new AlarmChannelState { ChannelIndex = index, ChannelName = name };
            _states[index] = st;
        }

        st.ChannelName = name;
        st.LastValue = value;
        if (!enabled || double.IsNaN(value))
        {
            if (!LatchEnabled) st.State = AlarmState.Normal;
            return st;
        }

        var enterHigh = high - Hysteresis;
        var enterLow = low + Hysteresis;
        var inAlarm = value > enterHigh || value < enterLow;

        if (inAlarm)
        {
            if (st.State == AlarmState.Normal)
            {
                st.State = LatchEnabled ? AlarmState.Latched : AlarmState.Active;
                st.ActivatedUtc = DateTime.UtcNow;
                st.Message = value > enterHigh ? $"HIGH {value:0.####}>{high:0.####}" : $"LOW {value:0.####}<{low:0.####}";
                var ev = new AlarmEvent
                {
                    ChannelIndex = index,
                    ChannelName = name,
                    Message = st.Message
                };
                _events.Insert(0, ev);
                if (_events.Count > 2000) _events.RemoveAt(_events.Count - 1);
                AlarmFired?.Invoke(this, ev);
            }
            else if (!LatchEnabled)
            {
                st.State = AlarmState.Active;
            }
        }
        else if (!LatchEnabled || st.State == AlarmState.Active)
        {
            st.State = AlarmState.Normal;
            st.Message = "";
        }

        return st;
    }

    public void Acknowledge(int channelIndex)
    {
        if (_states.TryGetValue(channelIndex, out var st) && st.State == AlarmState.Latched)
        {
            st.State = AlarmState.Normal;
            st.Message = "ACK";
        }
        foreach (var e in _events.Where(x => x.ChannelIndex == channelIndex && !x.Acknowledged))
            e.Acknowledged = true;
    }

    public void AcknowledgeAll()
    {
        foreach (var st in _states.Values)
        {
            if (st.State == AlarmState.Latched) st.State = AlarmState.Normal;
        }
        foreach (var e in _events) e.Acknowledged = true;
    }
}
