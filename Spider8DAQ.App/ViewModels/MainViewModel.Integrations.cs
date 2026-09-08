using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using Spider8DAQ.App.Integrations;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Integrations;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private LabCameraHub? _camera;
    private LabUsbCameraService? _usbCamera;
    private ThirdPartyExportSettings _integrations = new();
    private string _cameraStatus = "Cameră: opțional — atașați imagini sau urmăriți un folder.";
    private string _integrationStatus = "Integrări 3rd-party: dezactivate.";
    private bool _cameraWatchDuringRecord = true;
    private string _cameraWatchFolder = "";
    private bool _experimentVideoEnabled;
    private string _experimentCameraId = "";
    private string _experimentCameraName = "";
    private string _experimentVideoPath = "";
    private readonly List<string> _experimentVideoFiles = new();

    public ObservableCollection<string> CameraShots { get; } = new();

    public ICommand CameraAttachCommand { get; private set; } = null!;
    public ICommand CameraOpenWindowsCommand { get; private set; } = null!;
    public ICommand CameraOpenFolderCommand { get; private set; } = null!;
    public ICommand CameraRefreshCommand { get; private set; } = null!;
    public ICommand CameraStartWatchCommand { get; private set; } = null!;
    public ICommand CameraStopWatchCommand { get; private set; } = null!;
    public ICommand SaveIntegrationsCommand { get; private set; } = null!;
    public ICommand TestIntegrationsCommand { get; private set; } = null!;

    public string CameraStatus { get => _cameraStatus; set { _cameraStatus = value; OnPropertyChanged(); } }
    public string IntegrationStatus { get => _integrationStatus; set { _integrationStatus = value; OnPropertyChanged(); } }

    public bool ExperimentVideoEnabled
    {
        get => _experimentVideoEnabled;
        set
        {
            _experimentVideoEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExperimentBannerText));
        }
    }

    public string ExperimentCameraId
    {
        get => _experimentCameraId;
        set { _experimentCameraId = value ?? ""; OnPropertyChanged(); }
    }

    public string ExperimentCameraName
    {
        get => _experimentCameraName;
        set
        {
            _experimentCameraName = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExperimentBannerText));
        }
    }

    public string ExperimentVideoPath
    {
        get => _experimentVideoPath;
        set { _experimentVideoPath = value ?? ""; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> ExperimentVideoFiles => _experimentVideoFiles;

    public bool CameraWatchDuringRecord
    {
        get => _cameraWatchDuringRecord;
        set { _cameraWatchDuringRecord = value; OnPropertyChanged(); }
    }

    public string CameraWatchFolder
    {
        get => _cameraWatchFolder;
        set { _cameraWatchFolder = value; OnPropertyChanged(); }
    }

    public bool IntegrationEnabled
    {
        get => _integrations.Enabled;
        set { _integrations.Enabled = value; OnPropertyChanged(); }
    }

    public string IntegrationOutboundFolder
    {
        get => _integrations.OutboundFolder;
        set { _integrations.OutboundFolder = value; OnPropertyChanged(); }
    }

    public string IntegrationWebhookUrl
    {
        get => _integrations.WebhookUrl;
        set { _integrations.WebhookUrl = value; OnPropertyChanged(); }
    }

    public bool IntegrationCopyCsv
    {
        get => _integrations.CopyCsv;
        set { _integrations.CopyCsv = value; OnPropertyChanged(); }
    }

    public bool IntegrationPostMetadata
    {
        get => _integrations.PostMetadata;
        set { _integrations.PostMetadata = value; OnPropertyChanged(); }
    }

    private void WireIntegrationsCommands()
    {
        _camera = new LabCameraHub(AppPaths.Camera);
        _camera.SnapshotAdded += (_, path) => _dispatcher.Invoke(() =>
        {
            if (!CameraShots.Contains(path))
                CameraShots.Insert(0, path);
            while (CameraShots.Count > 80) CameraShots.RemoveAt(CameraShots.Count - 1);
            CameraStatus = $"Snapshot: {Path.GetFileName(path)}";
        });

        try
        {
            _usbCamera = new LabUsbCameraService(_dispatcher);
            _usbCamera.DevicesChanged += (_, _) => _dispatcher.BeginInvoke(() =>
            {
                if (!IsRecording)
                    RefreshExperimentCameraStatus();
            });
            _usbCamera.StatusChanged += (_, text) => _dispatcher.BeginInvoke(() =>
            {
                CameraStatus = text;
                Status = text;
            });
        }
        catch (Exception ex)
        {
            _usbCamera = null;
            CameraStatus = "Cameră USB indisponibilă: " + ex.Message;
        }

        _integrations = ThirdPartyExporter.Load();
        OnPropertyChanged(nameof(IntegrationEnabled));
        OnPropertyChanged(nameof(IntegrationOutboundFolder));
        OnPropertyChanged(nameof(IntegrationWebhookUrl));
        OnPropertyChanged(nameof(IntegrationCopyCsv));
        OnPropertyChanged(nameof(IntegrationPostMetadata));
        IntegrationStatus = _integrations.Enabled
            ? "Integrări 3rd-party: active."
            : "Integrări 3rd-party: dezactivate.";

        CameraAttachCommand = new RelayCommand(AttachCameraImage);
        CameraOpenWindowsCommand = new RelayCommand(() =>
        {
            CameraStatus = LabCameraHub.TryOpenWindowsCamera()
                ? "Camera Windows deschisă — salvați poza, apoi Atașează imagine."
                : "Nu s-a putut deschide Camera Windows.";
            Status = CameraStatus;
        });
        CameraOpenFolderCommand = new RelayCommand(() =>
        {
            var folder = _camera?.EnsureSession() ?? AppPaths.EnsureWritable(AppPaths.Camera);
            LabCameraHub.OpenFolder(folder);
            CameraStatus = $"Folder cameră: {folder}";
        });
        CameraRefreshCommand = new RelayCommand(RefreshCameraShots);
        CameraStartWatchCommand = new RelayCommand(() =>
        {
            _camera?.StartWatching(string.IsNullOrWhiteSpace(CameraWatchFolder) ? null : CameraWatchFolder);
            CameraStatus = "Watch folder activ (imagini noi → sesiune).";
            Status = CameraStatus;
        });
        CameraStopWatchCommand = new RelayCommand(() =>
        {
            _camera?.StopWatching();
            CameraStatus = "Watch folder oprit.";
        });
        SaveIntegrationsCommand = new RelayCommand(() =>
        {
            ThirdPartyExporter.Save(_integrations);
            IntegrationStatus = "Setări integrări salvate.";
            Status = IntegrationStatus;
            _journal.Setup(IntegrationStatus);
        });
        TestIntegrationsCommand = new RelayCommand(async () => await RunThirdPartyExportAsync(force: true));

        RefreshCameraShots();
        RefreshExperimentCameraStatus();
    }

    private void RefreshExperimentCameraStatus()
    {
        if (_usbCamera is null)
        {
            if (!ExperimentVideoEnabled) return;
            CameraStatus = "Film experiment: serviciu cameră indisponibil.";
            return;
        }

        CameraStatus = ExperimentVideoEnabled
            ? "Film experiment: " + _usbCamera.StatusSummary(ExperimentCameraId)
            : _usbCamera.StatusSummary();
        OnPropertyChanged(nameof(ExperimentBannerText));
    }

    private void RememberExperimentVideo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        ExperimentVideoPath = path;
        if (!_experimentVideoFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            _experimentVideoFiles.Add(path);
    }

    private void ClearExperimentVideos()
    {
        _experimentVideoFiles.Clear();
        ExperimentVideoPath = "";
    }

    private void ApplyExperimentVideoFromMeta(Spider8DAQ.Core.Projects.ProjectMeta? meta)
    {
        ExperimentVideoEnabled = meta?.ExperimentVideoEnabled == true;
        ExperimentCameraId = meta?.ExperimentCameraId ?? "";
        ExperimentCameraName = meta?.ExperimentCameraName ?? "";
        ExperimentVideoPath = meta?.ExperimentVideoPath ?? "";
        _experimentVideoFiles.Clear();
        if (meta?.ExperimentVideoFiles is { Count: > 0 })
        {
            foreach (var p in meta.ExperimentVideoFiles)
            {
                if (!string.IsNullOrWhiteSpace(p) && !_experimentVideoFiles.Contains(p, StringComparer.OrdinalIgnoreCase))
                    _experimentVideoFiles.Add(p);
            }
        }
        else if (!string.IsNullOrWhiteSpace(ExperimentVideoPath))
            _experimentVideoFiles.Add(ExperimentVideoPath);
    }

    private void WriteExperimentVideoToMeta(Spider8DAQ.Core.Projects.ProjectMeta meta)
    {
        meta.ExperimentVideoEnabled = ExperimentVideoEnabled;
        meta.ExperimentCameraId = ExperimentCameraId ?? "";
        meta.ExperimentCameraName = ExperimentCameraName ?? "";
        meta.ExperimentVideoPath = ExperimentVideoPath ?? "";
        meta.ExperimentVideoFiles = _experimentVideoFiles.ToList();
    }

    private async Task StartExperimentVideoForRecordingAsync(string csvPath)
    {
        if (!ExperimentVideoEnabled || _usbCamera is null)
            return;

        try
        {
            var stamp = IsExperimentActive && ExperimentStartedAt is { } expStamp
                ? expStamp
                : DateTime.Now;
            var dir = Path.GetDirectoryName(csvPath) ?? GetWritableRecordingsDirectory();
            var name = ExperimentFileNaming.BuildFileName(SampleId, stamp, ExperimentFileNaming.RoleVideo, ".mp4");
            var dest = ExperimentFileNaming.UniquePath(Path.Combine(dir, name));
            var result = await _usbCamera.StartRecordingAsync(ExperimentCameraId, dest).ConfigureAwait(true);
            if (result.Ok)
            {
                RememberExperimentVideo(result.Path);
                CameraStatus = result.Message;
                _journal.Info("Film experiment: " + result.Path);
            }
            else
            {
                CameraStatus = result.Message;
                _journal.Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            CameraStatus = "Filmare epruvetă eșuată: " + ex.Message;
            _journal.Warn(CameraStatus);
        }
    }

    private async Task StopExperimentVideoForRecordingAsync()
    {
        if (_usbCamera is null || !_usbCamera.IsRecordingVideo)
            return;
        try
        {
            var result = await _usbCamera.StopRecordingAsync().ConfigureAwait(true);
            if (result.Ok)
                RememberExperimentVideo(result.Path);
            else if (!string.IsNullOrWhiteSpace(result.Message))
                CameraStatus = result.Message;
        }
        catch (Exception ex)
        {
            CameraStatus = "Oprire film eșuată: " + ex.Message;
            _journal.Warn(CameraStatus);
        }
    }

    private void RefreshCameraShots()
    {
        CameraShots.Clear();
        if (_camera is null) return;
        foreach (var p in _camera.ListRecent())
            CameraShots.Add(p);
        CameraStatus = $"Cameră: {CameraShots.Count} imagini recente în {_camera.SnapshotRoot}";
    }

    private void AttachCameraImage()
    {
        if (_camera is null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Imagini|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dest = _camera.AttachImage(dlg.FileName);
            if (!CameraShots.Contains(dest))
                CameraShots.Insert(0, dest);
            CameraStatus = $"Atașat: {Path.GetFileName(dest)}";
            Status = CameraStatus;
            _journal.Info(CameraStatus);
        }
        catch (Exception ex)
        {
            CameraStatus = "Atașare eșuată: " + AppPaths.FriendlyIoMessage(ex, AppPaths.Camera);
            Status = CameraStatus;
        }
    }

    private void OnRecordingStartedForIntegrations(string csvPath)
    {
        if (_camera is null) return;
        try
        {
            var folder = _camera.BeginSession(csvPath);
            if (CameraWatchDuringRecord)
            {
                var watch = string.IsNullOrWhiteSpace(CameraWatchFolder) ? folder : CameraWatchFolder;
                _camera.StartWatching(watch);
                CameraStatus = $"Sesiune cameră: {folder}";
            }
        }
        catch (Exception ex)
        {
            CameraStatus = AppPaths.FriendlyIoMessage(ex, AppPaths.Camera);
            Status = CameraStatus;
            _journal.Error(CameraStatus);
        }
    }

    private async Task OnRecordingStoppedForIntegrationsAsync(string? csvPath)
    {
        await StopExperimentVideoForRecordingAsync();
        _camera?.StopWatching();
        var shots = _camera?.SessionShots.Count ?? 0;
        if (shots > 0)
            CameraStatus = $"Sesiune cameră închisă — {shots} snapshot(uri).";
        _camera?.EndSession();

        if (!string.IsNullOrWhiteSpace(csvPath))
            await RunThirdPartyExportAsync(force: false, csvPath);
    }

    private async Task RunThirdPartyExportAsync(bool force, string? csvPath = null)
    {
        var path = csvPath ?? LastRecordingPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            IntegrationStatus = "Nicio înregistrare pentru export 3rd-party.";
            if (force) Status = IntegrationStatus;
            return;
        }

        if (!force && !_integrations.Enabled)
            return;

        var settings = force
            ? new ThirdPartyExportSettings
            {
                Enabled = true,
                OutboundFolder = _integrations.OutboundFolder,
                WebhookUrl = _integrations.WebhookUrl,
                CopyCsv = _integrations.CopyCsv,
                PostMetadata = _integrations.PostMetadata
            }
            : _integrations;

        try
        {
            var meta = new
            {
                product = "UPET AcqLab",
                project = ProjectName,
                sampleId = SampleId,
                @operator = OperatorName,
                comment = Comment,
                backend = SelectedBackend,
                file = path,
                fileName = Path.GetFileName(path),
                tags = MeasurementTags,
                cameraShots = _camera?.SessionShots.ToArray() ?? Array.Empty<string>(),
                exportedUtc = DateTime.UtcNow
            };
            var result = await ThirdPartyExporter.ExportAsync(settings, path, meta);
            IntegrationStatus = result.Message;
            Status = "3rd-party: " + result.Message;
            _journal.Info(Status);
        }
        catch (Exception ex)
        {
            IntegrationStatus = "Export 3rd-party eșuat: " + ex.Message;
            Status = IntegrationStatus;
            _journal.Error(Status);
        }
    }
}
