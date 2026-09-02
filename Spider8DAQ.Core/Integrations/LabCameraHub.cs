using System.Diagnostics;

namespace Spider8DAQ.Core.Integrations;

/// <summary>
/// Optional lab camera hub — file-based snapshots (nu clonă UI HBM tech-notes).
/// Live DirectShow/OpenCV e opțional; aici: folder, attach, Windows Camera, watch pe înregistrare.
/// </summary>
public sealed class LabCameraHub : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string? _sessionFolder;
    private readonly List<string> _sessionShots = new();

    public string SnapshotRoot { get; }
    public IReadOnlyList<string> SessionShots => _sessionShots;

    public event EventHandler<string>? SnapshotAdded;

    /// <param name="cameraRoot">
    /// Writable camera root (typically <see cref="AppPaths.Camera"/> under LocalAppData).
    /// </param>
    public LabCameraHub(string cameraRoot)
    {
        SnapshotRoot = cameraRoot;
        AppPaths.EnsureWritable(SnapshotRoot);
    }

    public string BeginSession(string? recordingStem = null)
    {
        var name = string.IsNullOrWhiteSpace(recordingStem)
            ? DateTime.Now.ToString("yyyyMMdd_HHmmss")
            : Path.GetFileNameWithoutExtension(recordingStem);
        _sessionFolder = Path.Combine(SnapshotRoot, name);
        AppPaths.EnsureWritable(_sessionFolder);
        _sessionShots.Clear();
        return _sessionFolder;
    }

    public void EndSession()
    {
        StopWatching();
        _sessionFolder = null;
    }

    public string EnsureSession()
    {
        if (_sessionFolder is null || !Directory.Exists(_sessionFolder))
            return BeginSession();
        return _sessionFolder;
    }

    /// <summary>Copies an image into the current session with an ISO timestamp prefix.</summary>
    public string AttachImage(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Imagine negăsită.", sourcePath);
        var destDir = EnsureSession();
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        var dest = Path.Combine(destDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
        File.Copy(sourcePath, dest, overwrite: true);
        Remember(dest);
        return dest;
    }

    public IReadOnlyList<string> ListRecent(int max = 40)
    {
        if (!Directory.Exists(SnapshotRoot)) return Array.Empty<string>();
        return Directory.GetFiles(SnapshotRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsImage)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(max)
            .ToList();
    }

    public void StartWatching(string? watchFolder = null)
    {
        StopWatching();
        var folder = string.IsNullOrWhiteSpace(watchFolder) ? EnsureSession() : watchFolder;
        AppPaths.EnsureWritable(folder);
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnCreated;
        _watcher.Changed += OnCreated;
    }

    public void StopWatching()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreated;
        _watcher.Changed -= OnCreated;
        _watcher.Dispose();
        _watcher = null;
    }

    public static bool TryOpenWindowsCamera()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "microsoft.windows.camera:",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenFolder(string folder)
    {
        AppPaths.EnsureWritable(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!IsImage(e.FullPath)) return;
            // Brief settle for writers that lock the file.
            Thread.Sleep(80);
            if (!File.Exists(e.FullPath)) return;
            var destDir = EnsureSession();
            if (string.Equals(Path.GetDirectoryName(e.FullPath), destDir, StringComparison.OrdinalIgnoreCase))
            {
                Remember(e.FullPath);
                return;
            }
            var dest = Path.Combine(destDir, Path.GetFileName(e.FullPath));
            if (!File.Exists(dest))
                File.Copy(e.FullPath, dest, overwrite: false);
            Remember(dest);
        }
        catch
        {
            /* ignore transient IO */
        }
    }

    private void Remember(string path)
    {
        lock (_sessionShots)
        {
            if (!_sessionShots.Contains(path, StringComparer.OrdinalIgnoreCase))
                _sessionShots.Add(path);
        }
        SnapshotAdded?.Invoke(this, path);
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".webp";
    }

    public void Dispose() => StopWatching();
}
