using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Panel = Windows.Devices.Enumeration.Panel;

namespace Spider8DAQ.App.Integrations;

/// <summary>
/// USB / webcam video for the experiment (separate from montage still photos).
/// Enumerates cameras, watches plug/unplug, records MP4 for the Rec window.
/// </summary>
public sealed class LabVideoDeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool LooksExternalUsb { get; init; }

    public string Display => LooksExternalUsb ? Name + " (USB)" : Name;

    public override string ToString() => Display;
}

public sealed class LabUsbCameraService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private DeviceWatcher? _watcher;
    private MediaCapture? _capture;
    private bool _recording;
    private string? _outputPath;
    private bool _disposed;

    public ObservableCollection<LabVideoDeviceInfo> Devices { get; } = new();

    public event EventHandler? DevicesChanged;
    public event EventHandler<string>? StatusChanged;

    public bool IsRecordingVideo
    {
        get { lock (_gate) return _recording; }
    }

    public string? CurrentVideoPath
    {
        get { lock (_gate) return _outputPath; }
    }

    public LabUsbCameraService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        StartWatcher();
        _ = RefreshAsync();
    }

    public void StartWatcher()
    {
        if (_watcher is not null) return;
        try
        {
            _watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
            _watcher.Added += OnWatcherAdded;
            _watcher.Removed += OnWatcherRemoved;
            _watcher.Updated += OnWatcherUpdated;
            _watcher.EnumerationCompleted += OnWatcherEnumCompleted;
            _watcher.Start();
        }
        catch
        {
            _watcher = null;
        }
    }

    public async Task RefreshAsync()
    {
        IReadOnlyList<LabVideoDeviceInfo> next;
        try
        {
            var found = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            next = found.Select(ToInfo).ToList();
        }
        catch
        {
            next = Array.Empty<LabVideoDeviceInfo>();
        }

        await UiAsync(() =>
        {
            Devices.Clear();
            foreach (var d in next)
                Devices.Add(d);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public LabVideoDeviceInfo? AutoSelect()
    {
        if (Devices.Count == 0) return null;
        return Devices.FirstOrDefault(d => d.LooksExternalUsb) ?? Devices[0];
    }

    public bool IsConnected(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Devices.Count > 0;
        return Devices.Any(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    public string StatusSummary(string? preferredId = null)
    {
        if (Devices.Count == 0)
            return "Nicio cameră video detectată — conectați camera USB.";
        var pick = !string.IsNullOrWhiteSpace(preferredId)
            ? Devices.FirstOrDefault(d => string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            : AutoSelect();
        pick ??= AutoSelect();
        if (pick is null)
            return $"{Devices.Count} cameră(e) video — selectați una.";
        var kind = pick.LooksExternalUsb ? "USB" : "cameră";
        return $"{kind} detectată: {pick.Name}";
    }

    public async Task<(bool Ok, string Message, string? Path)> StartRecordingAsync(
        string? deviceId,
        string outputPath)
    {
        if (_disposed) return (false, "Serviciu cameră oprit.", null);
        if (string.IsNullOrWhiteSpace(outputPath))
            return (false, "Cale clip lipsă.", null);

        return await OnUi(async () =>
        {
            lock (_gate)
            {
                if (_recording)
                    return (false, "Filmare deja pornită.", _outputPath);
            }

            await StopCaptureUnlockedAsync().ConfigureAwait(true);

            var id = deviceId;
            if (string.IsNullOrWhiteSpace(id) || !IsConnected(id))
                id = AutoSelect()?.Id;
            if (string.IsNullOrWhiteSpace(id))
                return (false, "Nicio cameră conectată — Rec CSV continuă fără film.", null);

            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                var name = Path.GetFileName(outputPath);
                if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name))
                    return (false, "Cale clip invalidă.", null);
                Directory.CreateDirectory(dir);

                var capture = new MediaCapture();
                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = id,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };
                await capture.InitializeAsync(settings);
                capture.Failed += OnCaptureFailed;

                var profile = CreateMp4Profile();
                var folder = await StorageFolder.GetFolderFromPathAsync(dir);
                var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await capture.StartRecordToStorageFileAsync(profile, file);

                lock (_gate)
                {
                    _capture = capture;
                    _recording = true;
                    _outputPath = file.Path;
                }

                RaiseStatus("Filmare epruvetă pornită: " + Path.GetFileName(file.Path));
                return (true, "Filmare epruvetă pornită.", file.Path);
            }
            catch (UnauthorizedAccessException)
            {
                await StopCaptureUnlockedAsync().ConfigureAwait(true);
                return (false,
                    "Acces cameră refuzat. Activați Camera pentru aplicații desktop în Setări Windows → Confidențialitate.",
                    null);
            }
            catch (Exception ex)
            {
                await StopCaptureUnlockedAsync().ConfigureAwait(true);
                return (false, "Filmare eșuată: " + Truncate(ex.Message, 160), null);
            }
        }).ConfigureAwait(false);
    }

    public async Task<(bool Ok, string Message, string? Path)> StopRecordingAsync()
    {
        return await OnUi(async () =>
        {
            string? path;
            lock (_gate) path = _outputPath;
            var was = false;
            lock (_gate) was = _recording;
            await StopCaptureUnlockedAsync().ConfigureAwait(true);
            if (!was)
                return (true, "Filmare deja oprită.", path);
            var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            RaiseStatus(exists
                ? "Filmare epruvetă oprită: " + Path.GetFileName(path)
                : "Filmare oprită — clip indisponibil.");
            return (exists, exists ? "Clip salvat." : "Filmare oprită fără fișier.", path);
        }).ConfigureAwait(false);
    }

    private async Task StopCaptureUnlockedAsync()
    {
        MediaCapture? capture;
        var recording = false;
        lock (_gate)
        {
            capture = _capture;
            recording = _recording;
            _recording = false;
            _capture = null;
        }

        if (capture is null) return;
        try
        {
            if (recording)
                await capture.StopRecordAsync();
        }
        catch { /* already stopped / failed */ }

        try { capture.Failed -= OnCaptureFailed; } catch { /* ignore */ }
        try { capture.Dispose(); } catch { /* ignore */ }
    }

    private void OnCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        _ = OnUi(async () =>
        {
            RaiseStatus("Cameră deconectată sau eroare: " + Truncate(args.Message, 140));
            await StopCaptureUnlockedAsync().ConfigureAwait(true);
            return 0;
        });
    }

    private static MediaEncodingProfile CreateMp4Profile()
    {
        foreach (var q in new[]
                 {
                     VideoEncodingQuality.HD720p,
                     VideoEncodingQuality.Auto,
                     VideoEncodingQuality.Vga
                 })
        {
            try { return MediaEncodingProfile.CreateMp4(q); }
            catch { /* try next */ }
        }

        return MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
    }

    public static bool LooksExternalUsb(DeviceInformation d)
    {
        var id = d.Id ?? "";
        var name = (d.Name ?? "").Trim();
        try
        {
            var loc = d.EnclosureLocation;
            if (loc is null)
            {
                if (id.Contains("USB#", StringComparison.OrdinalIgnoreCase)
                    || id.Contains(@"USB\", StringComparison.OrdinalIgnoreCase)
                    || NameLooksUsb(name))
                    return true;
            }
            else if (loc.Panel is Panel.Unknown or Panel.Back or Panel.Bottom or Panel.Left or Panel.Right)
                return true;
            else if (loc.Panel is Panel.Front or Panel.Top)
                return NameLooksUsb(name);
        }
        catch
        {
            /* fall through */
        }

        return NameLooksUsb(name);
    }

    private static bool NameLooksUsb(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("usb")
               || n.Contains("logitech")
               || n.Contains("webcam")
               || n.Contains("elgato")
               || n.Contains("c920")
               || n.Contains("c270")
               || n.Contains("c922")
               || n.Contains("brio");
    }

    private static LabVideoDeviceInfo ToInfo(DeviceInformation d) =>
        new()
        {
            Id = d.Id ?? "",
            Name = string.IsNullOrWhiteSpace(d.Name) ? "Cameră video" : d.Name.Trim(),
            LooksExternalUsb = LooksExternalUsb(d)
        };

    private void OnWatcherAdded(DeviceWatcher sender, DeviceInformation args) => QueueRefresh();
    private void OnWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate args) => QueueRefresh();
    private void OnWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate args) => QueueRefresh();
    private void OnWatcherEnumCompleted(DeviceWatcher sender, object args) => QueueRefresh();

    private void QueueRefresh()
    {
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(250).ConfigureAwait(false); } catch { /* ignore */ }
            try { await RefreshAsync().ConfigureAwait(false); } catch { /* ignore */ }
        });
    }

    private void RaiseStatus(string text) =>
        _ = UiAsync(() => StatusChanged?.Invoke(this, text));

    private Task UiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> OnUi<T>(Func<Task<T>> work)
    {
        if (_dispatcher.CheckAccess())
            return work();
        return _dispatcher.InvokeAsync(work).Task.Unwrap();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_watcher is not null)
            {
                _watcher.Added -= OnWatcherAdded;
                _watcher.Removed -= OnWatcherRemoved;
                _watcher.Updated -= OnWatcherUpdated;
                _watcher.EnumerationCompleted -= OnWatcherEnumCompleted;
                try { _watcher.Stop(); } catch { /* ignore */ }
                _watcher = null;
            }
        }
        catch { /* ignore */ }

        _ = Task.Run(async () =>
        {
            try { await StopRecordingAsync().ConfigureAwait(false); }
            catch { /* ignore */ }
        });
    }
}
