using System.IO.Ports;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

public static class ComPortScanner
{
    public static IReadOnlyList<string> GetPorts()
    {
        try
        {
            return SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
