using System.Runtime.Versioning;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Hardware;

public enum DeviceBackend
{
    Simulator,
    Serial,
    Spider32Dll,
    /// <summary>Direct usbhbm.sys DEST interface (USBHBM…). Prefer over Spider32 PORT_USB.</summary>
    HbmUsb
}

public sealed class DeviceFactoryOptions
{
    public DeviceBackend Backend { get; set; } = DeviceBackend.Simulator;
    public string? ComPort { get; set; }
    public int BaudRate { get; set; } = 9600;
    public string? Spider32DllPath { get; set; }
    public int ChannelCount { get; set; } = 8;
    public int DeviceCount { get; set; } = 1;
    public List<DeviceSlot>? Slots { get; set; }
}

[SupportedOSPlatform("windows")]
public static class DeviceFactory
{
    public static ISpider8Device Create(DeviceFactoryOptions options)
    {
        var slots = (options.Slots ?? new List<DeviceSlot> { new() { Index = 0, Enabled = true, ComPort = options.ComPort } })
            .Where(s => s.Enabled)
            .OrderBy(s => s.Index)
            .Take(8)
            .ToList();
        if (slots.Count == 0)
            slots.Add(new DeviceSlot { Index = 0, Enabled = true, ComPort = options.ComPort });

        if (slots.Count == 1 && options.Backend != DeviceBackend.Simulator)
            return CreateSingle(options, slots[0], Math.Max(8, options.ChannelCount));

        if (options.Backend == DeviceBackend.Simulator && slots.Count == 1)
            return new SimulatedSpider8(Math.Max(options.ChannelCount, 8), Math.Max(1, options.DeviceCount));

        // Multi-device cascade
        var children = new List<ISpider8Device>();
        foreach (var slot in slots)
        {
            var childOpts = new DeviceFactoryOptions
            {
                Backend = options.Backend,
                ComPort = slot.ComPort ?? options.ComPort,
                BaudRate = options.BaudRate,
                Spider32DllPath = options.Spider32DllPath,
                ChannelCount = 8,
                DeviceCount = 1
            };
            children.Add(options.Backend == DeviceBackend.Simulator
                ? new SimulatedSpider8(8, 1)
                : CreateSingle(childOpts, slot, 8));
        }
        return new CascadedSpider8(children);
    }

    private static ISpider8Device CreateSingle(DeviceFactoryOptions options, DeviceSlot slot, int channels)
    {
        var portHint = slot.ComPort ?? options.ComPort;
        return options.Backend switch
        {
            DeviceBackend.Serial => new SerialProtocolAdapter(
                portHint ?? throw new ArgumentException($"COM port required for {slot.Name}."),
                options.BaudRate,
                channels),
            // USBHBM: Intfac32 first; DEST SoftSetup measure on OpenPort failure.
            DeviceBackend.Spider32Dll when HbmUsbDeviceScanner.IsHbmUsbTarget(portHint)
                => PreferIntfacUsb(portHint, channels),
            DeviceBackend.HbmUsb => PreferIntfacUsb(portHint, channels),
            DeviceBackend.Spider32Dll => new Spider32DllAdapter(
                options.Spider32DllPath,
                portHint,
                channels),
            _ => new SimulatedSpider8(channels, 1)
        };
    }

    /// <summary>Intfac32 first; DEST SoftSetup (half-bridge + MSV) if OpenPort fails.</summary>
    private static ISpider8Device PreferIntfacUsb(string? portHint, int channels)
        => new UsbSpider8HybridAdapter(portHint, channels);

    public static int CountEnabledDevices(IEnumerable<DeviceSlot> slots)
        => Math.Clamp(slots.Count(s => s.Enabled), 1, 8);
}
