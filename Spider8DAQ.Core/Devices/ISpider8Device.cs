namespace Spider8DAQ.Core.Devices;

public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Streaming,
    Error
}

public interface ISpider8Device : IAsyncDisposable
{
    string DisplayName { get; }
    DeviceConnectionState State { get; }
    IReadOnlyList<ChannelConfig> Channels { get; }
    int SampleRateHz { get; set; }
    event EventHandler<SampleFrame>? SampleReceived;
    event EventHandler<string>? StatusChanged;
    /// <summary>Raised when USB/DEST is gone or streaming silence exceeds the lost-comm window.</summary>
    event EventHandler? ConnectionLost;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task StartStreamingAsync(CancellationToken cancellationToken = default);
    Task StopStreamingAsync();
    Task TareAsync(int? channelIndex = null);
    Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels);
    /// <summary>Optional hardware shunt check; adapters may simulate or send SHUNT command.</summary>
    Task<double> ShuntCheckAsync(int channelIndex);
}
