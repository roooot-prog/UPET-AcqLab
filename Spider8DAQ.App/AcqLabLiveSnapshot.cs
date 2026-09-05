using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Spider8DAQ.App.ViewModels;
using Spider8DAQ.Core.Display;
using Spider8DAQ.Core.Journal;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App;

internal sealed class AcqLabLiveSnapshot : IApplicationKeyLiveSnapshot
{
    private static readonly TimeSpan PendingShotInterval = TimeSpan.FromMinutes(3);
    private DateTime _lastShotUtc = DateTime.MinValue;
    private byte[]? _lastFull;
    private byte[]? _lastThumb;

    public IReadOnlyList<ApplicationKeyChannelInfo> GetChannels()
    {
        return OnUi<IReadOnlyList<ApplicationKeyChannelInfo>>(() =>
        {
            if (Application.Current?.MainWindow is not MainWindow { DataContext: MainViewModel vm })
                return Array.Empty<ApplicationKeyChannelInfo>();

            var list = new List<ApplicationKeyChannelInfo>();
            foreach (var c in vm.Channels)
            {
                if (!c.Enabled) continue;
                var sensor = string.IsNullOrWhiteSpace(c.SensorName) || c.SensorName == LiveCitire.Placeholder
                    ? null
                    : c.SensorName;
                var reading = LiveCitire.IsPlaceholder(c.LiveReading) ? null : c.LiveReading;
                var value = TryParseReading(reading);
                if (reading is null && value is double parsed)
                    reading = parsed.ToString("G6", CultureInfo.InvariantCulture);
                var lo = FiniteAlarm(c.AlarmLow);
                var hi = FiniteAlarm(c.AlarmHigh);
                var alarm = "ok";
                if (c.IsOverflow) alarm = "sat";
                else if (c.IsLinkLost) alarm = "usb";
                else if (c.AlarmEnabled && value is double v)
                {
                    if (hi is double h && v > h) alarm = "hi";
                    else if (lo is double l && v < l) alarm = "lo";
                }
                else if (!c.AlarmEnabled)
                    alarm = "ok";
                list.Add(new ApplicationKeyChannelInfo
                {
                    Name = c.Name ?? "",
                    On = true,
                    Unit = string.IsNullOrWhiteSpace(c.Unit) ? null : c.Unit,
                    Sensor = sensor,
                    Reading = reading,
                    Value = value,
                    Lo = c.AlarmEnabled ? lo : null,
                    Hi = c.AlarmEnabled ? hi : null,
                    Alarm = alarm
                });
            }
            return list;
        }) ?? Array.Empty<ApplicationKeyChannelInfo>();
    }

    public ApplicationKeySessionInfo GetSession()
    {
        return OnUi(() =>
        {
            if (Application.Current?.MainWindow is not MainWindow { DataContext: MainViewModel vm })
                return new ApplicationKeySessionInfo();

            var alarms = 0;
            foreach (var c in vm.Channels)
            {
                if (!c.Enabled) continue;
                if (c.IsOverflow || c.IsLinkLost) { alarms++; continue; }
                if (!c.AlarmEnabled) continue;
                var value = TryParseReading(LiveCitire.IsPlaceholder(c.LiveReading) ? null : c.LiveReading);
                if (value is double v && c.IsInAlarm(v))
                    alarms++;
            }

            var recSec = 0;
            if (vm.IsRecording && vm.SampleRateHz > 0)
                recSec = Math.Max(0, vm.RecordingSampleCount / Math.Max(1, vm.SampleRateHz));

            var file = vm.LastRecordingPath;
            if (!string.IsNullOrWhiteSpace(file))
                file = Path.GetFileName(file);

            return new ApplicationKeySessionInfo
            {
                Rec = vm.IsRecording,
                RecSeconds = recSec,
                LastFile = string.IsNullOrWhiteSpace(file) ? null : file,
                AlarmCount = alarms,
                Conn = vm.IsConnected
            };
        }) ?? new ApplicationKeySessionInfo();
    }

    private static double? FiniteAlarm(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) >= 1e8)
            return null;
        return v;
    }

    private static double? TryParseReading(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        var end = 0;
        while (end < t.Length)
        {
            var ch = t[end];
            if (char.IsDigit(ch) || ch is '.' or ',' or '-' or '+' or 'e' or 'E')
            {
                end++;
                continue;
            }
            break;
        }
        var num = end > 0 ? t[..end] : t;
        num = num.Replace(',', '.');
        if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && !double.IsNaN(d) && !double.IsInfinity(d))
            return d;
        return null;
    }

    public IReadOnlyList<ApplicationKeyJournalLine> GetJournal(int maxLines = 30)
    {
        return OnUi<IReadOnlyList<ApplicationKeyJournalLine>>(() =>
        {
            if (Application.Current?.MainWindow is MainWindow { DataContext: MainViewModel vm })
                return Map(vm.JournalSnapshot(maxLines));
            return Array.Empty<ApplicationKeyJournalLine>();
        }) ?? Array.Empty<ApplicationKeyJournalLine>();
    }

    public (string? message, string level) GetLastError()
    {
        return OnUi<(string? message, string level)>(() =>
        {
            if (Application.Current?.MainWindow is not MainWindow { DataContext: MainViewModel vm })
                return (null, "Info");
            var e = vm.JournalLastError();
            if (e is null) return (null, "Info");
            var level = e.Level switch
            {
                LogLevel.Error => "Error",
                LogLevel.Warning => "Warn",
                _ => "Info"
            };
            return (e.Message, level);
        });
    }

    public (byte[]? jpeg, byte[]? thumb) TryCapture(int maxJpegBytes, int maxThumbBytes, bool force) =>
        Capture(force);

    private (byte[]? full, byte[]? thumb) Capture(bool force)
    {
        if (!force && DateTime.UtcNow - _lastShotUtc < PendingShotInterval)
            return (null, null);

        var shot = ApplicationKeyWindowCapture.Capture(
            ApplicationKeyLiveSnapshot.ScreenshotMaxBytes, 12_000);
        if (shot.full is null && shot.thumb is null)
            return (_lastFull, _lastThumb);

        _lastShotUtc = DateTime.UtcNow;
        if (shot.full is not null) _lastFull = shot.full;
        if (shot.thumb is not null) _lastThumb = shot.thumb;
        return shot;
    }

    private static IReadOnlyList<ApplicationKeyJournalLine> Map(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0) return Array.Empty<ApplicationKeyJournalLine>();
        return entries.Select(e => new ApplicationKeyJournalLine
        {
            Ts = e.Timestamp.ToString("O"),
            Level = e.Level == LogLevel.Error ? "Error"
                : e.Level == LogLevel.Warning ? "Warn"
                : "Info",
            Message = e.Message ?? ""
        }).ToList();
    }

    private static T OnUi<T>(Func<T> fn)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return fn();
            if (app.Dispatcher.CheckAccess())
                return fn();
            return app.Dispatcher.Invoke(fn, DispatcherPriority.Background);
        }
        catch
        {
            return default!;
        }
    }
}
