using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Spider8DAQ.App.Export;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Advisory;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.DataViewer;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Macros;
using Spider8DAQ.Core.MathChannels;
using Spider8DAQ.Core.Physics;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Simulation;
using Spider8DAQ.Core.Specimens;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>Bundled install catalog next to the EXE (may be read-only under Program Files).</summary>
    private static string BundledSensorsPath => AppPaths.BundledVendorFile("sensors.json");

    /// <summary>Writable catalog under LocalAppData — upgrades/edits survive Program Files installs.</summary>
    private static string WritableSensorsPath => AppPaths.SensorsCatalog;

    private string SensorsPath => ResolveSensorsPath();

    private static string ResolveSensorsPath()
    {
        try
        {
            var dir = Path.GetDirectoryName(WritableSensorsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return WritableSensorsPath;
        }
        catch
        {
            return BundledSensorsPath;
        }
    }

    private void EnsureLocalSensorsSeed()
    {
        var path = SensorsPath;
        var bundled = BundledSensorsPath;
        try
        {
            if (!File.Exists(path))
            {
                if (File.Exists(bundled) && !string.Equals(path, bundled, StringComparison.OrdinalIgnoreCase))
                    File.Copy(bundled, path, overwrite: false);
                return;
            }

            // If bundled catalog is newer, refresh AppData from bundled so U2B etc. land before LoadAsync merge.
            if (!File.Exists(bundled) || string.Equals(path, bundled, StringComparison.OrdinalIgnoreCase))
                return;
            var localVer = TryReadCatalogVersion(path);
            var bundledVer = TryReadCatalogVersion(bundled);
            if (bundledVer > localVer)
                File.Copy(bundled, path, overwrite: true);
        }
        catch { /* LoadAsync regenerates from embedded catalog */ }
    }

    private static int TryReadCatalogVersion(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("catalogVersion", out var v) && v.TryGetInt32(out var n))
                return n;
        }
        catch { /* ignore */ }
        return 0;
    }

    private void ApplySensorLibraryUi(string? statusOverride = null)
    {
        SensorCategories.Clear();
        SensorCategories.Add("(Toate)");
        foreach (var c in _sensorLibrary.Categories)
            SensorCategories.Add(c);
        if (!SensorCategories.Contains(SelectedSensorCategory))
            SelectedSensorCategory = "(Toate)";
        var n = _sensorLibrary.Sensors.Count;
        var m = _sensorLibrary.Categories.Count();
        SensorLibrarySummary = $"Bibliotecă senzori — {n} senzori / {m} categorii";
        RefreshSensorFilter();
        Status = statusOverride ?? $"Încărcat {n} senzori în {m} categorii.";
        _journal.Info(Status);
    }

    /// <summary>If UI was left empty (failed prior load), reload when user opens the library tab.</summary>
    public void EnsureSensorsLoadedForUi()
    {
        if (_sensorsLoadStarted && (SensorGroups.Count > 0 || _sensorLibrary.Sensors.Count > 0))
            return;
        if (SensorGroups.Count > 0 && _sensorLibrary.Sensors.Count > 0) return;
        _ = LoadSensorsAsync();
    }

    private bool _sensorsLoadStarted;

    private async Task LoadSensorsAsync(bool forceReload = false)
    {
        if (!forceReload && _sensorsLoadStarted && SensorGroups.Count > 0) return;
        _sensorsLoadStarted = true;
        try
        {
            EnsureLocalSensorsSeed();
            _sensorLibrary = await SensorLibrary.LoadAsync(SensorsPath);
            var audit = SensorApplyHelper.AuditLibrary(_sensorLibrary);
            if (audit.Count > 0)
                _journal.Warn($"Audit senzori: {audit.Count} note — {string.Join("; ", audit.Take(3))}");
            await UiAsync(() => ApplySensorLibraryUi(
                audit.Count == 0
                    ? null
                    : $"Bibliotecă {_sensorLibrary.Sensors.Count} senzori — audit: {audit.Count} note (vezi jurnal)."));
        }
        catch (Exception ex)
        {
            // Never leave the tree empty: embedded catalog is always available in-process.
            _sensorLibrary = SensorLibrary.CreateDefault();
            await UiAsync(() => ApplySensorLibraryUi(
                $"Catalog senzori (implicit, {_sensorLibrary.Sensors.Count} senzori): {ex.Message}"));
            _journal.Error($"Eroare încărcare senzori (fallback catalog): {ex.Message}");
            _ = SensorLibrary.TrySaveAsync(SensorsPath, _sensorLibrary);
        }
    }

    private void RefreshSensorFilter()
    {
        var keepName = SelectedSensorName;
        var keepCode = SelectedSensorRow?.Code;
        SensorNames.Clear();
        SensorRows.Clear();
        SensorGroups.Clear();
        var q = (_sensorSearchText ?? "").Trim();

        var preferredOrder = global::Spider8DAQ.Core.Sensors.SensorCategories.All.ToList();
        IEnumerable<SensorDefinition> source = _sensorLibrary.ByCategory(SelectedSensorCategory);

        var grouped = source
            .Where(s =>
            {
                if (q.Length == 0) return true;
                return s.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || s.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || s.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || s.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || s.Unit.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || (s.Notes ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || (s.TransducerType ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Category) ? "Personalizat" : s.Category)
            .OrderBy(g =>
            {
                var i = preferredOrder.FindIndex(c => string.Equals(c, g.Key, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? 1000 : i;
            })
            .ThenBy(g => g.Key);

        foreach (var g in grouped)
        {
            var group = new SensorCategoryGroup { Category = g.Key };
            foreach (var s in g.OrderBy(x => x.Code).ThenBy(x => x.Name))
            {
                var row = new SensorListRow
                {
                    Code = s.Code,
                    Category = s.Category,
                    Name = s.Name,
                    Unit = s.Unit,
                    Capacity = s.Capacity,
                    Bridge = s.Bridge,
                    Scale = s.Scale,
                    Offset = s.Offset,
                    Sensitivity = s.Sensitivity,
                    ExcitationV = s.ExcitationV,
                    ShuntKohm = s.ShuntKohm,
                    Notes = s.Notes ?? ""
                };
                group.Sensors.Add(row);
                SensorRows.Add(row);
                SensorNames.Add(s.Name);
            }
            if (group.Sensors.Count > 0)
                SensorGroups.Add(group);
        }

        SelectedSensorName = SensorNames.Contains(keepName ?? "")
            ? keepName
            : SensorNames.FirstOrDefault();
        SelectedSensorRow = SensorRows.FirstOrDefault(r => r.Name == SelectedSensorName)
                            ?? SensorRows.FirstOrDefault(r => r.Code == keepCode)
                            ?? SensorRows.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSensorTreeItem));
    }

    private async Task SaveSensorsAsync()
    {
        if (!await SensorLibrary.TrySaveAsync(SensorsPath, _sensorLibrary))
        {
            Status = $"Nu s-a putut salva catalogul (doar citire?): {SensorsPath}";
            _journal.Warn(Status);
            return;
        }
        Status = $"Sensors saved: {SensorsPath} ({_sensorLibrary.Sensors.Count})";
        _journal.Setup(Status);
    }

    private async Task ImportSensorsAsync()
    {
        var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        await SensorLibrary.ImportMergeAsync(dlg.FileName, _sensorLibrary);
        await SensorLibrary.TrySaveAsync(SensorsPath, _sensorLibrary);
        ApplySensorLibraryUi($"Import/merge senzori din {dlg.FileName} ({_sensorLibrary.Sensors.Count} total)");
        _journal.Setup(Status);
    }

    private async Task ExportSensorsAsync()
    {
        var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "sensors.json" };
        if (dlg.ShowDialog() != true) return;
        await SensorLibrary.SaveAsync(dlg.FileName, _sensorLibrary);
        Status = $"Senzori exportați: {dlg.FileName}";
    }

    private void AddSensor()
    {
        var cat = SelectedSensorCategory is "(Toate)" or "(All)"
            ? "Personalizat"
            : SelectedSensorCategory;
        var n = _sensorLibrary.Sensors.Count + 1;
        var prefix = global::Spider8DAQ.Core.Sensors.SensorCategories.CodePrefix(cat);
        var s = new SensorDefinition
        {
            Name = $"Personalizat {n}",
            Category = cat,
            Code = $"{prefix}-{n:D4}",
            Unit = "mV/V",
            Scale = 1,
            Bridge = "Full"
        };
        _sensorLibrary.Sensors.Add(s);
        if (!SensorCategories.Contains(cat)) SensorCategories.Add(cat);
        RefreshSensorFilter();
        SelectedSensorName = s.Name;
        SelectedSensorRow = SensorRows.FirstOrDefault(r => r.Name == s.Name);
    }

    private void DuplicateSensor()
    {
        if (SelectedSensorName is null) return;
        var src = _sensorLibrary.FindByName(SelectedSensorName);
        if (src is null) return;
        var n = _sensorLibrary.Sensors.Count + 1;
        var prefix = global::Spider8DAQ.Core.Sensors.SensorCategories.CodePrefix(src.Category);
        var copy = new SensorDefinition
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Code = $"{prefix}-{n:D4}",
            Category = src.Category,
            Name = src.Name + " (copie)",
            Unit = src.Unit,
            Scale = src.Scale,
            Offset = src.Offset,
            TransducerType = src.TransducerType,
            Notes = src.Notes,
            Sensitivity = src.Sensitivity,
            ExcitationV = src.ExcitationV,
            Capacity = src.Capacity,
            Bridge = src.Bridge,
            FilterHz = src.FilterHz,
            RangeMvPerV = src.RangeMvPerV
        };
        _sensorLibrary.Sensors.Add(copy);
        RefreshSensorFilter();
        SelectedSensorName = copy.Name;
        SelectedSensorRow = SensorRows.FirstOrDefault(r => r.Name == copy.Name);
        Status = $"Duplicat: {copy.Code} {copy.Name}";
    }

    private void DeleteSensor()
    {
        if (SelectedSensorName is null) return;
        var src = _sensorLibrary.FindByName(SelectedSensorName);
        if (src is null) return;
        var ask = MessageBox.Show(
            $"Ștergeți senzorul [{src.Code}] {src.Name}?",
            "UPET AcqLab",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;
        _sensorLibrary.Sensors.Remove(src);
        RefreshSensorFilter();
        Status = $"Sters: {src.Code}";
    }

    private void UpdateSelectedSensor()
    {
        if (SelectedSensorName is null) return;
        var src = _sensorLibrary.FindByName(SelectedSensorName);
        if (src is null) return;
        src.Name = string.IsNullOrWhiteSpace(EditSensorName) ? src.Name : EditSensorName.Trim();
        src.Unit = string.IsNullOrWhiteSpace(EditSensorUnit) ? src.Unit : EditSensorUnit.Trim();
        src.Scale = EditSensorScale;
        src.Bridge = string.IsNullOrWhiteSpace(EditSensorBridge) ? src.Bridge : EditSensorBridge.Trim();
        src.Notes = EditSensorNotes ?? "";
        src.ExcitationV = EditSensorExcitationV;
        src.ShuntKohm = EditSensorShuntKohm;
        RefreshSensorFilter();
        SelectedSensorName = src.Name;
        SelectedSensorRow = SensorRows.FirstOrDefault(r => r.Code == src.Code) ?? SensorRows.FirstOrDefault(r => r.Name == src.Name);
        Status = $"Actualizat [{src.Code}] {src.Name}";
        _journal.Setup(Status);
    }

    private void ApplySensor(bool allEnabled)
    {
        var sensor = ResolveSelectedSensor();
        if (sensor is null) return;
        IEnumerable<ChannelRow> targets = allEnabled
            ? Channels.Where(c => c.Enabled)
            : (ResolveSelectedHwChannel() is ChannelRow one
                ? new[] { one }
                : Array.Empty<ChannelRow>());

        if (!targets.Any())
        {
            Status = "Selectați un canal (CH0–CH7) sau activați canalele țintă.";
            return;
        }

        var overwrite = targets
            .Where(t => !string.IsNullOrWhiteSpace(t.SensorCategory)
                        && !string.Equals(t.SensorCategory, sensor.Category, StringComparison.OrdinalIgnoreCase))
            .Select(t => $"{t.Name}[{t.SensorCategory}]")
            .ToList();
        if (overwrite.Count > 0)
        {
            var msg =
                "Atenție: Apply va înlocui senzorul pe:\n" +
                string.Join("\n", overwrite.Take(6)) +
                $"\n\ncu [{sensor.Category}] {sensor.Name}.\n\nContinuați?";
            if (MessageBox.Show(msg, "Apply senzor", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                Status = "Apply anulat — canalele păstrează senzorii actuali.";
                return;
            }
        }

        SensorApplyHelper.ApplyResult? last = null;
        _suppressChannelConfigPush = true;
        try
        {
            foreach (var ch in targets)
            {
                var cfg = ToConfig(ch);
                last = SensorApplyHelper.Apply(cfg, sensor, allEnabled);
                ch.Name = cfg.Name;
                ch.Unit = cfg.Unit;
                ch.Scale = cfg.Scale;
                ch.Offset = cfg.Offset;
                ch.TareValue = cfg.TareValue;
                ch.SensorId = cfg.SensorId;
                ch.SensorName = cfg.SensorName;
                ch.SensorCategory = cfg.SensorCategory;
                ch.Capacity = cfg.Capacity;
                ch.ExcitationV = cfg.ExcitationV;
                if (sensor.ShuntKohm >= ShuntCheck.MinShuntKohm)
                    ch.ShuntKohm = sensor.ShuntKohm;
                ch.FilterHz = cfg.FilterHz;
                ch.ChannelSampleRateHz = cfg.ChannelSampleRateHz;
                ch.RangeMvPerV = cfg.RangeMvPerV;
                ch.Bridge = cfg.Bridge.ToString();
                ch.AlarmEnabled = cfg.AlarmEnabled;
                ch.AlarmLow = cfg.AlarmLow;
                ch.AlarmHigh = cfg.AlarmHigh;
                ch.Enabled = true;
                ch.ShowOnPlot = true;
                ch.RecordEnabled = true;
                _journal.Setup(
                    $"Senzor '{sensor.Category}/{sensor.Code}' → {ch.Name} (HW{ch.Index}): " +
                    $"scale={cfg.Scale:G6}, bridge={cfg.Bridge}, range={cfg.RangeMvPerV:G4}, " +
                    $"exc={cfg.ExcitationV:G4} V, filtru={cfg.FilterHz} Hz, rată={cfg.ChannelSampleRateHz} Hz" +
                    (sensor.ShuntKohm > 0 ? $", R-Shunt={sensor.ShuntKohm:0} kΩ" : ""));
            }
        }
        finally
        {
            _suppressChannelConfigPush = false;
        }
        if (targets.Any() && sensor.ChannelSampleRateHz > 0)
            SampleRateHz = sensor.ChannelSampleRateHz;
        RebuildPlotSeries();
        RefreshLiveValueHeaders();
        ResetCatmanLiveAxis();
        RefreshLiveGaugeChannelLabel();
        ResetAllLiveGaugeScales();
        ResetAllChannelPlotScales();
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: IsStreaming);

        var restartHint = IsStreaming
            ? " Măsurare repornită cu ASA nou (Stop→Start automat)."
            : " Apăsați Start (F5) după cablare.";
        var first = targets.First();
        Status = (allEnabled
            ? $"Aplicat [{sensor.Code}] pe {targets.Count()} canale — Scale={last?.Scale:G6} {sensor.Unit}."
            : $"Aplicat [{sensor.Code}] {sensor.Name} pe {first.Name} (HW{first.Index}) — Scale={last?.Scale:G6} {sensor.Unit}.")
            + restartHint;

        // Mixed engineering units → Dual Y(t) (Easy-style multi-sensor readability).
        var plotUnits = Channels
            .Where(c => c.Enabled && c.ShowOnPlot && !string.IsNullOrWhiteSpace(c.Unit))
            .Select(c => c.Unit.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plotUnits.Count >= 2 && PlotMode is DisplayLayouts.Yt or DisplayLayouts.Bar or DisplayLayouts.Numeric)
        {
            PlotMode = DisplayLayouts.DualYt;
            Status += " Viz: Dual Y(t) (unități mixte).";
        }

        RefreshWorkflowStepsOnly();
        RefreshOperatorStatus();
    }

    private SensorDefinition? ResolveSelectedSensor()
    {
        if (SelectedSensorRow is not null)
        {
            var byCode = _sensorLibrary.FindByCode(SelectedSensorRow.Code);
            if (byCode is not null) return byCode;
        }
        if (SelectedSensorName is not null)
        {
            var byName = _sensorLibrary.FindByName(SelectedSensorName);
            if (byName is not null) return byName;
        }
        return null;
    }

    private async Task PushChannelConfigAsync(bool reapplyAcquisition = false)
    {
        if (_device is null) return;
        try
        {
            await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
            if (reapplyAcquisition && IsStreaming)
            {
                await _device.StopStreamingAsync();
                await _device.StartStreamingAsync();
                IsStreaming = true;
                // Simulator resets Sequence→0; drop stale UI sample + DataLoggers so ScottPlot
                // does not throw "X coordinates must be in ascending order".
                await UiAsync(() =>
                {
                    Volatile.Write(ref _latestUiSample, null);
                    RebuildPlotSeries();
                    RebuildAllChannelPlotSeries();
                    ResetCatmanLiveAxis();
                });
                _journal.Info("Achiziție repornită — SoftSetup ASA/ACT pentru senzor(i) noi.");
            }
        }
        catch (Exception ex)
        {
            Status = "Config canale: " + ex.Message;
            _journal.Error(Status);
        }
    }

    private void EditSensorNavigate()
    {
        if (SelectedSensorRow is null)
        {
            Status = "Selectați un senzor din listă.";
            return;
        }
        RequestSensorLibraryTab?.Invoke(this, EventArgs.Empty);
        Status = $"Editați [{SelectedSensorRow.Code}] {SelectedSensorRow.Name} în panoul Edit.";
    }

    private void CopySensorCode()
    {
        if (SelectedSensorRow is null) return;
        try
        {
            System.Windows.Clipboard.SetText(SelectedSensorRow.Code);
            Status = $"Copiat cod: {SelectedSensorRow.Code}";
        }
        catch (Exception ex)
        {
            Status = "Clipboard: " + ex.Message;
        }
    }

    private void CopySensorName()
    {
        if (SelectedSensorRow is null) return;
        try
        {
            var text = $"{SelectedSensorRow.Code}  {SelectedSensorRow.Name}";
            System.Windows.Clipboard.SetText(text);
            Status = $"Copiat: {text}";
        }
        catch (Exception ex)
        {
            Status = "Clipboard: " + ex.Message;
        }
    }

    private void ShowSensorDetails()
    {
        if (SelectedSensorRow is null) return;
        System.Windows.MessageBox.Show(
            SelectedSensorRow.DetailsText,
            $"Detalii — {SelectedSensorRow.Code}",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private async Task ZeroSensorOffsetAsync()
    {
        if (SelectedSensorRow is null)
        {
            Status = "Selectați un senzor pentru Zero / Offset 0.";
            return;
        }

        var sensor = _sensorLibrary.FindByName(SelectedSensorRow.Name)
                     ?? _sensorLibrary.Sensors.FirstOrDefault(s => s.Code == SelectedSensorRow.Code);
        if (sensor is not null)
        {
            sensor.Offset = 0;
            SelectedSensorRow.Offset = 0;
            foreach (var ch in Channels.Where(c =>
                         (!string.IsNullOrEmpty(sensor.Id) && c.SensorId == sensor.Id)
                         || string.Equals(c.SensorName, sensor.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ch.Offset = 0;
            }
        }

        var idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (IsConnected)
        {
            await TareAsync(idx);
        }
        else if (idx is int i)
        {
            Channels[i].TareValue = 0;
            Channels[i].Offset = 0;
        }

        Status = sensor is null
            ? $"Zero {(idx is int z ? $"CH{z}" : "?")} (senzor fără definiție în bibliotecă)."
            : $"Zero / Offset 0: [{sensor.Code}] + tare {(idx is int t ? $"CH{t}" : "?")}.";
        _journal.Setup(Status);
    }

    private async Task SaveProjectAsync()
    {
        var dlg = new SaveFileDialog { Filter = "UPET AcqLab project (*.s8proj)|*.s8proj", FileName = $"{ProjectName}.s8proj" };
        if (dlg.ShowDialog() != true) return;
        var project = BuildProject();
        project.LastProjectPath = dlg.FileName;
        project.Meta.ModifiedUtc = DateTime.UtcNow;
        await ProjectStore.SaveAsync(dlg.FileName, project);
        Status = $"Proiect salvat: {dlg.FileName}";
        _journal.Info(Status);
    }

    private async Task LoadProjectAsync()
    {
        var dlg = new OpenFileDialog { Filter = "UPET AcqLab project (*.s8proj)|*.s8proj|JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        ApplyProject(await ProjectStore.LoadAsync(dlg.FileName));
        Status = $"Proiect încărcat: {dlg.FileName}";
        _journal.Info(Status);
    }

    private ProjectFile BuildProject()
    {
        var p = new ProjectFile
    {
        Name = ProjectName,
        DeviceMode = SelectedBackend,
        ComPort = SelectedPort,
        SampleRateHz = SampleRateHz,
        Channels = Channels.Select(ToConfig).ToList(),
        MathChannels = MathChannels.Select(m => new MathChannelDefinition
        {
            Name = m.Name, Unit = m.Unit,
            Operation = Enum.TryParse<MathOp>(m.Operation, out var op) ? op : MathOp.Average,
            SourceA = m.SourceA, SourceB = m.SourceB, SourceC = m.SourceC,
            WindowSize = m.WindowSize, Enabled = m.Enabled, Formula = m.Formula
        }).ToList(),
        Trigger = new TriggerSettings
        {
            Enabled = TriggerEnabled, ChannelIndex = TriggerChannel,
            Threshold = TriggerThreshold, RisingEdge = TriggerRising
        },
        AdvancedTrigger = _engine.AdvancedTrigger,
        Macros = { BuildMacroFromUi() },
        Meta = new ProjectMeta
        {
            Operator = OperatorName,
            SampleId = SampleId,
            Comment = Comment,
            Location = LabLocation ?? "",
            ExperimentType = ExperimentType ?? "",
            CylinderContour = ExperimentTypes.IsCylinderContour(ExperimentType) ? CylinderContour : null,
            PlannedSensors = PlannedSensors ?? "",
            ExperimentStartLocal = ExperimentStartedAt?.ToString("O") ?? "",
            EstimatedDurationMinutes = EstimatedDurationMinutes,
            SampleLengthMm = SampleLengthMm,
            SampleWidthMm = SampleWidthMm,
            SampleThicknessMm = SampleThicknessMm,
            SampleDiameterMm = SampleDiameterMm,
            SampleAreaMm2 = SampleAreaMm2,
            SampleMassG = SampleMassG,
            SampleDimensionsSummary = SampleDimensionsSummary ?? "",
            MontagePhotoPath = MontagePhotoPath ?? "",
            MontagePhotoAfterPath = MontagePhotoAfterPath ?? "",
            MontageBeforeNotes = MontageBeforeNotes ?? "",
            MontageAfterNotes = MontageAfterNotes ?? "",
            MontageBeforeCapturedLocal = MontageBeforeCapturedAt?.ToString("O") ?? "",
            MontageAfterCapturedLocal = MontageAfterCapturedAt?.ToString("O") ?? "",
            ExperimentEndLocal = ExperimentEndedAt?.ToString("O") ?? "",
            CreatedUtc = _project.Meta.CreatedUtc,
            ModifiedUtc = DateTime.UtcNow
        },
        Recording = new RecordingSettings
        {
            PreTriggerSamples = PreTriggerSamples, AppendMode = AppendMode, AutoFileName = AutoFileName,
            StorageMode = StorageMode, PeakIntervalSeconds = PeakIntervalSeconds
        },
        Devices = Devices.Select(d => new DeviceSlot
        {
            Index = d.Index, Name = d.Name, Enabled = d.Enabled, ComPort = d.ComPort, Notes = d.Notes
        }).ToList(),
        PanelMode = PanelMode,
        PlotMode = PlotMode,
        XyXChannel = XyXChannel,
        XyYChannel = XyYChannel,
        LastRecordingPath = LastRecordingPath
        };
        WriteSpecimenToMeta(p.Meta);
        WriteExperimentVideoToMeta(p.Meta);
        return p;
    }

    private void ApplyProject(ProjectFile project)
    {
        _project = project;
        ProjectName = project.Name;
        SelectedBackend = project.DeviceMode;
        SelectedPort = project.ComPort;
        SampleRateHz = project.SampleRateHz;
        TriggerEnabled = project.Trigger.Enabled;
        TriggerChannel = project.Trigger.ChannelIndex;
        TriggerThreshold = project.Trigger.Threshold;
        TriggerRising = project.Trigger.RisingEdge;
        OperatorName = project.Meta.Operator;
        SampleId = project.Meta.SampleId;
        Comment = project.Meta.Comment;
        if (!string.IsNullOrWhiteSpace(project.Meta.Location))
            LabLocation = project.Meta.Location;
        ExperimentType = project.Meta.ExperimentType ?? "";
        CylinderContour = project.Meta.CylinderContour;
        PlannedSensors = project.Meta.PlannedSensors ?? "";
        EstimatedDurationMinutes = project.Meta.EstimatedDurationMinutes;
        SampleLengthMm = project.Meta.SampleLengthMm;
        SampleWidthMm = project.Meta.SampleWidthMm;
        SampleThicknessMm = project.Meta.SampleThicknessMm;
        SampleDiameterMm = project.Meta.SampleDiameterMm;
        SampleAreaMm2 = project.Meta.SampleAreaMm2;
        SampleMassG = project.Meta.SampleMassG;
        SampleDimensionsSummary = string.IsNullOrWhiteSpace(project.Meta.SampleDimensionsSummary)
            ? SampleDimensions.BuildSummary(project.Meta)
            : project.Meta.SampleDimensionsSummary;
        ReadSpecimenFromMeta(project.Meta);
        ApplyExperimentVideoFromMeta(project.Meta);
        MontagePhotoPath = project.Meta.MontagePhotoPath ?? "";
        MontagePhotoAfterPath = project.Meta.MontagePhotoAfterPath ?? "";
        MontageBeforeNotes = project.Meta.MontageBeforeNotes ?? "";
        MontageAfterNotes = project.Meta.MontageAfterNotes ?? "";
        MontageBeforeCapturedAt = TryParseLocalIso(project.Meta.MontageBeforeCapturedLocal);
        MontageAfterCapturedAt = TryParseLocalIso(project.Meta.MontageAfterCapturedLocal);
        if (!string.IsNullOrWhiteSpace(project.Meta.ExperimentStartLocal) &&
            DateTime.TryParse(project.Meta.ExperimentStartLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var started))
            ExperimentStartedAt = started.ToLocalTime();
        if (!string.IsNullOrWhiteSpace(project.Meta.ExperimentEndLocal) &&
            DateTime.TryParse(project.Meta.ExperimentEndLocal, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var ended))
            ExperimentEndedAt = ended.ToLocalTime();
        IsExperimentActive = ExperimentStartedAt is not null && ExperimentEndedAt is null;
        PreTriggerSamples = project.Recording.PreTriggerSamples;
        AppendMode = project.Recording.AppendMode;
        AutoFileName = project.Recording.AutoFileName;
        StorageMode = string.IsNullOrWhiteSpace(project.Recording.StorageMode) ? "Full" : project.Recording.StorageMode;
        PeakIntervalSeconds = project.Recording.PeakIntervalSeconds > 0 ? project.Recording.PeakIntervalSeconds : 1.0;
        PanelMode = project.PanelMode;
        PlotMode = project.PlotMode switch
        {
            "YT" or "Y(t)" => DisplayLayouts.Yt,
            "XY" or "Y(X)" => DisplayLayouts.Yx,
            "Poisson" or "ν" or "Nu" => DisplayLayouts.Poisson,
            _ when DisplayLayouts.All.Contains(project.PlotMode) => project.PlotMode,
            _ => DisplayLayouts.Yt
        };
        XyXChannel = project.XyXChannel;
        XyYChannel = project.XyYChannel;
        LastRecordingPath = project.LastRecordingPath ?? "";

        foreach (var existing in Channels)
            existing.PropertyChanged -= OnChannelRowPropertyChanged;
        Channels.Clear();
        foreach (var ch in project.Channels)
        {
            var row = new ChannelRow
            {
                Index = ch.Index, DeviceIndex = ch.DeviceIndex, Name = ch.Name, Unit = ch.Unit,
                Enabled = ch.Enabled, ShowOnPlot = ch.Enabled, RecordEnabled = ch.RecordEnabled,
                Scale = ch.Scale, Offset = ch.Offset, TareValue = ch.TareValue,
                SensorId = ch.SensorId, SensorName = ch.SensorName, Bridge = ch.Bridge.ToString(),
                RangeMvPerV = ch.RangeMvPerV, FilterHz = ch.FilterHz, ChannelSampleRateHz = ch.ChannelSampleRateHz,
                ExcitationV = ch.ExcitationV, ShuntKohm = ch.ShuntKohm,
                GaugeFactor = ch.GaugeFactor, GaugeOhm = ch.GaugeOhm,
                HalfConfig = ch.HalfConfig, PoissonRatio = ch.PoissonRatio,
                BridgeFactor = ch.BridgeFactor,
                AlarmEnabled = ch.AlarmEnabled, AlarmLow = ch.AlarmLow,
                AlarmHigh = ch.AlarmHigh, ShuntEnabled = ch.ShuntEnabled, LastShuntReading = ch.LastShuntReading,
                Capacity = ch.Capacity, SensorCategory = ch.SensorCategory
            };
            row.PropertyChanged += OnChannelRowPropertyChanged;
            Channels.Add(row);
        }
        MathChannels.Clear();
        foreach (var m in project.MathChannels)
        {
            MathChannels.Add(new MathChannelRow
            {
                Name = m.Name, Unit = m.Unit, Operation = m.Operation.ToString(),
                SourceA = m.SourceA, SourceB = m.SourceB ?? 0, SourceC = m.SourceC ?? 0,
                WindowSize = m.WindowSize, Enabled = m.Enabled, Formula = m.Formula ?? ""
            });
        }
        Devices.Clear();
        foreach (var d in project.Devices)
            Devices.Add(new DeviceRow { Index = d.Index, Name = d.Name, Enabled = d.Enabled, ComPort = d.ComPort, Notes = d.Notes ?? "" });
        if (Devices.Count == 0) Devices.Add(new DeviceRow { Index = 0, Name = "Spider8 #1", Enabled = true });

        MacroSteps.Clear();
        var macro = project.Macros.FirstOrDefault() ?? MacroRunner.CreateDefaultMeasureSequence();
        foreach (var s in macro.Steps)
            MacroSteps.Add(new MacroStepRow { Type = s.Type.ToString(), IntParam = s.IntParam, Text = s.Text });

        RefreshLiveValueHeaders();
        ResetAllLiveGaugeScales();
        ResetAllChannelPlotScales();
        SyncMathToEngine();
        RebuildPlotSeries();
    }

    private void AddMathChannel()
    {
        MathChannels.Add(new MathChannelRow
        {
            Name = $"Math{MathChannels.Count + 1}",
            Operation = nameof(MathOp.Formula),
            Formula = "(CH1-CH2)",
            SourceA = 1, SourceB = 2, WindowSize = 25, Enabled = true
        });
        SyncMathToEngine();
        RefreshLiveValueHeaders();
    }

    private void LoadDefaultMacroSteps()
    {
        MacroSteps.Clear();
        foreach (var s in MacroRunner.CreateDefaultMeasureSequence(MacroSettleMs, MacroRecordMs).Steps)
            MacroSteps.Add(new MacroStepRow { Type = s.Type.ToString(), IntParam = s.IntParam, Text = s.Text });
    }

    private void LoadLabMacroSteps()
    {
        MacroSteps.Clear();
        var filter = (int)Math.Round(Channels.Where(c => c.Enabled).Select(c => c.FilterHz).DefaultIfEmpty(10).Average());
        foreach (var s in MacroRunner.CreateLabJobSequence(SampleRateHz, Math.Max(1, filter), MacroSettleMs, MacroRecordMs).Steps)
            MacroSteps.Add(new MacroStepRow { Type = s.Type.ToString(), IntParam = s.IntParam, Text = s.Text });
        Status = "Macro job lab încărcat (rată → filtru → tare → record).";
    }

    private MacroDefinition BuildMacroFromUi() => new()
    {
        Name = "Custom macro",
        Steps = MacroSteps.Select(s => new MacroStep
        {
            Type = Enum.TryParse<MacroStepType>(s.Type, out var t) ? t : MacroStepType.Status,
            IntParam = s.IntParam,
            Text = s.Text
        }).ToList()
    };

    private async Task SaveMacroFileAsync()
    {
        var dlg = new SaveFileDialog { Filter = "UPET macro (*.s8macro.json)|*.s8macro.json|JSON|*.json", FileName = "job.s8macro.json" };
        if (dlg.ShowDialog() != true) return;
        var macro = BuildMacroFromUi();
        macro.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
        await MacroStore.SaveAsync(dlg.FileName, macro);
        Status = "Macro salvat: " + dlg.FileName;
        _journal.Setup(Status);
    }

    private async Task LoadMacroFileAsync()
    {
        var dlg = new OpenFileDialog { Filter = "UPET macro (*.s8macro.json)|*.s8macro.json|JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        var macro = await MacroStore.LoadAsync(dlg.FileName);
        MacroSteps.Clear();
        foreach (var s in macro.Steps)
            MacroSteps.Add(new MacroStepRow { Type = s.Type.ToString(), IntParam = s.IntParam, Text = s.Text });
        RefreshMacroFlow();
        Status = $"Macro încărcat: {macro.Name} ({macro.Steps.Count} pași)";
    }

    private async Task RunMacroAsync()
    {
        CancelMacro();
        _macroCts = new CancellationTokenSource();
        try { await _macroRunner.RunAsync(BuildMacroFromUi(), this, _macroCts.Token); }
        catch (OperationCanceledException) { Status = "Macro anulat."; }
        catch (Exception ex) { Status = $"Macro eșuat: {ex.Message}"; _journal.Error(Status); }
        finally { MacroWaitingOperator = false; }
    }

    private void CancelMacro()
    {
        try { _macroCts?.Cancel(); } catch { /* ignore */ }
        _macroCts?.Dispose();
        _macroCts = null;
        MacroWaitingOperator = false;
        _macroOperatorWait?.TrySetCanceled();
        _macroOperatorWait = null;
    }

    private void AddDevice()
    {
        if (Devices.Count >= 8) { Status = "Max 8 Spider8 devices in cascade."; return; }
        Devices.Add(new DeviceRow { Index = Devices.Count, Name = $"Spider8 #{Devices.Count + 1}", Enabled = true });
        _journal.Setup($"Added device slot {Devices.Count}");
    }

    private void RebuildChannelsFromDevices()
    {
        var n = Math.Clamp(Devices.Count(d => d.Enabled), 1, 8) * 8;
        SeedDefaultChannels(n);
        Status = $"Rebuilt {n} channels for {n / 8} device(s).";
        _journal.Setup(Status);
        RebuildPlotSeries();
    }

    private void TedsAutoMapSensors()
    {
        // Classic Spider8 has no TEDS EEPROM — auto-suggest sensors from library for enabled channels.
        var assigned = 0;
        var pool = _sensorLibrary.Sensors
            .Where(s => s.Category.Contains("Forță", StringComparison.OrdinalIgnoreCase)
                        || s.Category.Contains("Generic", StringComparison.OrdinalIgnoreCase)
                        || s.Category.Contains("Punte", StringComparison.OrdinalIgnoreCase))
            .Take(Channels.Count)
            .ToList();
        if (pool.Count == 0) pool = _sensorLibrary.Sensors.Take(Channels.Count).ToList();

        for (var i = 0; i < Channels.Count && i < pool.Count; i++)
        {
            if (!Channels[i].Enabled) continue;
            var s = pool[i % pool.Count];
            Channels[i].SensorId = s.Id;
            Channels[i].SensorName = s.Name;
            Channels[i].Unit = s.Unit;
            Channels[i].Scale = s.Scale;
            Channels[i].Bridge = s.Bridge;
            Channels[i].ExcitationV = s.ExcitationV;
            Channels[i].ShuntKohm = s.ShuntKohm;
            Channels[i].FilterHz = s.FilterHz;
            Channels[i].RangeMvPerV = s.RangeMvPerV;
            assigned++;
        }
        RefreshLiveValueHeaders();
        foreach (var d in Devices.Where(x => x.Enabled))
            d.Notes = "Auto-map senzori (fără TEDS hardware)";
        Status = $"Scan senzori: {assigned} canale mapate din bibliotecă (Spider8 fără TEDS fizic).";
        _journal.Setup(Status);
    }

    private void ReplayCsv()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _offline = OfflineSession.FromCsv(dlg.FileName);
            _replayMode = true;
            RefreshAnalysisUi();
            RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
            Status = $"Replay/analysis loaded {_offline.Timestamps.Count} samples.";
        }
        catch (Exception ex) { Status = $"Replay failed: {ex.Message}"; }
    }

    private void LoadAnalysisCsv()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Încarcă CSV sau raport .upet",
            Filter = UpetReportFile.AnalysisOpenFilter,
            FilterIndex = 1,
            DefaultExt = "upet",
            CheckFileExists = true,
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (UpetReportFile.LooksLikeUpetReport(dlg.FileName) ||
                UpetReportFile.HasReportExtension(dlg.FileName))
            {
                _offline = UpetReportFile.Load(dlg.FileName);
                Annotations.Clear();
                ApplyAttachedMetaFromOffline(_offline);
                RefreshAnalysisUi();
                ApplyFingerprintVerification(dlg.FileName);
            try { ScanOfflineDefectsNow(); } catch { /* ignore */ }
            try { RequestAdvisorPeerRefresh(force: true); } catch { /* ignore */ }
            var photos = CountMontageInSession(_offline);
                Status = photos > 0
                    ? $"Raport UPET (.upet) încărcat: {_offline.Timestamps.Count:N0} eșantioane · {photos} poză(e) montaj — {dlg.FileName}"
                    : $"Raport UPET (.upet) încărcat: {_offline.Timestamps.Count:N0} eșantioane — {dlg.FileName}";
                if (!string.IsNullOrWhiteSpace(FingerprintStatusText))
                    Status += " · " + FingerprintStatusText;
                return;
            }

            _offline = OfflineSession.FromCsv(dlg.FileName);
            ImportRecordingMarksFromCsv(dlg.FileName);
            RefreshAnalysisUi();
            ApplyFingerprintVerification(dlg.FileName);
            try { ScanOfflineDefectsNow(); } catch { /* ignore */ }
            try { RequestAdvisorPeerRefresh(force: true); } catch { /* ignore */ }
            Status = Annotations.Count > 0
                ? $"Analysis loaded (+{Annotations.Count} MARK): {dlg.FileName}"
                : $"Analysis loaded: {dlg.FileName}";
            if (!string.IsNullOrWhiteSpace(FingerprintStatusText))
                Status += " · " + FingerprintStatusText;
        }
        catch (Exception ex) { Status = $"Analysis load failed: {ex.Message}"; }
    }

    private void ExportUpetReport()
    {
        OfflineSession? session = TryResolveExportSession(out var resolveError);
        if (session is null)
        {
            Status = string.IsNullOrWhiteSpace(resolveError)
                ? "Nu există date de exportat — Record sau Load CSV / Open .upet întâi."
                : resolveError;
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export raport proprietar UPET AcqLab (.upet)",
            Filter = UpetReportFile.FileFilter,
            FilterIndex = 1,
            DefaultExt = UpetReportFile.Extension.TrimStart('.'),
            AddExtension = true,
            FileName = SuggestRaportFileName(UpetReportFile.Extension),
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true)
        {
            Status = "Export .upet anulat.";
            return;
        }

        var path = dlg.FileName;
        if (!UpetReportFile.HasReportExtension(path))
            path += UpetReportFile.Extension;

        try
        {
            Status = "Se generează raportul .upet…";
            UpetReportFileAssociation.EnsureRegistered();
            var meta = CurrentProjectMeta();
            IReadOnlyList<(string Role, string Name, byte[] PngBytes)> graphs =
                Array.Empty<(string, string, byte[])>();
            try
            {
                using var pack = ReportGraphPack.Build(session, SampleRateHz > 0 ? SampleRateHz : 50, CurrentProjectMeta());
                graphs = pack.ToEmbeddedPngAssets().ToList();
            }
            catch (Exception graphEx)
            {
                _journal.Warn("Grafice .upet omise: " + graphEx.Message);
            }

            var montageCount = UpetReportFile.CollectMontageAssets(meta).Count;
            UpetReportFile.Save(path, session, meta, graphs);

            // Keep analysis session so follow-up exports / Open stay consistent.
            if (_offline is null)
            {
                _offline = session;
                try { RefreshAnalysisUi(); } catch { /* non-fatal */ }
            }

            if (!string.IsNullOrWhiteSpace(meta.MeasurementFingerprint))
            {
                MeasurementFingerprint = meta.MeasurementFingerprint;
                FingerprintStatusText = "Validă · " + meta.MeasurementFingerprint;
                SetFingerprintStatusBrush("#2E7D32");
            }
            Status =
                $"Raport .upet salvat ({session.Timestamps.Count:N0} eșantioane, {graphs.Count} grafice" +
                (montageCount > 0 ? $", {montageCount} poză(e) montaj" : "") +
                (string.IsNullOrWhiteSpace(meta.MeasurementFingerprint) ? "" : $", amprentă {meta.MeasurementFingerprint}") +
                $" în fișier — toată durata): {path}";
            _journal.Info(Status);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("Export .upet eșuat.");
            Status = "Export .upet eșuat: " + AppPaths.FriendlyIoMessage(ex, path);
            _journal.Error(Status);
        }
    }

    /// <summary>
    /// Resolves offline analysis, last CSV recording, or DataViewer selection for export.
    /// </summary>
    private OfflineSession? TryResolveExportSession(out string? error)
    {
        error = null;
        if (_offline is { Timestamps.Count: > 0 })
            return _offline;

        string? selectedViewer = null;
        try
        {
            selectedViewer = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA;
        }
        catch { /* property may be unset early */ }

        foreach (var candidate in new[] { LastRecordingPath, selectedViewer })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;
            try
            {
                if (UpetReportFile.LooksLikeUpetReport(candidate) ||
                    UpetReportFile.HasReportExtension(candidate))
                {
                    var loaded = UpetReportFile.Load(candidate);
                    if (loaded.Timestamps.Count > 0) return loaded;
                    continue;
                }

                var fromCsv = OfflineSession.FromCsv(candidate);
                if (fromCsv.Timestamps.Count > 0) return fromCsv;
            }
            catch (Exception ex)
            {
                error = "Nu pot citi înregistrarea pentru .upet: " + ex.Message;
            }
        }

        return null;
    }

    private async Task ExportLabPackageAsync()
    {
        OfflineSession? session = TryResolveExportSession(out var resolveError);
        if (session is null)
        {
            Status = string.IsNullOrWhiteSpace(resolveError)
                ? "Nu există date de împachetat — Record sau Load CSV / Open .upet întâi."
                : resolveError;
            return;
        }

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var opts = new Controls.LabPackageOptionsWindow { Owner = owner };
        if (opts.ShowDialog() != true || !opts.Confirmed) return;

        var stamp = ExperimentStartedAt ?? DateTime.Now;
        var packStem = ExperimentFileNaming.BuildStem(SampleId, stamp) + "_pachet";
        var usePassword = opts.UsePassword;
        var password = opts.Password;
        var dlg = new SaveFileDialog
        {
            Title = usePassword ? "Salvează pachet laborator (.upetlab)" : "Salvează pachet laborator (.zip)",
            Filter = usePassword
                ? "Pachet criptat UPET (*.upetlab)|*.upetlab"
                : "ZIP laborator (*.zip)|*.zip",
            DefaultExt = usePassword ? "upetlab" : "zip",
            AddExtension = true,
            FileName = packStem + (usePassword ? UpetLabPackageFile.Extension : ".zip"),
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        if (!TryBeginExport()) return;

        var dest = dlg.FileName;
        var staging = Path.Combine(Path.GetTempPath(), "upet_pachet_" + Guid.NewGuid().ToString("N"));
        var meta = CurrentProjectMeta();
        var lastCsv = LastRecordingPath;
        var sampleId = SampleId;
        var fs = SampleRateHz > 0 ? SampleRateHz : 50;
        var montageBefore = HasMontagePhoto ? MontagePhotoPath : null;
        var montageAfter = HasMontagePhotoAfter ? MontagePhotoAfterPath : null;
        Status = "Pachet laborator în curs… interfața rămâne activă.";
        try
        {
            var status = await Task.Run(() =>
            {
                Directory.CreateDirectory(staging);
                var included = new List<string>();
                string? csvName;

                if (!string.IsNullOrWhiteSpace(lastCsv) && File.Exists(lastCsv))
                {
                    csvName = Path.GetFileName(lastCsv);
                    LabPackageBuilder.TryCopy(lastCsv, staging, csvName);
                    included.Add(csvName);
                }
                else
                {
                    csvName = ExperimentFileNaming.BuildFileName(sampleId, stamp, null, ".csv");
                    session.SaveCsv(Path.Combine(staging, csvName));
                    included.Add(csvName);
                }

                if (!string.IsNullOrWhiteSpace(montageBefore))
                {
                    var name = Path.GetFileName(montageBefore);
                    LabPackageBuilder.TryCopy(montageBefore, staging, name);
                    included.Add(name);
                }

                if (!string.IsNullOrWhiteSpace(montageAfter))
                {
                    var name = Path.GetFileName(montageAfter);
                    LabPackageBuilder.TryCopy(montageAfter, staging, name);
                    included.Add(name);
                }

                LabPackageBuilder.CopyExperimentVideos(meta, staging, included);

                var upetName = ExperimentFileNaming.BuildFileName(
                    sampleId, stamp, ExperimentFileNaming.RoleRaport, UpetReportFile.Extension);
                try
                {
                    using var pack = ReportGraphPack.Build(session, fs, meta);
                    var graphs = pack.ToEmbeddedPngAssets();
                    UpetReportFile.Save(Path.Combine(staging, upetName), session, meta, graphs);
                }
                catch (Exception upetEx)
                {
                    _journal.Warn("Pachet: grafice .upet omise — " + upetEx.Message);
                    UpetReportFile.Save(Path.Combine(staging, upetName), session, meta);
                }
                included.Add(upetName);

                var xlsxName = ExperimentFileNaming.BuildFileName(
                    sampleId, stamp, ExperimentFileNaming.RoleRaport, ".xlsx");
                try
                {
                    ExcelReportExporter.Export(
                        Path.Combine(staging, xlsxName), session, meta,
                        csvPath: Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                            lastCsv, session.SourcePath));
                    included.Add(xlsxName);
                }
                catch (Exception ex)
                {
                    _journal.Warn("Pachet: Excel omis — " + ex.Message);
                }

                var htmlName = ExperimentFileNaming.BuildFileName(
                    sampleId, stamp, ExperimentFileNaming.RoleRaport, ".html");
                try
                {
                    HtmlReportExporter.Export(
                        Path.Combine(staging, htmlName), session, meta, session.Stats,
                        csvPath: Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                            lastCsv, session.SourcePath));
                    included.Add(htmlName);
                }
                catch (Exception ex)
                {
                    _journal.Warn("Pachet: HTML omis — " + ex.Message);
                }

                LabPackageBuilder.WriteReadme(
                    staging,
                    LabPackageBuilder.BuildReadme(meta, csvName, included, session));
                included.Add("README_laborator.txt");

                var zipBytes = LabPackageBuilder.ZipDirectoryToBytes(staging);
                if (usePassword)
                    UpetLabPackageFile.SaveEncrypted(dest, zipBytes, password);
                else
                    File.WriteAllBytes(dest, zipBytes);

                return
                    $"Pachet laborator salvat ({included.Count} fișiere, {session.Timestamps.Count:N0} eșantioane" +
                    (usePassword ? ", AES parolă" : ", ZIP deschis") +
                    $"): {dest}";
            }).ConfigureAwait(true);

            Status = status;
            _journal.Info(Status);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + dest + "\"",
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("Pachet laborator eșuat.");
            Status = "Pachet laborator eșuat: " + AppPaths.FriendlyIoMessage(ex, dest);
        }
        finally
        {
            EndExport();
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch { /* ignore */ }
        }
    }

    private void OpenLabPackage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Deschide pachet laborator",
            Filter = UpetLabPackageFile.FileFilter,
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            byte[] zipBytes;
            if (UpetLabPackageFile.LooksLikeEncryptedPackage(dlg.FileName))
            {
                var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current?.MainWindow;
                var prompt = new Controls.PasswordPromptWindow(
                    "Parolă pachet .upetlab",
                    "Introduceți parola pentru:\n" + Path.GetFileName(dlg.FileName))
                {
                    Owner = owner
                };
                if (prompt.ShowDialog() != true || !prompt.Confirmed)
                {
                    Status = "Deschidere pachet anulată.";
                    return;
                }

                zipBytes = UpetLabPackageFile.LoadEncrypted(dlg.FileName, prompt.Password);
            }
            else
            {
                zipBytes = File.ReadAllBytes(dlg.FileName);
            }

            var outDir = Path.Combine(
                GetWritableRecordingsDirectory(),
                Path.GetFileNameWithoutExtension(dlg.FileName) + "_extras");
            if (Directory.Exists(outDir))
                outDir += "_" + DateTime.Now.ToString("HHmmss");
            LabPackageBuilder.ExtractZipBytes(zipBytes, outDir);
            _analysisVideoSearchDir = outDir;

            var upet = Directory.EnumerateFiles(outDir, "*" + UpetReportFile.Extension, SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (upet is not null)
                TryOpenUpetReportPath(upet);
            else
            {
                var csv = Directory.EnumerateFiles(outDir, "*.csv", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (csv is not null)
                {
                    _offline = OfflineSession.FromCsv(csv);
                    ImportRecordingMarksFromCsv(csv);
                    RefreshAnalysisUi();
                    RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
                }
            }

            BindAnalysisVideoFromSession(outDir);

            Status = $"Pachet extras: {outDir}" + (HasAnalysisVideo ? " · clip încărcat în Analiză" : (upet is not null ? " · .upet încărcat în Analiză" : ""));
            _journal.Info(Status);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outDir,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            Status = "Deschidere pachet eșuată: " + ex.Message;
        }
    }

    private void ApplyAttachedMetaFromOffline(OfflineSession session)
    {
        if (session.AttachedMeta is not { } m) return;
        if (!string.IsNullOrWhiteSpace(m.Operator)) OperatorName = m.Operator;
        if (!string.IsNullOrWhiteSpace(m.SampleId)) SampleId = m.SampleId;
        if (!string.IsNullOrWhiteSpace(m.Comment)) Comment = m.Comment;
        if (!string.IsNullOrWhiteSpace(m.Location)) LabLocation = m.Location;
        if (!string.IsNullOrWhiteSpace(m.ProjectName)) ProjectName = m.ProjectName;
        if (!string.IsNullOrWhiteSpace(m.ExperimentType)) ExperimentType = m.ExperimentType;
        if (ExperimentTypes.IsCylinderContour(m.ExperimentType) && m.CylinderContour is not null)
            CylinderContour = m.CylinderContour;
        else if (ExperimentTypes.IsExplicitNonContour(m.ExperimentType))
            CylinderContour = null;
        if (!string.IsNullOrWhiteSpace(m.PlannedSensors)) PlannedSensors = m.PlannedSensors;
        if (m.SampleRateHz > 0) SampleRateHz = m.SampleRateHz;
        EstimatedDurationMinutes = m.EstimatedDurationMinutes;
        ExperimentStartedAt = TryParseLocalIso(m.ExperimentStartLocal);
        ExperimentEndedAt = TryParseLocalIso(m.ExperimentEndLocal);
        MontagePhotoPath = m.MontagePhotoPath ?? "";
        MontagePhotoAfterPath = m.MontagePhotoAfterPath ?? "";
        MontageBeforeNotes = m.MontageBeforeNotes ?? "";
        MontageAfterNotes = m.MontageAfterNotes ?? "";
        MontageBeforeCapturedAt = TryParseLocalIso(m.MontageBeforeCapturedLocal);
        MontageAfterCapturedAt = TryParseLocalIso(m.MontageAfterCapturedLocal);
        SampleLengthMm = m.SampleLengthMm;
        SampleWidthMm = m.SampleWidthMm;
        SampleThicknessMm = m.SampleThicknessMm;
        SampleDiameterMm = m.SampleDiameterMm;
        SampleAreaMm2 = m.SampleAreaMm2;
        SampleMassG = m.SampleMassG;
        SampleDimensionsSummary = string.IsNullOrWhiteSpace(m.SampleDimensionsSummary)
            ? SampleDimensions.BuildSummary(m)
            : m.SampleDimensionsSummary;
        ReadSpecimenFromMeta(m);
        ApplyExperimentVideoFromMeta(m);
        if (!string.IsNullOrWhiteSpace(m.MeasurementFingerprint))
            MeasurementFingerprint = m.MeasurementFingerprint;
    }

    private void ApplyFingerprintVerification(string? csvPath = null)
    {
        try
        {
            // Prefer sealed CSV verify — VerifySession against rich meta false-positives Alterată
            // when the code was produced by SealCsvFile (slim meta + file bytes).
            var csv = Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                csvPath, _offline?.SourcePath);
            if (csv is not null)
            {
                var vr = Core.Export.MeasurementFingerprint.VerifyCsvFile(csv);
                if (vr.Status != Core.Export.MeasurementFingerprint.VerifyStatus.Missing)
                {
                    MeasurementFingerprint = vr.StoredCode ?? "";
                    SetFingerprintUi(vr);
                    return;
                }
            }

            if (_offline?.AttachedMeta is { } meta &&
                !string.IsNullOrWhiteSpace(meta.MeasurementFingerprint))
            {
                var vr = Core.Export.MeasurementFingerprint.VerifySession(_offline, meta);
                MeasurementFingerprint = vr.StoredCode ?? meta.MeasurementFingerprint;
                SetFingerprintUi(vr);
                return;
            }

            if (string.IsNullOrWhiteSpace(MeasurementFingerprint))
            {
                FingerprintStatusText = "";
                SetFingerprintStatusBrush("#607D8B");
            }
        }
        catch (Exception ex)
        {
            FingerprintStatusText = "Verificare amprentă: " + ex.Message;
            SetFingerprintStatusBrush("#E65100");
        }
    }

    private void SetFingerprintUi(Core.Export.MeasurementFingerprint.VerifyResult vr)
    {
        FingerprintStatusText =
            Core.Export.MeasurementFingerprint.StatusLabelRo(vr.Status) +
            (string.IsNullOrWhiteSpace(vr.StoredCode) ? "" : " · " + vr.StoredCode);
        SetFingerprintStatusBrush(vr.Status switch
        {
            Core.Export.MeasurementFingerprint.VerifyStatus.Valid => "#2E7D32",
            Core.Export.MeasurementFingerprint.VerifyStatus.Altered => "#C62828",
            Core.Export.MeasurementFingerprint.VerifyStatus.Incomplete => "#E65100",
            _ => "#607D8B"
        });
    }

    private static int CountMontageInSession(OfflineSession session)
    {
        var n = 0;
        if (session.AttachedGraphs.Any(g =>
                string.Equals(g.Role, UpetReportFile.RoleMontageBefore, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(g.Role, UpetReportFile.RoleMontageAfter, StringComparison.OrdinalIgnoreCase)))
            return session.AttachedGraphs.Count(g =>
                g.Role.StartsWith("montage", StringComparison.OrdinalIgnoreCase));
        if (session.AttachedMeta is { } m)
        {
            if (!string.IsNullOrWhiteSpace(m.MontagePhotoPath) && File.Exists(m.MontagePhotoPath)) n++;
            if (!string.IsNullOrWhiteSpace(m.MontagePhotoAfterPath) && File.Exists(m.MontagePhotoAfterPath)) n++;
        }
        return n;
    }

    private string SuggestRaportFileName(string extension)
    {
        if (ExperimentStartedAt is { } stamp)
            return ExperimentFileNaming.BuildFileName(
                SampleId, stamp, ExperimentFileNaming.RoleRaport, extension);

        if (!string.IsNullOrWhiteSpace(LastRecordingPath))
        {
            var stem = Path.GetFileNameWithoutExtension(LastRecordingPath);
            if (stem.EndsWith("_" + ExperimentFileNaming.RoleRaport, StringComparison.OrdinalIgnoreCase))
                return stem + ExperimentFileNamingNormalizeExt(extension);
            return stem + "_" + ExperimentFileNaming.RoleRaport + ExperimentFileNamingNormalizeExt(extension);
        }

        return ExperimentFileNaming.BuildFileName(
            SampleId, DateTime.Now, ExperimentFileNaming.RoleRaport, extension);
    }

    private string SuggestIndustrialReportFileName()
    {
        const string ext = ".pdf";
        if (ExperimentStartedAt is { } stamp)
            return ExperimentFileNaming.BuildFileName(
                SampleId, stamp, ExperimentFileNaming.RoleIndustrial, ext);

        if (!string.IsNullOrWhiteSpace(LastRecordingPath))
        {
            var stem = Path.GetFileNameWithoutExtension(LastRecordingPath);
            foreach (var suffix in new[]
                     {
                         "_" + ExperimentFileNaming.RoleRaport,
                         "_" + ExperimentFileNaming.RoleIndustrial,
                         "_raport_industrial"
                     })
            {
                if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    stem = stem[..^suffix.Length];
                    break;
                }
            }
            return stem + "_" + ExperimentFileNaming.RoleIndustrial + ext;
        }

        return ExperimentFileNaming.BuildFileName(
            SampleId, DateTime.Now, ExperimentFileNaming.RoleIndustrial, ext);
    }

    private static string ExperimentFileNamingNormalizeExt(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return "";
        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private string PromoteMontagePhoto(string? sourcePath, string? sampleId, DateTime stamp, string role)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return "";
        try
        {
            return ExperimentFileNaming.EnsureUnifiedCopy(
                sourcePath,
                GetWritableRecordingsDirectory(),
                sampleId,
                stamp,
                role,
                deleteSourceIfDifferent: true);
        }
        catch
        {
            return sourcePath;
        }
    }

    private void OpenUpetReport()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Deschide raport UPET AcqLab (.upet)",
            Filter = UpetReportFile.FileFilter,
            FilterIndex = 1,
            DefaultExt = UpetReportFile.Extension.TrimStart('.'),
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        TryOpenUpetReportPath(dlg.FileName);
    }

    private void ImportRecordingMarksFromCsv(string path)
    {
        Annotations.Clear();
        try
        {
            foreach (var line in Spider8DAQ.Core.Export.CsvSharedIO.EnumerateLines(path))
            {
                var t = line.TrimStart().TrimStart('\uFEFF');
                if (!t.StartsWith("# MARK ", StringComparison.OrdinalIgnoreCase)) continue;
                Annotations.Add(t[7..].Trim());
                if (Annotations.Count >= 200) break;
            }
        }
        catch { /* ignore */ }
    }

    private void SmoothAnalysis()
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        _offline.SmoothAll(SmoothWindow);
        RefreshAnalysisUi();
    }

    private void CutAnalysis()
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        _offline.Cut(CutStart, CutEnd);
        RefreshAnalysisUi();
    }

    private void FindPeaks()
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        PeakIndexes.Clear();
        foreach (var p in _offline.Peaks(AnalysisChannel).Take(200))
            PeakIndexes.Add($"idx {p}: {_offline.Columns[AnalysisChannel][p]:0.####}");
        RefreshAnalysisPlot(_offline.Peaks(AnalysisChannel));
    }

    private void SaveAnalysisCsv()
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "analysis.csv" };
        if (dlg.ShowDialog() != true) return;
        _offline.SaveCsv(dlg.FileName);
        var png = SessionPlotRenderer.TrySaveSiblingPng(_offline, dlg.FileName);
        Status = png is null
            ? $"Saved {dlg.FileName}"
            : $"Saved {dlg.FileName} + {Path.GetFileName(png)}";
    }

    private void ExportCursorRegion()
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        var start = Math.Min(CursorA, CursorB);
        var end = Math.Max(CursorA, CursorB) + 1;
        var region = _offline.ExportRegion(start, end);
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "region.csv",
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        region.SaveCsv(dlg.FileName);
        var png = SessionPlotRenderer.TrySaveSiblingPng(region, dlg.FileName);
        Status = png is null
            ? $"Exported region [{start},{end}) -> {dlg.FileName}"
            : $"Exported region [{start},{end}) -> {dlg.FileName} + {Path.GetFileName(png)}";
    }

    private void ExportAnalysis(string kind) => _ = ExportAnalysisAsync(kind);

    private async Task ExportAnalysisAsync(string kind)
    {
        if (_offline is null) { Status = "Load analysis first."; return; }
        if (!TryBeginExport()) return;
        var dlg = new SaveFileDialog
        {
            Filter = kind switch { "mat" => "MAT|*.mat", "xlsx" => "Excel|*.xlsx", _ => "TXT|*.txt" },
            FileName = SuggestRaportFileName("." + kind),
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true)
        {
            EndExport();
            return;
        }

        var dest = dlg.FileName;
        var session = _offline;
        var meta = CurrentProjectMeta();
        CylinderContourExport.MergeFromSession(meta, session);
        var csvPath = Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
            LastRecordingPath, session.SourcePath);
        Status = kind == "xlsx"
            ? "Export Excel în curs… interfața rămâne activă."
            : $"Export {kind.ToUpperInvariant()} în curs…";
        try
        {
            var status = await Task.Run(() =>
            {
                if (kind == "mat")
                {
                    session.ExportMat(dest);
                    return $"MAT: {dest}{SaveFullGraphsBeside(session, dest, meta, SampleRateHz, csvPath)}";
                }

                if (kind == "xlsx")
                {
                    var result = ExcelReportExporter.Export(dest, session, meta, csvPath: csvPath);
                    return FormatExcelExportStatus(result);
                }

                session.ExportTxt(dest);
                return $"TXT: {dest}{SaveFullGraphsBeside(session, dest, meta, SampleRateHz, csvPath)}";
            }).ConfigureAwait(true);
            Status = status;
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("Export eșuat.");
            Status = "Export eșuat: " + AppPaths.FriendlyIoMessage(ex, dest);
        }
        finally
        {
            EndExport();
        }
    }

    private async Task ExportHtmlReportAsync()
    {
        OfflineSession? session = _offline;
        string? pickedPath = null;
        if (session is null
            && (string.IsNullOrWhiteSpace(LastRecordingPath) || !File.Exists(LastRecordingPath)))
        {
            var open = new OpenFileDialog
            {
                Filter = UpetReportFile.AnalysisOpenFilter,
                FilterIndex = 1,
                Title = "Selectați CSV sau .upet pentru raport HTML",
                InitialDirectory = GetWritableRecordingsDirectory()
            };
            if (open.ShowDialog() != true) return;
            pickedPath = open.FileName;
            if (UpetReportFile.HasReportExtension(pickedPath) ||
                UpetReportFile.LooksLikeUpetReport(pickedPath))
            {
                session = UpetReportFile.Load(pickedPath);
                ApplyAttachedMetaFromOffline(session);
            }
            else
                session = OfflineSession.FromCsv(pickedPath);
            _offline = session;
            RefreshAnalysisUi();
        }

        var dlg = new SaveFileDialog
        {
            Filter = "HTML|*.html",
            FileName = SuggestRaportFileName(".html"),
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        if (!TryBeginExport()) return;

        var dest = dlg.FileName;
        var lastCsv = LastRecordingPath;
        var metaHtml = CurrentProjectMeta();
        var existing = session;
        Status = "Export HTML în curs… interfața rămâne activă.";
        try
        {
            var status = await Task.Run(() =>
            {
                var s = existing ?? OfflineSession.FromCsv(lastCsv);
                CylinderContourExport.MergeFromSession(metaHtml, s);
                HtmlReportExporter.Export(
                    dest, s, metaHtml, s.Stats,
                    csvPath: Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                        lastCsv, s.SourcePath));
                string? contourNote = null;
                if (CylinderContourExport.ShouldAttempt(metaHtml, s))
                {
                    var probe = CylinderContourExport.TryCompute(s, metaHtml);
                    if (probe is { IsValid: true })
                        contourNote = " · contur cilindru inclus";
                    else
                        contourNote = " — " + (probe?.Error
                            ?? CylinderContourExport.DescribeSkipReason(metaHtml, s));
                }
                return
                    $"Raport HTML cu toate graficele în fișier (toată durata, {s.Timestamps.Count:N0} eșantioane)" +
                    (contourNote ?? "") +
                    $": {dest}";
            }).ConfigureAwait(true);
            Status = status;
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("HTML eșuat.");
            Status = "HTML eșuat: " + AppPaths.FriendlyIoMessage(ex, dest);
        }
        finally
        {
            EndExport();
        }
    }

    private async Task ExportLastOrPickAsync(string kind)
    {
        var csv = LastRecordingPath;
        if (string.IsNullOrWhiteSpace(csv) || !File.Exists(csv))
        {
            var open = new OpenFileDialog
            {
                Filter = UpetReportFile.AnalysisOpenFilter,
                FilterIndex = 1,
                Title = "Selectați CSV sau .upet pentru export",
                InitialDirectory = GetWritableRecordingsDirectory()
            };
            if (open.ShowDialog() != true) return;
            csv = open.FileName;
        }

        var (filter, ext) = kind switch
        {
            "xlsx" => ("Excel|*.xlsx", ".xlsx"),
            "txt" => ("TXT|*.txt", ".txt"),
            "mat" => ("MAT|*.mat", ".mat"),
            _ => ("DIAdem DAT|*.dat", ".dat")
        };
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = SuggestRaportFileName(ext),
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (dlg.ShowDialog() != true) return;
        if (!TryBeginExport()) return;

        var dest = dlg.FileName;
        var csvLocal = csv;
        var meta = CurrentProjectMeta();
        var fs = SampleRateHz > 0 ? SampleRateHz : 50;
        Status = kind == "xlsx"
            ? "Export Excel în curs… interfața rămâne activă."
            : $"Export {kind.ToUpperInvariant()} în curs…";
        try
        {
            var status = await Task.Run(() =>
            {
                var session = RecordingIndex.Load(csvLocal);
                CylinderContourExport.MergeFromSession(meta, session);
                if (kind == "xlsx")
                {
                    var result = ExcelReportExporter.Export(dest, session, meta, csvPath: csvLocal);
                    return FormatExcelExportStatus(result);
                }

                if (kind == "txt")
                {
                    AsciiExporter.ExportConfigurable(dest, session);
                    return $"TXT: {dest}{SaveFullGraphsBeside(session, dest, meta, fs, csvLocal)}";
                }

                if (kind == "mat")
                {
                    session.ExportMat(dest);
                    return $"MAT: {dest}{SaveFullGraphsBeside(session, dest, meta, fs, csvLocal)}";
                }

                DiaDemExporter.Export(dest, session);
                return $"DIAdem: {dest}{SaveFullGraphsBeside(session, dest, meta, fs, csvLocal)}";
            }).ConfigureAwait(true);
            Status = status;
            _journal.Info(Status);
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("Export eșuat.");
            Status = "Export eșuat: " + AppPaths.FriendlyIoMessage(ex, dest);
        }
        finally
        {
            EndExport();
        }
    }

    private bool TryBeginExport()
    {
        if (_exportBusy)
        {
            Status = "Export deja în curs — așteptați să se termine.";
            return false;
        }

        _exportBusy = true;
        _advisorExportStatus = AdvisorExportStatus.InProgress;
        _advisorLastExportError = null;
        try { RefreshLabAdvisorFromState(force: true); } catch { /* ignore */ }
        return true;
    }

    private void EndExport()
    {
        _exportBusy = false;
        if (_advisorExportStatus == AdvisorExportStatus.InProgress)
            _advisorExportStatus = AdvisorExportStatus.Succeeded;
        TrimLivePlotLoggers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        try { RefreshLabAdvisorFromState(force: true); } catch { /* ignore */ }
    }

    private string SaveFullGraphsBeside(
        OfflineSession session,
        string primaryPath,
        ProjectMeta? meta = null,
        int sampleRateHz = 0,
        string? csvPath = null)
    {
        try
        {
            meta ??= CurrentProjectMeta();
            var fs = sampleRateHz > 0 ? sampleRateHz : (SampleRateHz > 0 ? SampleRateHz : 50);
            csvPath ??= Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(
                LastRecordingPath, session.SourcePath);
            using var pack = ReportGraphPack.Build(session, fs, meta);
            var dir = pack.SaveBesideReport(primaryPath);
            // Companion HTML with all graphs embedded (TXT/MAT/DAT cannot hold images).
            var stem = Path.GetFileNameWithoutExtension(primaryPath);
            var htmlStem = stem.EndsWith("_" + ExperimentFileNaming.RoleRaport, StringComparison.OrdinalIgnoreCase)
                ? stem
                : stem + "_" + ExperimentFileNaming.RoleRaport;
            var htmlPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(primaryPath)) ?? ".",
                htmlStem + ".html");
            HtmlReportExporter.Export(
                htmlPath, session, meta, session.Stats, csvPath: csvPath);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dir))
                parts.Add($"PNG: {Path.GetFileName(dir)}");
            parts.Add($"HTML cu grafice: {Path.GetFileName(htmlPath)}");
            return " · " + string.Join(" · ", parts);
        }
        catch (Exception ex)
        {
            return " · (grafice: " + ex.Message + ")";
        }
    }

    private void WriteSpecimenToMeta(ProjectMeta meta)
    {
        if (!ExperimentTypes.IsCompression(meta.ExperimentType))
        {
            SpecimenIdentification.ClearFromMeta(meta);
            return;
        }

        meta.SpecimenId = SpecimenId ?? "";
        meta.SpecimenNameRo = SpecimenNameRo ?? "";
        meta.SpecimenClass = SpecimenClass ?? "";
        meta.SpecimenFormulaPack = SpecimenFormulaPack ?? "";
        meta.SpecimenFormulaPackLabel = SpecimenFormulaPackLabel ?? "";
        meta.SpecimenSummary = SpecimenSummary ?? "";
        meta.SpecimenStandardNote = SpecimenStandardNote ?? "";
        meta.SpecimenStrengthNotes = SpecimenStrengthNotes ?? "";
        meta.SpecimenShape = SpecimenShape ?? "";
        meta.SpecimenYoungGPa = SpecimenYoungGPa;
        meta.SpecimenPoissonNu = SpecimenPoissonNu;
        meta.SpecimenDensityKgM3 = SpecimenDensityKgM3;
        meta.SpecimenNotes = SpecimenNotes ?? "";
        if (string.IsNullOrWhiteSpace(meta.SpecimenSummary) &&
            !string.IsNullOrWhiteSpace(meta.SpecimenNameRo))
            meta.SpecimenSummary = SpecimenIdentification.BuildSummary(meta);
    }

    private void ReadSpecimenFromMeta(ProjectMeta m)
    {
        if (!ExperimentTypes.IsCompression(m.ExperimentType))
        {
            ClearSpecimenVm();
            return;
        }

        SpecimenId = m.SpecimenId ?? "";
        SpecimenNameRo = m.SpecimenNameRo ?? "";
        SpecimenClass = m.SpecimenClass ?? "";
        SpecimenFormulaPack = m.SpecimenFormulaPack ?? "";
        SpecimenFormulaPackLabel = string.IsNullOrWhiteSpace(m.SpecimenFormulaPackLabel)
            ? FormulaPacks.LabelRo(m.SpecimenFormulaPack)
            : m.SpecimenFormulaPackLabel;
        SpecimenSummary = m.SpecimenSummary ?? "";
        SpecimenStandardNote = m.SpecimenStandardNote ?? "";
        SpecimenStrengthNotes = m.SpecimenStrengthNotes ?? "";
        SpecimenShape = m.SpecimenShape ?? "";
        SpecimenYoungGPa = m.SpecimenYoungGPa;
        SpecimenPoissonNu = m.SpecimenPoissonNu;
        SpecimenDensityKgM3 = m.SpecimenDensityKgM3;
        SpecimenNotes = m.SpecimenNotes ?? "";
    }

    private void ClearSpecimenVm()
    {
        SpecimenId = "";
        SpecimenNameRo = "";
        SpecimenClass = "";
        SpecimenFormulaPack = "";
        SpecimenFormulaPackLabel = "";
        SpecimenSummary = "";
        SpecimenStandardNote = "";
        SpecimenStrengthNotes = "";
        SpecimenShape = "";
        SpecimenYoungGPa = 0;
        SpecimenPoissonNu = 0;
        SpecimenDensityKgM3 = 0;
        SpecimenNotes = "";
    }

    private void ApplySpecimenFromDialog(Controls.StartExperimentWindow dlg)
    {
        if (!ExperimentTypes.IsCompression(dlg.ExperimentTypeValue) || dlg.SelectedSpecimenCard is null)
        {
            ClearSpecimenVm();
            if (ExperimentTypes.IsCompression(dlg.ExperimentTypeValue))
            {
                try
                {
                    var lib = SpecimenLibrary.Load();
                    lib.RememberLast("");
                }
                catch { /* ignore prefs */ }
            }
            return;
        }

        var card = dlg.SelectedSpecimenCard;
        SpecimenId = card.Id ?? "";
        SpecimenNameRo = card.NameRo ?? "";
        SpecimenClass = card.Class ?? "";
        SpecimenFormulaPack = card.FormulaPack ?? "";
        SpecimenFormulaPackLabel = FormulaPacks.LabelRo(card.FormulaPack);
        SpecimenStandardNote = card.StandardNote ?? "";
        SpecimenStrengthNotes = card.StrengthNotes ?? "";
        SpecimenShape = string.IsNullOrWhiteSpace(card.Shape) ? SpecimenShapes.Cilindru : card.Shape;
        SpecimenYoungGPa = dlg.SpecimenYoungGPaValue > 0 ? dlg.SpecimenYoungGPaValue : card.EGPa;
        SpecimenPoissonNu = dlg.SpecimenPoissonNuValue > 0 ? dlg.SpecimenPoissonNuValue : card.Nu;
        SpecimenDensityKgM3 = card.DensityKgM3;
        SpecimenNotes = dlg.SpecimenNotesValue ?? card.Notes ?? "";
        SpecimenSummary = card.BuildSummaryLine();
        try
        {
            var lib = SpecimenLibrary.Load();
            lib.RememberLast(card.Id);
        }
        catch { /* ignore prefs */ }
    }

    private ProjectMeta CurrentProjectMeta()
    {
        var channelConfigs = Channels.Select(ToConfig).ToList();
        var reportChannels = ChannelReportBuilder.FromChannels(channelConfigs, _sensorLibrary.Sensors);
        var sensorSummary = reportChannels.Count > 0
            ? ChannelReportBuilder.BuildSensorSummary(reportChannels)
            : string.Join("; ", Channels
                .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.SensorName))
                .Select(c =>
                {
                    var id = string.IsNullOrWhiteSpace(c.SensorId) ? c.SensorName : c.SensorId;
                    return $"{c.Name}: {id} ({c.Unit}, scale={c.Scale:G4})";
                }));

        var calNotes = new List<string>();
        if (PistonAreaCm2 > 0 && LiveGauges.Any(g => g.DisplayMode == GaugeModePressureToKg))
            calNotes.Add(
                $"Presiune→masă UPET: A={PistonAreaCm2:0.##} cm² (implicit {PressureToMass.DefaultPistonAreaCm2:0.##} cm²); F[kg]=P[bar]·A·1e-4·10");
        if (Channels.Any(c => c.Enabled && c.Unit.Contains("µm/m", StringComparison.Ordinal)))
            calNotes.Add("Tensometrie: Zero software (F9), Scale/GF păstrate — fără TAR hardware.");
        if (Annotations.Count > 0)
            calNotes.Add("MARK: " + string.Join(" | ", Annotations.Take(12)));
        var metro = BuildMetrologyReportNotes();
        if (!string.IsNullOrWhiteSpace(metro))
            calNotes.Add(metro);

        var comment = Comment ?? "";
        if (DisplayFrozen)
            comment = (string.IsNullOrWhiteSpace(comment) ? "" : comment + " · ") + "Hold afișaj activ la export";

        var meta = new ProjectMeta
        {
            Operator = OperatorName,
            SampleId = SampleId,
            Comment = comment,
            Location = string.IsNullOrWhiteSpace(LabLocation)
                ? "Universitatea din Petroșani — Facultatea de Inginerie Mecanică și Electrică / Departamentul de Inginerie Mecanică, Industrială și Transporturi"
                : LabLocation,
            ProjectName = ProjectName,
            Backend = SelectedBackend,
            SampleRateHz = SampleRateHz,
            ExperimentName = SelectedExperiment?.Name ?? "",
            ExperimentType = ExperimentType,
            CylinderContour = ExperimentTypes.IsCylinderContour(ExperimentType) ? CylinderContour : null,
            PlannedSensors = PlannedSensors,
            ExperimentStartLocal = ExperimentStartedAt?.ToString("O") ?? "",
            ExperimentEndLocal = ExperimentEndedAt?.ToString("O") ?? "",
            EstimatedDurationMinutes = EstimatedDurationMinutes,
            SampleLengthMm = SampleLengthMm,
            SampleWidthMm = SampleWidthMm,
            SampleThicknessMm = SampleThicknessMm,
            SampleDiameterMm = SampleDiameterMm,
            SampleAreaMm2 = SampleAreaMm2,
            SampleMassG = SampleMassG,
            SampleDimensionsSummary = SampleDimensionsSummary ?? "",
            MontagePhotoPath = MontagePhotoPath ?? "",
            MontagePhotoAfterPath = MontagePhotoAfterPath ?? "",
            MontageBeforeNotes = MontageBeforeNotes ?? "",
            MontageAfterNotes = MontageAfterNotes ?? "",
            MontageBeforeCapturedLocal = MontageBeforeCapturedAt?.ToString("O") ?? "",
            MontageAfterCapturedLocal = MontageAfterCapturedAt?.ToString("O") ?? "",
            SensorSummary = sensorSummary,
            CalibrationNotes = string.Join(" · ", calNotes),
            DeviceEstHint = IsCleanEstForReport(DeviceErrorText) ? DeviceErrorText : "",
            ActiveChannelCount = reportChannels.Count,
            Channels = reportChannels,
            MeasurementFingerprint = MeasurementFingerprint ?? "",
            FingerprintAlgo = string.IsNullOrWhiteSpace(MeasurementFingerprint)
                ? ""
                : Core.Export.MeasurementFingerprint.Algorithm
        };
        WriteSpecimenToMeta(meta);
        WriteExperimentVideoToMeta(meta);
        SampleDimensions.ApplyComputedFields(meta);
        CylinderContourExport.EnsureConfig(
            meta,
            Math.Max(Channels.Count, reportChannels.Count),
            _offline ?? _viewerSession);
        if (meta.CylinderContour is not null)
            CylinderContour = meta.CylinderContour;
        SampleDimensionsSummary = meta.SampleDimensionsSummary;
        SampleAreaMm2 = meta.SampleAreaMm2;
        PublishStrainIndicatorsToMeta(meta);
        return meta;
    }

    private static bool IsCleanEstForReport(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint) || hint == "EST: —") return false;
        if (hint.Contains("Experiment ", StringComparison.OrdinalIgnoreCase)) return false;
        if (hint.Contains("foto montaj", StringComparison.OrdinalIgnoreCase)) return false;
        // Prefer real EST/LED device strings only.
        return hint.Contains("EST", StringComparison.OrdinalIgnoreCase)
               || hint.Contains("LED", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? TryParseLocalIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime();
        return null;
    }

    private void StartExperimentDialog()
    {
        EnsureSensorsLoadedForUi();
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        // NEW experiment each time Exp is pressed: empty before-photo in the dialog.
        // VM keeps last-session montage until Confirm (so you can export after Close first).
        var dlg = new Controls.StartExperimentWindow(
            ProjectName,
            OperatorName,
            SampleId,
            LabLocation,
            Comment,
            SampleRateHz,
            ExperimentType,
            SelectedExperiment,
            ExperimentPresets.ToList(),
            _sensorLibrary.Sensors.ToList(),
            montagePhotoPath: "",
            montageBeforeNotes: "",
            montageBeforeCapturedAt: null,
            sampleLengthMm: SampleLengthMm,
            sampleWidthMm: SampleWidthMm,
            sampleThicknessMm: SampleThicknessMm,
            sampleDiameterMm: SampleDiameterMm,
            sampleMassG: SampleMassG,
            initialContour: CylinderContour,
            channelCount: Math.Max(1, Math.Max(
                Channels.Count(c => c.Enabled && c.RecordEnabled),
                Channels.Count)),
            channelHints: Channels.Select((c, i) => new CylinderContourConfig.ChannelHint
            {
                Index = i,
                Unit = c.Unit ?? "",
                Name = string.IsNullOrWhiteSpace(c.Name) ? $"CH{i}" : c.Name,
                Enabled = c.Enabled,
                RecordEnabled = c.RecordEnabled
            }).ToList(),
            initialSpecimenId: SpecimenId,
            specimenYoungGPa: SpecimenYoungGPa,
            specimenPoissonNu: SpecimenPoissonNu,
            specimenNotes: SpecimenNotes,
            simulatorContourDemo: string.Equals(SelectedBackend, "Simulator", StringComparison.OrdinalIgnoreCase),
            usbCamera: _usbCamera)
        {
            Owner = owner
        };
        if (dlg.ShowDialog() != true || !dlg.Confirmed)
        {
            Status = "Start experiment anulat.";
            return;
        }

        ProjectName = string.IsNullOrWhiteSpace(dlg.ProjectNameValue) ? ProjectName : dlg.ProjectNameValue;
        OperatorName = dlg.OperatorValue;
        SampleId = dlg.SampleIdValue;
        SampleLengthMm = dlg.SampleLengthMmValue;
        SampleWidthMm = dlg.SampleWidthMmValue;
        SampleThicknessMm = dlg.SampleThicknessMmValue;
        SampleDiameterMm = dlg.SampleDiameterMmValue;
        SampleAreaMm2 = dlg.SampleAreaMm2Value;
        SampleMassG = dlg.SampleMassGValue;
        SampleDimensionsSummary = dlg.SampleDimensionsSummaryValue ?? "";
        ExperimentType = dlg.ExperimentTypeValue;
        ApplySpecimenFromDialog(dlg);
        CylinderContour = dlg.ContourConfigValue;
        _contourSimDemoEnabled = dlg.ContourSimDemoEnabled;
        _contourSimAssignment = null;
        if (ExperimentTypes.IsCylinderContour(ExperimentType) && CylinderContour is null)
        {
            var recN = Channels.Count(c => c.Enabled && c.RecordEnabled);
            if (recN <= 0) recN = Channels.Count;
            CylinderContour = CylinderContourConfig.CreateDefault(
                recN >= 9 ? 8 : 4,
                SampleDiameterMm,
                recN);
        }
        if (CylinderContour is not null && SampleDiameterMm > 0)
            CylinderContour.InitialRadiusMm = SampleDiameterMm / 2.0;
        LabLocation = dlg.LocationValue;
        Comment = dlg.CommentValue;
        var prevRate = SampleRateHz;
        SampleRateHz = dlg.SampleRateHzValue;
        if (IsStreaming && SampleRateHz != prevRate)
        {
            if (_device is not null) _device.SampleRateHz = SampleRateHz;
            foreach (var ch in Channels)
                ch.ChannelSampleRateHz = SampleRateHz;
            RebuildPlotSeries();
            RebuildAllChannelPlotSeries();
            ResetCatmanLiveAxis();
        }
        EstimatedDurationMinutes = dlg.EstimatedDurationMinutesValue;
        ExperimentStartedAt = dlg.ExperimentStartLocalValue;
        ExperimentEndedAt = null;
        MontagePhotoPath = PromoteMontagePhoto(
            dlg.MontagePhotoPathValue,
            SampleId,
            ExperimentStartedAt ?? DateTime.Now,
            ExperimentFileNaming.RoleBefore);
        MontageBeforeNotes = dlg.MontageBeforeNotesValue ?? "";
        MontageBeforeCapturedAt = dlg.MontageBeforeCapturedAtValue;
        MontagePhotoAfterPath = "";
        MontageAfterNotes = "";
        MontageAfterCapturedAt = null;
        ExperimentVideoEnabled = dlg.ExperimentVideoEnabledValue;
        ExperimentCameraId = dlg.ExperimentCameraIdValue ?? "";
        ExperimentCameraName = dlg.ExperimentCameraNameValue ?? "";
        ClearExperimentVideos();
        IsExperimentActive = true;
        if (dlg.SelectedPreset is not null)
            SelectedExperiment = dlg.SelectedPreset;

        PlannedSensors = dlg.PlannedSensors.Count == 0
            ? ""
            : string.Join("; ", dlg.PlannedSensors.Select(s =>
                string.IsNullOrWhiteSpace(s.Code) ? s.Name : $"{s.Code} ({s.Name})"));

        var applied = 0;
        if (dlg.ApplySensorsToChannels && dlg.PlannedSensors.Count > 0)
            applied = ApplyPlannedSensorsToChannels(dlg.PlannedSensors);
        ApplyContourSimDemoSetup();

        var photo = HasMontagePhoto ? " · montaj înainte" : "";
        var film = ExperimentVideoEnabled
            ? (string.IsNullOrWhiteSpace(ExperimentCameraName)
                ? " · film la Rec"
                : " · film " + ExperimentCameraName)
            : "";
        var dur = EstimatedDurationMinutes > 0 ? $" · durată est. {EstimatedDurationMinutes} min" : "";
        var dims = string.IsNullOrWhiteSpace(SampleDimensionsSummary) ? "" : " · " + SampleDimensionsSummary;
        var specimen = SpecimenIdentification.HasMeaningfulSpecimenText(SpecimenSummary)
            ? " · " + SpecimenSummary
            : "";
        var contour = CylinderContour is not null ? " · " + CylinderContour.ToStatusSummary() : "";
        var simAssign = _contourSimDemoEnabled
            ? " · simulator: la Start, 4×8 mm + 1×1 mm + 3×3 mm aleatoriu (10 s)"
            : "";
        Status =
            $"Experiment pornit: {ProjectName} · {ExperimentType} · {OperatorName} · {SampleId} · " +
            $"{ExperimentStartedAt:yyyy-MM-dd HH:mm:ss}{dur}{dims}{specimen}{contour}{simAssign}{photo}{film}" +
            (applied > 0 ? $" · senzori aplicați pe {applied} canal(e)" : "");
        _journal.Info(Status);
        HelpPanelText =
            ExperimentTypes.IsCylinderContour(ExperimentType)
                ? (_contourSimDemoEnabled
                    ? "Simulator contur 8 senzori: Connect (dacă e nevoie) → Start → Record. " +
                      "La fiecare Start se trag aleatoriu 4 senzori → 8 mm, 1 → 1 mm, 3 → 3 mm. " +
                      "Rampă 0→țintă u [mm] în exact 10 s, apoi stop înregistrare. L0/Ø rămân cele setate. Repartizarea apare după Start."
                    : "Contur cilindru: la export Excel/PDF apare schema industrială (plan, elevație, secțiune, Contur la Fmax, u_max, tabel u_i, mini F-cursă). " +
                      "Index: forță → Cursor B → |cursă| (fără forță: index = max cursă) → media |u|.")
                : "Start experiment: dimensiuni / greutate / poza montaj sunt opționale. Film USB (sub poză) pornește automat la Rec. La «Închide» puteți adăuga (tot opțional) poza după + observații.";
        PushSimulatorScenarioHint();
        try
        {
            RequestAdvisorPeerRefresh(force: true);
            RefreshLabAdvisorFromState(force: true);
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Simulator Contur 8 senzori: mm + Rec pe S1…S8, durată Record 10 s.
    /// Does not overwrite specimen L0/Ø.
    /// </summary>
    private void ApplyContourSimDemoSetup()
    {
        if (!_contourSimDemoEnabled
            || !ExperimentTypes.IsCylinderContour(ExperimentType)
            || CylinderContour is not { } cfg
            || cfg.SensorCount != 8
            || !string.Equals(SelectedBackend, "Simulator", StringComparison.OrdinalIgnoreCase))
            return;

        cfg.EnsureShape(8);
        var n = Math.Min(8, cfg.SensorChannelIndices.Count);
        for (var i = 0; i < n; i++)
        {
            var idx = cfg.SensorChannelIndices[i];
            if (idx < 0 || idx >= Channels.Count) continue;
            var ch = Channels[idx];
            ch.Unit = "mm";
            ch.Enabled = true;
            ch.RecordEnabled = true;
            ch.ShowOnPlot = true;
        }

        RecordStopMode = "Duration";
        MaxSeconds = 10;
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: IsStreaming);
    }

    private void CloseExperiment()
    {
        if (!IsExperimentActive)
        {
            Status = "Niciun experiment activ de închis.";
            return;
        }

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var dlg = new Controls.CloseExperimentWindow(
            ProjectName,
            OperatorName,
            SampleId,
            ExperimentStartedAt,
            MontagePhotoPath,
            MontagePhotoAfterPath,
            MontageAfterNotes,
            MontageAfterCapturedAt,
            GetWritableRecordingsDirectory())
        {
            Owner = owner
        };
        if (dlg.ShowDialog() != true || !dlg.Confirmed)
        {
            Status = "Închidere experiment anulată.";
            return;
        }

        MontagePhotoAfterPath = PromoteMontagePhoto(
            dlg.MontagePhotoAfterPathValue,
            SampleId,
            ExperimentStartedAt ?? DateTime.Now,
            ExperimentFileNaming.RoleAfter);
        MontageAfterNotes = dlg.MontageAfterNotesValue ?? "";
        MontageAfterCapturedAt = dlg.MontageAfterCapturedAtValue;
        ExperimentEndedAt = DateTime.Now;
        IsExperimentActive = false;
        var realMin = ExperimentStartedAt is { } start
            ? Math.Max(0, (ExperimentEndedAt.Value - start).TotalMinutes)
            : 0;
        Status =
            $"Experiment închis: {ProjectName} · {OperatorName} · {SampleId} · " +
            $"stop {ExperimentEndedAt:yyyy-MM-dd HH:mm:ss}" +
            (realMin > 0 ? $" · durată reală {realMin:0.#} min" : "") +
            (HasMontagePhoto ? " · montaj înainte" : "") +
            (HasMontagePhotoAfter ? " · probă după" : "") +
            (!string.IsNullOrWhiteSpace(MontageAfterNotes) ? " · observații" : "") +
            (_experimentVideoFiles.Count > 0 ? $" · {_experimentVideoFiles.Count} clip(uri)" : "");
        _journal.Info(Status);
        HelpPanelText =
            "Experiment închis. Pozele rămân disponibile pentru export; Exp pornește un experiment nou.";
    }

    /// <summary>Apply planned sensors onto Enabled channels in order (CH index).</summary>
    private int ApplyPlannedSensorsToChannels(IReadOnlyList<SensorDefinition> sensors)
    {
        var targets = Channels.Where(c => c.Enabled).OrderBy(c => c.Index).ToList();
        if (targets.Count == 0) return 0;
        var n = Math.Min(targets.Count, sensors.Count);
        _suppressChannelConfigPush = true;
        try
        {
            for (var i = 0; i < n; i++)
            {
                var ch = targets[i];
                var sensor = sensors[i];
                var cfg = ToConfig(ch);
                SensorApplyHelper.Apply(cfg, sensor, renameForMultiApply: n > 1);
                ch.Name = cfg.Name;
                ch.Unit = cfg.Unit;
                ch.Scale = cfg.Scale;
                ch.Offset = cfg.Offset;
                ch.TareValue = cfg.TareValue;
                ch.SensorId = cfg.SensorId;
                ch.SensorName = cfg.SensorName;
                ch.SensorCategory = cfg.SensorCategory;
                ch.Capacity = cfg.Capacity;
                ch.ExcitationV = cfg.ExcitationV;
                if (sensor.ShuntKohm >= ShuntCheck.MinShuntKohm)
                    ch.ShuntKohm = sensor.ShuntKohm;
                ch.FilterHz = cfg.FilterHz;
                ch.ChannelSampleRateHz = cfg.ChannelSampleRateHz;
                ch.RangeMvPerV = cfg.RangeMvPerV;
                ch.Bridge = cfg.Bridge.ToString();
                ch.AlarmEnabled = cfg.AlarmEnabled;
                ch.AlarmLow = cfg.AlarmLow;
                ch.AlarmHigh = cfg.AlarmHigh;
                ch.Enabled = true;
                ch.ShowOnPlot = true;
                ch.RecordEnabled = true;
            }
        }
        finally
        {
            _suppressChannelConfigPush = false;
        }

        RebuildPlotSeries();
        RefreshLiveValueHeaders();
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: IsStreaming);
        return n;
    }

    private void RefreshAnalysisUi()
    {
        AnalysisStats.Clear();
        if (_offline is null) return;
        foreach (var s in _offline.Stats)
            AnalysisStats.Add(new StatsRow
            {
                Name = s.Name, Count = s.Count, Min = Format(s.Min), Max = Format(s.Max),
                Mean = Format(s.Mean), StdDev = Format(s.StdDev), Rms = Format(s.Rms),
                PeakToPeak = Format(s.PeakToPeak), Crest = Format(s.CrestFactor)
            });
        CursorA = _offline.CursorA;
        CursorB = _offline.CursorB;
        CutEnd = Math.Max(CutEnd, _offline.Timestamps.Count);
        AnalysisSummary = $"{_offline.Timestamps.Count} samples / {_offline.ChannelNames.Count} channels";
        UpdateDeltaText();
        ResetAnalysisPlayheadForNewSession();
        RefreshAnalysisPlot();
        BindAnalysisVideoFromSession();
    }

    private void SyncCursorsToOffline()
    {
        if (_offline is null) return;
        _offline.CursorA = CursorA;
        _offline.CursorB = CursorB;
        UpdateDeltaText();
        RefreshAnalysisPlot();
    }

    private void UpdateDeltaText()
    {
        if (_offline is null) { DeltaText = "Δt=—  Δy=—"; return; }
        DeltaText = $"Δt={_offline.DeltaTSeconds():0.####}s  Δy={_offline.DeltaY(AnalysisChannel):0.####}";
    }

    private void RefreshAnalysisPlot(IReadOnlyList<int>? peaks = null)
    {
        if (_plotAnalysis is null || _offline is null) return;
        _analysisRevealSeries.Clear();
        _plotAnalysis.Plot.Clear();
        _crosshairA = _plotAnalysis.Plot.Add.Crosshair(CursorA, 0);
        _crosshairB = _plotAnalysis.Plot.Add.Crosshair(CursorB, 0);
        _crosshairA.LineColor = ScottPlot.Color.FromHex("#0072C6");
        _crosshairB.LineColor = ScottPlot.Color.FromHex("#C87000");
        _crosshairA.LineWidth = 1.4f;
        _crosshairB.LineWidth = 1.4f;
        ApplyEngineeringPlotStyle(_plotAnalysis.Plot, "Analiză offline", "index / t", "Y");
        var colors = new[] { ScottPlot.Colors.SteelBlue, ScottPlot.Colors.Orange, ScottPlot.Colors.SeaGreen, ScottPlot.Colors.Crimson };
        for (var c = 0; c < _offline.Columns.Count; c++)
        {
            var xs = Enumerable.Range(0, _offline.Columns[c].Length).Select(i => (double)i).ToArray();
            var sig = _plotAnalysis.Plot.Add.Scatter(xs, _offline.Columns[c]);
            sig.LegendText = _offline.ChannelNames[c];
            sig.Color = colors[c % colors.Length];
            sig.MarkerSize = 0;
            _analysisRevealSeries.Add(sig);
        }
        if (peaks is { Count: > 0 } && AnalysisChannel < _offline.Columns.Count)
        {
            var xs = peaks.Select(i => (double)i).ToArray();
            var ys = peaks.Select(i => _offline.Columns[AnalysisChannel][i]).ToArray();
            var m = _plotAnalysis.Plot.Add.Scatter(xs, ys);
            m.Color = ScottPlot.Colors.Red; m.LineWidth = 0; m.MarkerSize = 8; m.LegendText = "Peaks";
        }
        foreach (var ann in Annotations)
        {
            // format "@idx  ..."
            var sp = ann.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (sp.Length > 0 && sp[0].StartsWith('@') && int.TryParse(sp[0].AsSpan(1), out var ai)
                && AnalysisChannel < _offline.Columns.Count && ai >= 0 && ai < _offline.Columns[AnalysisChannel].Length)
            {
                var txt = _plotAnalysis.Plot.Add.Text(sp.Length > 1 ? sp[1] : "•", ai, _offline.Columns[AnalysisChannel][ai]);
                txt.LabelFontColor = ScottPlot.Colors.DarkRed;
                txt.LabelFontSize = 11;
            }
        }
        ApplyUnloadMarkersToAnalysisPlot();
        _plotAnalysis.Plot.ShowLegend();
        var fullLast = Math.Max(0, _offline.Timestamps.Count - 1);
        foreach (var s in _analysisRevealSeries)
        {
            try { s.MaxRenderIndex = fullLast; }
            catch { /* ignore */ }
        }
        // Scale to the full recording first, then clip the trace to the playhead
        // so the red line travels across a fixed time axis instead of shrinking it.
        _plotAnalysis.Plot.Axes.AutoScale();
        var limits = _plotAnalysis.Plot.Axes.GetLimits();
        ResetAnalysisPlayheadAfterPlotClear();
        _plotAnalysis.Plot.Axes.SetLimits(limits);
        _plotAnalysis.Refresh();
    }

    private void ExportCsvToExcel(string csvPath, string xlsxPath) =>
        ExcelReportExporter.ExportFromCsv(csvPath, xlsxPath, CurrentProjectMeta());

    private static string FormatExcelExportStatus(ExcelExportResult r)
    {
        var baseMsg =
            $"Excel cu grafice în fișier (durată completă): {r.SampleCount:N0} eșantioane, " +
            $"{r.ActiveChannelCount} canale, {r.ChartCount} grafice" +
            (r.ContourIncluded ? " · foaie Contur (prima)" : "") +
            $" → {r.Path}";
        if (!r.ContourIncluded && !string.IsNullOrWhiteSpace(r.ContourSkipReasonRo))
        {
            // Tensometrie / other types: no Contur sheet — don't tell the operator to open one.
            if (r.ContourSkipReasonRo.Contains("nu include foaia Contur", StringComparison.OrdinalIgnoreCase))
                return baseMsg;
            return baseMsg + " — " + r.ContourSkipReasonRo;
        }
        return baseMsg;
    }
}
