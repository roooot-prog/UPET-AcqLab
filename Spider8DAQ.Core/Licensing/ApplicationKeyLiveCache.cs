using System.Globalization;
using Spider8DAQ.Core.Display;

namespace Spider8DAQ.Core.Licensing;

/// <summary>
/// Latest DAQ sample for the license live POST. Physical values update on the
/// acquisition thread; layout (names / alarms) comes from the UI tick.
/// </summary>
public static class ApplicationKeyLiveCache
{
    private static readonly double[] PhysicalA = new double[64];
    private static readonly double[] PhysicalB = new double[64];
    private static int _page;
    private static int _physicalLen;
    private static Layout[] _layout = Array.Empty<Layout>();
    private static volatile bool _rec;
    private static volatile bool _conn;
    private static int _recSeconds;
    private static string? _lastFile;
    private static long _utcTicks;

    public readonly struct Layout
    {
        public Layout()
        {
        }

        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string? Unit { get; init; }
        public string? Sensor { get; init; }
        public bool On { get; init; }
        public bool HasSetup { get; init; }
        public bool Overflow { get; init; }
        public bool LinkLost { get; init; }
        public bool AlarmEnabled { get; init; }
        public double? Lo { get; init; }
        public double? Hi { get; init; }
    }

    public static bool HasLayout => Volatile.Read(ref _layout).Length > 0;

    public static void PublishLayout(Layout[] layout)
    {
        Volatile.Write(ref _layout, layout ?? Array.Empty<Layout>());
        Volatile.Write(ref _utcTicks, DateTime.UtcNow.Ticks);
    }

    public static void PublishSession(bool rec, bool conn, int recSeconds, string? lastFile)
    {
        _rec = rec;
        _conn = conn;
        Volatile.Write(ref _recSeconds, recSeconds < 0 ? 0 : recSeconds);
        Volatile.Write(ref _lastFile, lastFile);
        Volatile.Write(ref _utcTicks, DateTime.UtcNow.Ticks);
    }

    public static void PublishPhysical(ReadOnlySpan<double> physical)
    {
        var page = Volatile.Read(ref _page) ^ 1;
        var buf = page == 0 ? PhysicalA : PhysicalB;
        var n = Math.Min(physical.Length, buf.Length);
        physical[..n].CopyTo(buf.AsSpan(0, n));
        Volatile.Write(ref _physicalLen, n);
        Volatile.Write(ref _page, page);
        Volatile.Write(ref _utcTicks, DateTime.UtcNow.Ticks);
    }

    public static bool TryGet(out ApplicationKeyChannelInfo[] channels, out ApplicationKeySessionInfo session)
    {
        session = new ApplicationKeySessionInfo
        {
            Rec = _rec,
            Conn = _conn,
            RecSeconds = Volatile.Read(ref _recSeconds),
            LastFile = Volatile.Read(ref _lastFile)
        };
        var layout = Volatile.Read(ref _layout);
        if (layout.Length == 0)
        {
            channels = Array.Empty<ApplicationKeyChannelInfo>();
            return false;
        }

        var page = Volatile.Read(ref _page);
        var buf = page == 0 ? PhysicalA : PhysicalB;
        var len = Volatile.Read(ref _physicalLen);
        var list = new List<ApplicationKeyChannelInfo>(Math.Min(32, layout.Length));
        var alarms = 0;
        foreach (var row in layout)
        {
            if (!row.On) continue;
            var inScan = row.Index >= 0 && row.Index < len;
            var v = inScan ? buf[row.Index] : double.NaN;
            var reading = LiveCitire.FormatOrPlaceholder(
                v, row.Unit, row.On, row.Overflow, row.LinkLost, inScan, row.HasSetup,
                CultureInfo.InvariantCulture);
            double? value = null;
            if (!LiveCitire.IsPlaceholder(reading) && double.IsFinite(v))
                value = v;
            var alarm = "ok";
            if (row.Overflow) alarm = "sat";
            else if (row.LinkLost) alarm = "usb";
            else if (row.AlarmEnabled && value is double dv)
            {
                if (row.Hi is double h && dv > h) alarm = "hi";
                else if (row.Lo is double l && dv < l) alarm = "lo";
            }
            if (alarm is not "ok") alarms++;
            list.Add(new ApplicationKeyChannelInfo
            {
                Name = row.Name ?? "",
                On = true,
                Unit = string.IsNullOrWhiteSpace(row.Unit) ? null : row.Unit,
                Sensor = row.Sensor,
                Reading = LiveCitire.IsPlaceholder(reading) ? null : reading,
                Value = value,
                Lo = row.AlarmEnabled ? row.Lo : null,
                Hi = row.AlarmEnabled ? row.Hi : null,
                Alarm = alarm
            });
            if (list.Count >= 32) break;
        }

        session.AlarmCount = alarms;
        channels = list.ToArray();
        return true;
    }
}
