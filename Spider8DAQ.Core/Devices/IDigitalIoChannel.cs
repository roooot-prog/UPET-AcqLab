namespace Spider8DAQ.Core.Devices;

/// <summary>
/// Optional DigIO / digital bitmask channel (catman CH8) when the backend reports it.
/// Index is 0-based device index — typically 8 for Spider8.
/// </summary>
public interface IDigitalIoChannel
{
    /// <summary>0-based index of DigIO channel, or null if not present.</summary>
    int? DigitalChannelIndex { get; }
}
