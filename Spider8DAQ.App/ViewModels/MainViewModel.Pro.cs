using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Storage;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private MeasurementDatabase? _db;
    private DeviceWatchdog? _watchdog;
    private bool _watchdogEnabled = true;
    private string _dbSearch = "";
    private string _healthText = "Health: —";
    private string _measurementTags = "";

    public ObservableCollection<MeasurementRow> DbResults { get; } = new();
    public ObservableCollection<string> HealthChecks { get; } = new();

    public ICommand SelfTestCommand { get; private set; } = null!;
    public ICommand SearchDbCommand { get; private set; } = null!;
    public ICommand OpenDbFileCommand { get; private set; } = null!;
    public ICommand ToggleWatchdogCommand { get; private set; } = null!;

    public bool WatchdogEnabled
    {
        get => _watchdogEnabled;
        set
        {
            _watchdogEnabled = value;
            if (_watchdog is not null) _watchdog.Enabled = value;
            OnPropertyChanged();
        }
    }

    public string DbSearch { get => _dbSearch; set { _dbSearch = value; OnPropertyChanged(); } }
    public string HealthText { get => _healthText; set { _healthText = value; OnPropertyChanged(); } }
    public string MeasurementTags { get => _measurementTags; set { _measurementTags = value; OnPropertyChanged(); } }

    private async Task InitProServicesAsync()
    {
        try
        {
            var dbPath = AppPaths.MeasurementsDb;
            AppPaths.EnsureWritable(AppPaths.Data);
            _db = new MeasurementDatabase(dbPath);
            await _db.InitializeAsync();
            await SearchDbAsync();
            _journal.Info($"DB măsurători: {dbPath}");
        }
        catch (Exception ex)
        {
            _journal.Error($"DB init failed: {ex.Message}");
            await UiAsync(() => HealthText = $"DB error: {ex.Message}");
        }
    }

    private void WireProCommands()
    {
        SelfTestCommand = new RelayCommand(async () => await RunSelfTestAsync());
        SearchDbCommand = new RelayCommand(async () => await SearchDbAsync());
        OpenDbFileCommand = new RelayCommand(OpenSelectedDbFile);
        ToggleWatchdogCommand = new RelayCommand(() =>
        {
            WatchdogEnabled = !WatchdogEnabled;
            Status = WatchdogEnabled
                ? "Watchdog activ (monitorizare eșantioane / reconectare)."
                : "Watchdog dezactivat.";
        });
        _ = InitProServicesAsync();
    }

    private void StartWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = new DeviceWatchdog(
            TryWatchdogReconnectAsync,
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(1),
            isDevicePresent: IsUsbDestPresentForWatchdog,
            monitorSamples: () => IsStreaming);
        _watchdog.Enabled = WatchdogEnabled;
        _watchdog.StatusChanged += (_, msg) => _dispatcher.Invoke(() =>
        {
            var ro = msg
                .Replace("Watchdog: no samples for", "Watchdog: fără eșantioane de", StringComparison.OrdinalIgnoreCase)
                .Replace("reconnect attempt", "încercare reconectare", StringComparison.OrdinalIgnoreCase)
                .Replace("reconnect finished", "reconectare finalizată", StringComparison.OrdinalIgnoreCase)
                .Replace("max reconnects reached", "număr maxim de reconectări atins", StringComparison.OrdinalIgnoreCase)
                .Replace("Watchdog error:", "Eroare watchdog:", StringComparison.OrdinalIgnoreCase)
                .Replace("DEST USB absent", "DEST USB absent", StringComparison.OrdinalIgnoreCase)
                .Replace("comunicare pierdută", "comunicare pierdută", StringComparison.OrdinalIgnoreCase);
            Status = ro;
            HealthText = ro;
            _journal.Warn(ro);
        });
        _watchdog.ConnectionLost += OnDeviceConnectionLost;
        _watchdog.Start();
    }

    /// <summary>SetupAPI DIGCF_PRESENT DEST paths — not registry Enum\USB (stale after unplug).</summary>
    private bool IsUsbDestPresentForWatchdog()
    {
        if (SelectedBackend == "Simulator" || _device is SimulatedSpider8)
            return true;
        var usbTarget = SelectedBackend is "HBM USB" or "Spider32.dll"
            || _device is HbmUsbSpider8Adapter or UsbSpider8HybridAdapter or Spider32DllAdapter or IntfacSpider8Adapter;
        if (!usbTarget)
            return true;
        return HbmUsbDeviceScanner.IsDestInterfacePresent();
    }

    private async Task<bool> TryWatchdogReconnectAsync()
    {
        var wasStreaming = IsStreaming;
        if (_device is null)
        {
            await ConnectAsync();
            if (!IsConnected) return false;
            if (wasStreaming) await StartAsync();
            return IsConnected && (!wasStreaming || IsStreaming);
        }

        var usbTarget = SelectedBackend is "HBM USB" or "Spider32.dll"
            || _device is HbmUsbSpider8Adapter or UsbSpider8HybridAdapter or Spider32DllAdapter or IntfacSpider8Adapter;
        if (usbTarget)
        {
            if (IsCatmanEasyRunning())
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    Status = "Watchdog: catman Easy rulează — reconectare USB amânată (închideți catman).";
                    HealthText = Status;
                    _journal.Warn(Status);
                });
                return false;
            }

            if (!HbmUsbDeviceScanner.IsDestInterfacePresent())
                return false;
        }

        await Task.Delay(400);
        try { await _device.StopStreamingAsync(); } catch { /* ignore */ }
        try { await _device.DisconnectAsync(); } catch { /* ignore */ }
        await Task.Delay(500);
        await _device.ConnectAsync();
        ChannelConfig[] configs = Array.Empty<ChannelConfig>();
        await _dispatcher.InvokeAsync(() =>
        {
            configs = Channels.Select(ToConfig).ToArray();
        });
        await _device.ApplyChannelConfigAsync(configs);
        if (wasStreaming)
            await _device.StartStreamingAsync();
        await _dispatcher.InvokeAsync(() =>
        {
            _communicationLost = false;
            SetChannelsLinkLost(false);
            IsStreaming = wasStreaming;
            IsConnected = true;
            Status = wasStreaming
                ? "Watchdog: reconectat + streaming reluat."
                : "Watchdog: reconectat.";
        });
        return true;
    }

    private void StopWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = null;
    }

    private void NotifyWatchdogSample() => _watchdog?.NotifySample();

    private async Task RunSelfTestAsync()
    {
        if (_device is null)
        {
            // Offline diagnostics still useful before Connect.
            HealthChecks.Clear();
            HealthChecks.Add("Dispozitiv: neconectat");
            HealthChecks.Add(HbmUsbDeviceScanner.IsUsbHbmPresent()
                ? "PnP USBHBM: prezent"
                : "PnP USBHBM: absent");
            HealthChecks.Add(IsCatmanEasyRunning()
                ? "catman Easy: RULEAZĂ"
                : "catman Easy: oprit");
            try
            {
                HealthChecks.Add(Intfac32Native.IsAvailable()
                    ? $"Intfac32: OK · handles={Intfac32Native.GetNumOpenHandles()}"
                    : "Intfac32: lipsă");
            }
            catch (Exception ex) { HealthChecks.Add("Intfac: " + ex.Message); }
            HealthText = "Offline diagnostics — Connect pentru self-test hardware.";
            Status = HealthText;
            return;
        }

        DeviceHealthReport report;
        if (_device is IDeviceHealth health)
            report = await health.SelfTestAsync();
        else
        {
            report = new DeviceHealthReport
            {
                Status = HealthStatus.Unknown,
                Summary = "Dispozitivul nu expune IDeviceHealth",
                Checks = { _device.DisplayName, _device.State.ToString() }
            };
        }

        // Extra USB / exclusivity checks for lab overnight.
        report.Checks.Add(HbmUsbDeviceScanner.IsUsbHbmPresent()
            ? "PnP USBHBM: prezent (" + string.Join(", ", HbmUsbDeviceScanner.GetDevices().Select(d => d.Serial)) + ")"
            : "PnP USBHBM: absent");
        report.Checks.Add(IsCatmanEasyRunning()
            ? "catman Easy: RULEAZĂ (poate bloca USB)"
            : "catman Easy: oprit");
        try
        {
            if (Intfac32Native.IsAvailable())
            {
                var n = Intfac32Native.GetNumOpenHandles();
                report.Checks.Add($"Intfac handles deschise: {n}");
                if (n > 0 && report.Status == HealthStatus.Healthy)
                {
                    report = new DeviceHealthReport
                    {
                        Status = HealthStatus.Degraded,
                        Summary = report.Summary + " · handles Intfac>0",
                        Checks = report.Checks,
                        LastSampleUtc = report.LastSampleUtc,
                        SecondsSinceLastSample = report.SecondsSinceLastSample,
                        PortOpen = report.PortOpen,
                        FirmwareHint = report.FirmwareHint
                    };
                }
            }
            else report.Checks.Add("Intfac32.dll: indisponibil");
        }
        catch (Exception ex)
        {
            report.Checks.Add("Intfac check: " + ex.Message);
        }

        // Extra USB PnP check for Spider32 / USBHBM path
        if (_device is Spider32DllAdapter || SelectedBackend.Contains("Spider32", StringComparison.OrdinalIgnoreCase))
        {
            var usb = HbmUsbDeviceScanner.GetDevices();
            if (usb.Count == 0 && report.Status == HealthStatus.Healthy)
            {
                report = new DeviceHealthReport
                {
                    Status = HealthStatus.Degraded,
                    Summary = "Spider32 OK, dar USBHBM lipsește din PnP",
                    Checks = report.Checks,
                    LastSampleUtc = report.LastSampleUtc,
                    SecondsSinceLastSample = report.SecondsSinceLastSample,
                    PortOpen = report.PortOpen,
                    FirmwareHint = report.FirmwareHint
                };
            }
        }

        HealthChecks.Clear();
        foreach (var c in report.Checks) HealthChecks.Add(c);
        var statusRo = report.Status switch
        {
            HealthStatus.Healthy => "Sănătos",
            HealthStatus.Degraded => "Degradat",
            HealthStatus.Offline => "Offline",
            HealthStatus.Fault => "Defect",
            _ => "Necunoscut"
        };
        HealthText = $"{statusRo}: {report.Summary}";
        if (report.SecondsSinceLastSample is double age)
            HealthChecks.Add($"Ultimul eșantion: acum {age:0.0}s");
        if (!string.IsNullOrWhiteSpace(report.FirmwareHint))
            HealthChecks.Add("Firmware/IDN: " + report.FirmwareHint);
        Status = HealthText;
        _journal.Info(HealthText);
    }

    private async Task SearchDbAsync()
    {
        if (_db is null) return;
        var rows = await _db.SearchAsync(DbSearch);
        await UiAsync(() =>
        {
            DbResults.Clear();
            foreach (var r in rows)
            {
                DbResults.Add(new MeasurementRow
                {
                    Id = r.Id,
                    Created = r.CreatedLocal.ToString("yyyy-MM-dd HH:mm"),
                    Project = r.ProjectName,
                    SampleId = r.SampleId,
                    Operator = r.Operator,
                    Samples = r.SampleCount,
                    FilePath = r.FilePath,
                    Tags = r.Tags,
                    Backend = r.Backend
                });
            }
        });
    }

    private MeasurementRow? _selectedDbRow;
    public MeasurementRow? SelectedDbRow
    {
        get => _selectedDbRow;
        set { _selectedDbRow = value; OnPropertyChanged(); }
    }

    private void OpenSelectedDbFile()
    {
        var path = SelectedDbRow?.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "Select a DB row with a valid file.";
            return;
        }
        try
        {
            _offline = OfflineSession.FromCsv(path);
            RefreshAnalysisUi();
            LastRecordingPath = path;
            RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
            Status = $"Opened from DB: {path}";
        }
        catch (Exception ex)
        {
            Status = "Open failed: " + ex.Message;
        }
    }

    private async Task CatalogRecordingAsync(string path, int sampleCount)
    {
        if (_db is null || string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var id = await _db.InsertAsync(MeasurementDatabase.FromSession(
                path,
                new ProjectMeta { Operator = OperatorName, SampleId = SampleId, Comment = Comment },
                ProjectName,
                SelectedBackend,
                sampleCount,
                MeasurementTags));
            _journal.Info($"Cataloged measurement #{id}: {path}");
            await SearchDbAsync();
        }
        catch (Exception ex)
        {
            _journal.Error("DB insert failed: " + ex.Message);
        }
    }

    private void ExportPdfWithPlot()
    {
        try
        {
            OfflineSession session;
            if (_offline is not null) session = _offline;
            else if (!string.IsNullOrWhiteSpace(LastRecordingPath) && File.Exists(LastRecordingPath))
                session = OfflineSession.FromCsv(LastRecordingPath);
            else
            {
                var open = new OpenFileDialog
                {
                    Filter = "CSV|*.csv|" + UpetReportFile.FileFilter,
                    Title = "Selectați înregistrarea pentru raport PDF",
                    InitialDirectory = GetWritableRecordingsDirectory()
                };
                if (open.ShowDialog() != true) return;
                if (UpetReportFile.HasReportExtension(open.FileName) ||
                    UpetReportFile.LooksLikeUpetReport(open.FileName))
                {
                    session = UpetReportFile.Load(open.FileName);
                    ApplyAttachedMetaFromOffline(session);
                }
                else
                    session = OfflineSession.FromCsv(open.FileName);
                _offline = session;
                RefreshAnalysisUi();
            }

            var ytJpegs = new List<string>();
            var channelYtJpegs = new List<string>();
            var cwtJpegs = new List<string>();
            string? tempPdf = null;
            ReportGraphPack? graphPack = null;
            try
            {
                graphPack = ReportGraphPack.Build(session, SampleRateHz > 0 ? SampleRateHz : 50, CurrentProjectMeta());
                ytJpegs.AddRange(graphPack.OverviewJpegs.Where(File.Exists));
                channelYtJpegs.AddRange(graphPack.ChannelYtJpegs.Where(File.Exists));
                cwtJpegs.AddRange(graphPack.CwtJpegs.Where(File.Exists));
                if (ytJpegs.Count == 0)
                {
                    // Fallback single overview JPEG if pack failed.
                    var jpeg = Path.Combine(Path.GetTempPath(), $"upet_pdf_{Guid.NewGuid():N}.jpg");
                    SessionPlotRenderer.SaveJpeg(session, jpeg, 1000, 560);
                    ytJpegs.Add(jpeg);
                }
                if (cwtJpegs.Count == 0)
                    _journal.Warn("CWT export for PDF: no scalograms (insufficient samples or all channels inactive).");
            }
            catch (Exception ex)
            {
                _journal.Warn("Graph pack for PDF failed: " + ex.Message);
                try
                {
                    var jpeg = Path.Combine(Path.GetTempPath(), $"upet_pdf_fallback_{Guid.NewGuid():N}.jpg");
                    SessionPlotRenderer.SaveJpeg(session, jpeg, 1000, 560);
                    ytJpegs.Add(jpeg);
                }
                catch { /* ignore */ }
            }

            try
            {
                tempPdf = Path.Combine(Path.GetTempPath(), $"upet_preview_{Guid.NewGuid():N}.pdf");
                PdfReportExporter.Export(
                    tempPdf,
                    session,
                    CurrentProjectMeta(),
                    session.Stats,
                    ytJpegs.Count > 0 ? ytJpegs : null,
                    cwtJpegs.Count > 0 ? cwtJpegs : null,
                    channelYtJpegs.Count > 0 ? channelYtJpegs : null,
                    csvPath: Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                        LastRecordingPath, session.SourcePath));

                var previewImages = ytJpegs.Concat(channelYtJpegs).Concat(cwtJpegs).ToList();
                var summary =
                    $"{session.Timestamps.Count:N0} eșantioane · Y(t)×{ytJpegs.Count + channelYtJpegs.Count}" +
                    (cwtJpegs.Count > 0 ? $" · CWT×{cwtJpegs.Count}" : " · fără CWT") +
                    " — toată durata — confirmați Save As.";

                var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
                var preview = new Controls.PdfPreviewWindow(tempPdf, previewImages, summary)
                {
                    Owner = owner,
                    Tag = GetWritableRecordingsDirectory(),
                    SuggestedFileName = SuggestRaportFileName(".pdf")
                };
                var ok = preview.ShowDialog() == true;
                if (!ok || string.IsNullOrWhiteSpace(preview.ConfirmedSavePath))
                {
                    Status = "Export PDF anulat.";
                    return;
                }

                Status =
                    $"Raport PDF cu grafice în fișier (durată completă, Y(t)×{ytJpegs.Count + channelYtJpegs.Count}, CWT×{cwtJpegs.Count}, " +
                    $"{session.Timestamps.Count:N0} eșantioane): {preview.ConfirmedSavePath}";
                _journal.Info(Status);
            }
            finally
            {
                graphPack?.Dispose();
                if (tempPdf is not null)
                {
                    try { File.Delete(tempPdf); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            Status = "PDF eșuat: " + AppPaths.FriendlyIoMessage(ex, GetWritableRecordingsDirectory());
        }
    }

    private void ExportIndustrialReportPdf()
    {
        try
        {
            OfflineSession session;
            if (_offline is not null) session = _offline;
            else if (!string.IsNullOrWhiteSpace(LastRecordingPath) && File.Exists(LastRecordingPath))
                session = OfflineSession.FromCsv(LastRecordingPath);
            else
            {
                var open = new OpenFileDialog
                {
                    Filter = UpetReportFile.AnalysisOpenFilter,
                    FilterIndex = 1,
                    DefaultExt = "upet",
                    Title = "Selectați înregistrarea pentru raport industrial",
                    InitialDirectory = GetWritableRecordingsDirectory()
                };
                if (open.ShowDialog() != true) return;
                if (UpetReportFile.HasReportExtension(open.FileName) ||
                    UpetReportFile.LooksLikeUpetReport(open.FileName))
                {
                    session = UpetReportFile.Load(open.FileName);
                    ApplyAttachedMetaFromOffline(session);
                }
                else
                    session = OfflineSession.FromCsv(open.FileName);
                _offline = session;
                RefreshAnalysisUi();
            }

            SyncCursorsToOffline();
            var meta = CurrentProjectMeta();

            // Prefer sealed CSV path — never pass UI badge as trusted status (sticky "Alterată"
            // from VerifySession(rich) would short-circuit and poison the report).
            var csvForFp = Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                LastRecordingPath, session.SourcePath);
            var fpStatus = Core.Export.MeasurementFingerprint.ResolveReportStatusLabel(
                session, meta, csvPath: csvForFp);

            string? overviewJpeg = null;
            string? contourJpeg = null;
            string? contourSkipReason = null;
            var deformCurveJpegs = new List<string>();
            string? deformTempDir = null;
            try
            {
                overviewJpeg = Path.Combine(Path.GetTempPath(), $"upet_ind_yt_{Guid.NewGuid():N}.jpg");
                var plotSession = session;
                var n = session.Timestamps.Count;
                var a0 = Math.Min(CursorA, CursorB);
                var b0 = Math.Max(CursorA, CursorB);
                var isValidityZone = n > 1 && a0 != b0 && !(a0 == 0 && b0 == n - 1);
                if (isValidityZone)
                {
                    var a = Math.Clamp(a0, 0, n - 1);
                    var b = Math.Clamp(b0 + 1, a + 1, n);
                    plotSession = session.ExportRegion(a, b);
                }
                SessionPlotRenderer.SaveJpeg(plotSession, overviewJpeg, 1000, 420);
                if (!File.Exists(overviewJpeg) || new FileInfo(overviewJpeg).Length < 50)
                    overviewJpeg = null;
            }
            catch
            {
                overviewJpeg = null;
            }

            try
            {
                var metaForContour = CurrentProjectMeta();
                CylinderContourExport.MergeFromSession(metaForContour, session);
                if (CylinderContourExport.ShouldAttempt(metaForContour, session))
                {
                    contourJpeg = Path.Combine(Path.GetTempPath(), $"upet_ind_contour_{Guid.NewGuid():N}.jpg");
                    var saved = CylinderContourPlotRenderer.TrySaveJpeg(
                        session, metaForContour, contourJpeg, cursorA: CursorA, cursorB: CursorB);
                    if (saved is null)
                    {
                        contourJpeg = null;
                        var computed = CylinderContourExport.TryCompute(
                            session, metaForContour, CursorA, CursorB);
                        contourSkipReason = computed?.Error
                            ?? CylinderContourExport.DescribeSkipReason(metaForContour, session);
                    }
                    else
                    {
                        // Keep VM/meta in sync if EnsureConfig filled defaults
                        if (metaForContour.CylinderContour is not null)
                            CylinderContour = metaForContour.CylinderContour;

                        try
                        {
                            var contourResult = CylinderContourExport.TryCompute(
                                session, metaForContour, CursorA, CursorB);
                            if (contourResult is { IsValid: true })
                            {
                                deformTempDir = Path.Combine(Path.GetTempPath(), $"upet_ind_curbe_{Guid.NewGuid():N}");
                                var pack = CylinderDeformationCurveRenderer.TryBuildPngs(
                                    session, metaForContour, contourResult, deformTempDir,
                                    width: 1400, height: 780);
                                if (pack is not null)
                                {
                                    var ji = 0;
                                    foreach (var (_, png) in pack.AllPngs)
                                    {
                                        ji++;
                                        var jpg = Path.Combine(deformTempDir, $"curve_{ji:00}.jpg");
                                        try
                                        {
                                            var jpegBytes = ReportHeaderHelper.TryImageFileToJpegBytes(png, maxEdgePx: 1600);
                                            if (jpegBytes is { Length: > 0 })
                                            {
                                                File.WriteAllBytes(jpg, jpegBytes);
                                                deformCurveJpegs.Add(jpg);
                                            }
                                        }
                                        catch { /* skip one curve */ }
                                    }
                                }
                            }
                        }
                        catch { /* curves optional for industrial PDF */ }
                    }
                }
            }
            catch (Exception ex)
            {
                contourJpeg = null;
                contourSkipReason = "Contur cilindru omis: " + ex.Message;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Salvează raport industrial (PDF)",
                Filter = "PDF|*.pdf",
                FileName = SuggestIndustrialReportFileName(),
                InitialDirectory = GetWritableRecordingsDirectory()
            };
            if (dlg.ShowDialog() != true)
            {
                Status = "Raport industrial anulat.";
                TryDeleteTemp(overviewJpeg);
                TryDeleteTemp(contourJpeg);
                if (deformTempDir is not null)
                {
                    try { Directory.Delete(deformTempDir, recursive: true); } catch { /* ignore */ }
                }
                return;
            }

            try
            {
                IndustrialReportExporter.Export(
                    dlg.FileName,
                    session,
                    meta,
                    new IndustrialReportExporter.Options
                    {
                        SoftwareVersion = AppVersion,
                        FingerprintStatusLabel = fpStatus,
                        CsvPath = csvForFp,
                        PolarityOrDefectWarning = ShowPolarityWarningBanner
                            ? PolarityWarningText
                            : null,
                        OverviewJpegPath = overviewJpeg,
                        ContourJpegPath = contourJpeg,
                        DeformationCurveJpegPaths = deformCurveJpegs,
                        CursorA = CursorA,
                        CursorB = CursorB
                    });

                var n = session.Timestamps.Count;
                var a0 = Math.Min(CursorA, CursorB);
                var b0 = Math.Max(CursorA, CursorB);
                var zoneLabel = n > 1 && a0 != b0 && !(a0 == 0 && b0 == n - 1)
                    ? $"A–B {a0}–{b0}"
                    : "întreaga înregistrare";
                Status =
                    $"Raport industrial PDF (zonă {zoneLabel}" +
                    (contourJpeg is not null ? " · contur cilindru pe pagină dedicată" : "") +
                    (deformCurveJpegs.Count > 0 ? $" · {deformCurveJpegs.Count} curbe deformare" : "") +
                    $"): {dlg.FileName}" +
                    (contourJpeg is null && !string.IsNullOrWhiteSpace(contourSkipReason)
                        ? " — " + contourSkipReason
                        : "");
                _journal.Info(Status);
            }
            finally
            {
                TryDeleteTemp(overviewJpeg);
                TryDeleteTemp(contourJpeg);
                if (deformTempDir is not null)
                {
                    try { Directory.Delete(deformTempDir, recursive: true); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            Status = "Raport industrial eșuat: " + AppPaths.FriendlyIoMessage(ex, GetWritableRecordingsDirectory());
        }
    }

    private static void TryDeleteTemp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { /* ignore */ }
    }
}

public sealed class MeasurementRow
{
    public long Id { get; set; }
    public string Created { get; set; } = "";
    public string Project { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string Operator { get; set; } = "";
    public int Samples { get; set; }
    public string FilePath { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Backend { get; set; } = "";
}
