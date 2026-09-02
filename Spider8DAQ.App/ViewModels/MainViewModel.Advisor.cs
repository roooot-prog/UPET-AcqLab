using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Advisory;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.App.ViewModels;

public sealed class AdviceRow
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Suggestion { get; init; } = "";
    public string StepsText { get; init; } = "";
    public string SeverityLabel { get; init; } = "";
    public string? ActionId { get; init; }
    public string? ActionLabel { get; init; }
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionId);
}

/// <summary>
/// Lab advisor + local learning: suggestions on errors; AI ask box uses the same rule engine.
/// Does NOT auto-rewrite application code — learns which tips users mark as useful.
/// </summary>
public partial class MainViewModel
{
    private AdvisorMemory? _advisorMemory;
    private bool _showAdvisorPanel = true;
    /// <summary>User pressed Ascunde — do not auto-reopen on status/errors until Asistent lab.</summary>
    private bool _advisorUserHidden;
    private string _advisorAskText = "";
    private string _advisorLearningSummary = "";
    private string _advisorBannerTitle = "";
    private string _primaryAdviceId = "";
    private bool _advisorStatusGuard;
    private DispatcherTimer? _advisorTimer;
    private DateTime _lastAdvisorRefreshUtc = DateTime.MinValue;
    private string _lastAdvisorFingerprint = "";
    private AdvisorExportStatus _advisorExportStatus;
    private string? _advisorLastExportError;
    private ExperimentSummary? _advisorCurrent;
    private IReadOnlyList<ExperimentSummary> _advisorPeers = Array.Empty<ExperimentSummary>();
    private bool _advisorPeerScanComplete;
    private DateTime _advisorPeerScanUtc = DateTime.MinValue;
    private string? _advisorPeerScanType;
    private int _advisorPeerScanBusy;
    private CylinderContourResult? _advisorContourCache;
    private string? _advisorContourCacheKey;
    private static readonly JsonSerializerOptions AdvisorPrefsJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ObservableCollection<AdviceRow> AdvisorSuggestions { get; } = new();
    public ObservableCollection<string> AdvisorQaHistory { get; } = new();

    public ICommand AdvisorAskCommand { get; private set; } = null!;
    public ICommand AdvisorFeedbackHelpfulCommand { get; private set; } = null!;
    public ICommand AdvisorFeedbackNotHelpfulCommand { get; private set; } = null!;
    public ICommand ToggleAdvisorPanelCommand { get; private set; } = null!;
    public ICommand ShowAdvisorPanelCommand { get; private set; } = null!;
    public ICommand ClearAdvisorSuggestionsCommand { get; private set; } = null!;
    public ICommand AdvisorQuickActionCommand { get; private set; } = null!;

    public bool ShowAdvisorPanel
    {
        get => _showAdvisorPanel;
        set
        {
            if (_showAdvisorPanel == value) return;
            _showAdvisorPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AdvisorStatusHint));
            OnPropertyChanged(nameof(ShowAdvisorStatusHint));
            PersistAdvisorPanelPref(value);
        }
    }

    public bool HasAdvisorSuggestions => AdvisorSuggestions.Count > 0;

    public bool HasAdvisorQaHistory => AdvisorQaHistory.Count > 0;

    public bool ShowAdvisorStatusHint => !ShowAdvisorPanel && HasAdvisorSuggestions;

    public string AdvisorStatusHint =>
        ShowAdvisorStatusHint
            ? $"Asistent: {AdvisorSuggestions.Count} sugestii — click Asistent lab"
            : "";

    public string AdvisorAskText
    {
        get => _advisorAskText;
        set { _advisorAskText = value; OnPropertyChanged(); }
    }

    public string AdvisorLearningSummary
    {
        get => _advisorLearningSummary;
        set { _advisorLearningSummary = value; OnPropertyChanged(); }
    }

    public string AdvisorBannerTitle
    {
        get => _advisorBannerTitle;
        set { _advisorBannerTitle = value; OnPropertyChanged(); }
    }

    public string AdvisorModuleHint =>
        "Asistent lab: analizează experimentul (tip, Zero, canale, export) și compară cu ultimele teste " +
        "de același tip din recordings. Utilă/Nu. Nu pornește Record/Stop singur.";

    internal void WireAdvisorCommands()
    {
        try
        {
            AppPaths.EnsureWritable(AppPaths.Data);
            _advisorMemory = new AdvisorMemory(AppPaths.AdvisorMemory);
            AdvisorLearningSummary = _advisorMemory.SummarizeLearning();
            _showAdvisorPanel = LoadAdvisorPanelPref();
            _advisorUserHidden = !_showAdvisorPanel;
            OnPropertyChanged(nameof(ShowAdvisorPanel));
        }
        catch
        {
            _advisorMemory = null;
            AdvisorLearningSummary = "Memorie advisor indisponibilă.";
        }

        AdvisorAskCommand = new RelayCommand(() => AskAdvisor(AdvisorAskText));
        AdvisorFeedbackHelpfulCommand = new RelayCommand(() => FeedbackPrimary(true));
        AdvisorFeedbackNotHelpfulCommand = new RelayCommand(() => FeedbackPrimary(false));
        ToggleAdvisorPanelCommand = new RelayCommand(() =>
        {
            if (ShowAdvisorPanel)
            {
                _advisorUserHidden = true;
                ShowAdvisorPanel = false;
                SetAdvisorStatus("Asistent lab ascuns — redeschideți cu butonul «Asistent lab» din bara de sus.");
            }
            else
            {
                _advisorUserHidden = false;
                ShowAdvisorPanel = true;
                SetAdvisorStatus("Asistent lab afișat.");
            }
        });
        ShowAdvisorPanelCommand = new RelayCommand(() =>
        {
            _advisorUserHidden = false;
            ShowAdvisorPanel = true;
            RequestAdvisorPeerRefresh(force: false);
            SetAdvisorStatus(HasAdvisorSuggestions
                ? $"Asistent lab afișat — {AdvisorSuggestions.Count} sugestii."
                : "Asistent lab afișat — scrieți o întrebare sau provocați o eroare (Connect/Start).");
        });
        ClearAdvisorSuggestionsCommand = new RelayCommand(() =>
        {
            AdvisorSuggestions.Clear();
            AdvisorBannerTitle = "";
            _primaryAdviceId = "";
            NotifyAdvisorSuggestionProps();
        });
        AdvisorQuickActionCommand = new RelayCommand(p => RunAdvisorQuickAction(p as string));

        _advisorTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _advisorTimer.Tick += (_, _) =>
        {
            try
            {
                if (IsStreaming || IsRecording || _advisorExportStatus == AdvisorExportStatus.InProgress)
                    RefreshLabAdvisorFromState(force: false);
            }
            catch { /* advisor must never break UI */ }
        };
        _advisorTimer.Start();
    }

    /// <summary>Called from Status setter — auto-suggest on errors / warnings, with live context.</summary>
    internal void RefreshLabAdvisorFromStatus(string? status)
    {
        if (_advisorStatusGuard) return;
        if (string.IsNullOrWhiteSpace(status)) return;
        if (IsAdvisorMetaStatus(status)) return;
        var problem = LabAdvisor.LooksLikeProblem(status);
        if (!problem && status.Length < 40) return;
        RefreshLabAdvisorCore(status, force: problem);
    }

    /// <summary>Re-analyze the current experiment (throttled). Safe to call from connect/record/load/timer.</summary>
    internal void RefreshLabAdvisorFromState(bool force = false)
    {
        if (_advisorStatusGuard && !force) return;
        RefreshLabAdvisorCore(Status, force);
    }

    private void RefreshLabAdvisorCore(string? status, bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastAdvisorRefreshUtc).TotalSeconds < 2.8)
            return;
        _lastAdvisorRefreshUtc = now;
        try
        {
            var items = LabAdvisor.Advise(status, _advisorMemory, BuildAdviceContext(), max: 3);
            ApplyAdvice(items, auto: true, source: status ?? "");
        }
        catch { /* advisor must never break Status */ }
    }

    private AdviceContext BuildAdviceContext()
    {
        var on = 0;
        var hasSensor = false;
        var hasGf = false;
        var hasNan = false;
        try
        {
            if (Channels is not null)
            {
                foreach (var c in Channels)
                {
                    if (!c.Enabled) continue;
                    on++;
                    if (!string.IsNullOrWhiteSpace(c.SensorName)) hasSensor = true;
                    if (StrainScale.IsStrainUnit(c.Unit) && Math.Abs(c.Scale) >= 10) hasGf = true;
                    if (string.Equals(c.SensorCategory, global::Spider8DAQ.Core.Sensors.SensorCategories.Strain, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(c.Scale) >= 10)
                        hasGf = true;
                    if (string.Equals(c.LiveReading, "NaN", StringComparison.OrdinalIgnoreCase))
                        hasNan = true;
                }
            }
        }
        catch { /* init race */ }

        var findings = new List<AdviceSignalFinding>();
        var hasClip = false;
        var hasFlat = false;
        string? clipHint = null;
        string? flatHint = null;
        try
        {
            foreach (var f in LiveMistakeFindings.Take(4))
            {
                findings.Add(new AdviceSignalFinding
                {
                    Kind = f.DefectId ?? "",
                    ChannelHint = f.ChannelHint ?? "",
                    Message = f.Message ?? ""
                });
                if (f.DefectId is "saturare")
                {
                    hasClip = true;
                    clipHint ??= f.ChannelHint;
                }
                if (f.DefectId is "semnal-plat" or "cablu-desprins")
                {
                    hasFlat = true;
                    flatHint ??= f.ChannelHint;
                }
            }
        }
        catch { /* collection race */ }

        double? oval = null, barrel = null, umax = null, umean = null;
        string? flatRadial = null;
        var contour = ExperimentTypes.IsCylinderContour(ExperimentType);
        if (contour && _offline is { Timestamps.Count: > 8 })
        {
            try
            {
                var key = (_offline.SourcePath ?? "") + "|" + _offline.Timestamps.Count + "|" + SampleDiameterMm;
                if (key != _advisorContourCacheKey)
                {
                    _advisorContourCacheKey = key;
                    var meta = CurrentProjectMeta();
                    _advisorContourCache = CylinderContourExport.ShouldAttempt(meta, _offline)
                        ? CylinderContourExport.TryCompute(_offline, meta)
                        : null;
                }
                var r = _advisorContourCache;
                if (r is { IsValid: true })
                {
                    if (double.IsFinite(r.OvalityMm)) oval = r.OvalityMm;
                    if (double.IsFinite(r.BarrelingIndex)) barrel = r.BarrelingIndex;
                    if (double.IsFinite(r.UMaxMm)) umax = r.UMaxMm;
                    if (double.IsFinite(r.UMeanMm)) umean = r.UMeanMm;
                    flatRadial = r.FlatSensorWarningRo;
                }
            }
            catch { /* contour compute is optional */ }
        }

        ExperimentSummary? current = _advisorCurrent;
        try
        {
            if (_offline is { Timestamps.Count: > 0 })
                current = ExperimentPeerCatalog.SummarizeSession(_offline, ExperimentType, CurrentProjectMeta());
        }
        catch { /* keep cached */ }

        double? poisson = null;
        if (double.IsFinite(LivePoissonNu)) poisson = LivePoissonNu;
        else if (double.IsFinite(_reportPoissonNu)) poisson = _reportPoissonNu;

        var mapped = false;
        var angles = false;
        try
        {
            if (CylinderContour is { } cfg)
            {
                mapped = cfg.IsConfigured;
                angles = cfg.SensorAnglesDeg.Count >= cfg.SensorCount && cfg.SensorCount >= 4;
            }
        }
        catch { /* ignore */ }

        return new AdviceContext
        {
            IsConnected = IsConnected,
            IsStreaming = IsStreaming,
            IsRecording = IsRecording,
            IsZeroed = _sessionZeroed,
            HasAppliedSensor = hasSensor,
            ChannelsOn = on,
            SampleRateHz = SampleRateHz,
            SelectedSensor = SelectedSensorName ?? SelectedSensorRow?.Name ?? SelectedSensorRow?.Code,
            ExperimentType = ExperimentType,
            ExportStatus = _advisorExportStatus,
            LastExportError = _advisorLastExportError,
            HasClipping = hasClip,
            HasOverload = hasClip,
            HasFlatChannel = hasFlat,
            HasNan = hasNan,
            ClipChannelHint = clipHint,
            FlatChannelHint = flatHint,
            Findings = findings,
            SampleDiameterMm = SampleDiameterMm,
            SampleLengthMm = SampleLengthMm,
            ContourMapped = mapped,
            ContourAnglesSet = angles,
            OvalityMm = oval,
            BarrelingIndex = barrel,
            UMaxMm = umax,
            UMeanMm = umean,
            FlatRadialWarning = flatRadial,
            HasGaugeFactorScale = hasGf,
            PoissonNu = poisson,
            HasUnloadMarkers = HasUnloadMarkers,
            HasOfflineSession = _offline is { Timestamps.Count: > 0 },
            OfflineSampleCount = _offline?.Timestamps.Count ?? 0,
            Current = current,
            Peers = _advisorPeers,
            PeerScanComplete = _advisorPeerScanComplete,
            SpecimenName = ExperimentTypes.IsCompression(ExperimentType) ? SpecimenNameRo : null,
            SpecimenClass = ExperimentTypes.IsCompression(ExperimentType) ? SpecimenClass : null,
            SpecimenFormulaPack = ExperimentTypes.IsCompression(ExperimentType) ? SpecimenFormulaPack : null
        };
    }

    /// <summary>Background scan of recordings folder for same-type peers (does not block UI).</summary>
    internal void RequestAdvisorPeerRefresh(bool force = false)
    {
        var type = ExperimentType ?? "";
        if (string.IsNullOrWhiteSpace(type)) return;
        if (!force
            && type == _advisorPeerScanType
            && (DateTime.UtcNow - _advisorPeerScanUtc).TotalSeconds < 12)
            return;
        if (System.Threading.Interlocked.CompareExchange(ref _advisorPeerScanBusy, 1, 0) != 0)
            return;

        OfflineSession? session = null;
        ProjectMeta? meta = null;
        try
        {
            session = _offline;
            meta = CurrentProjectMeta();
        }
        catch { /* ignore */ }

        var folder = GetWritableRecordingsDirectory();
        var exclude = session?.SourcePath ?? LastRecordingPath;
        var typeCopy = type;

        _ = Task.Run(() =>
        {
            ExperimentSummary? cur = null;
            try
            {
                if (session is { Timestamps.Count: > 0 })
                    cur = ExperimentPeerCatalog.SummarizeSession(session, typeCopy, meta);
                else if (!string.IsNullOrWhiteSpace(exclude) && File.Exists(exclude)
                         && exclude.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    cur = ExperimentPeerCatalog.SummarizeFile(exclude, typeCopy, computeMetrics: true);

                var peers = ExperimentPeerCatalog.FindPeers(folder, typeCopy, exclude, maxPeers: 3);
                return (cur, peers);
            }
            catch
            {
                return (cur, Array.Empty<ExperimentSummary>() as IReadOnlyList<ExperimentSummary>);
            }
        }).ContinueWith(t =>
        {
            void Apply()
            {
                System.Threading.Interlocked.Exchange(ref _advisorPeerScanBusy, 0);
                if (t.Status != TaskStatus.RanToCompletion) return;
                var (cur, peers) = t.Result;
                _advisorCurrent = cur;
                _advisorPeers = peers ?? Array.Empty<ExperimentSummary>();
                _advisorPeerScanComplete = true;
                _advisorPeerScanUtc = DateTime.UtcNow;
                _advisorPeerScanType = typeCopy;
                RefreshLabAdvisorFromState(force: true);
            }

            try
            {
                if (_dispatcher.CheckAccess()) Apply();
                else _dispatcher.BeginInvoke(Apply);
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref _advisorPeerScanBusy, 0);
            }
        });
    }

    private void AskAdvisor(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            SetAdvisorStatus("Scrieți o întrebare (ex: de ce nu primesc date de pe P15?).");
            _advisorUserHidden = false;
            ShowAdvisorPanel = true;
            return;
        }

        var q = question.Trim();
        var items = LabAdvisor.Advise(q, _advisorMemory, BuildAdviceContext(), max: 3);
        ApplyAdvice(items, auto: false, source: q);
        _advisorUserHidden = false;
        ShowAdvisorPanel = true;

        var answer = AdvisorSuggestions.Count == 0
            ? "Nicio regulă potrivită — încercați Preflight F10 sau Ghid DB15."
            : AdvisorSuggestions[0].Title + " — " + AdvisorSuggestions[0].Suggestion;
        PushQaHistory(q, answer);

        if (AdvisorSuggestions.Count == 0)
            SetAdvisorStatus("Asistent: nicio regulă potrivită — încercați Preflight F10 sau Ghid DB15.");
        else
            SetAdvisorStatus("Asistent: " + AdvisorSuggestions[0].Title);
        AdvisorLearningSummary = _advisorMemory?.SummarizeLearning() ?? AdvisorLearningSummary;
        AdvisorAskText = "";
    }

    private void PushQaHistory(string question, string answer)
    {
        var line = $"Q: {Trunc(question, 80)}\nA: {Trunc(answer, 160)}";
        AdvisorQaHistory.Insert(0, line);
        while (AdvisorQaHistory.Count > 8)
            AdvisorQaHistory.RemoveAt(AdvisorQaHistory.Count - 1);
        OnPropertyChanged(nameof(HasAdvisorQaHistory));
    }

    private void ApplyAdvice(IReadOnlyList<AdviceItem> items, bool auto, string source)
    {
        AdvisorSuggestions.Clear();
        _primaryAdviceId = "";
        if (items.Count == 0)
        {
            AdvisorBannerTitle = "";
            _lastAdvisorFingerprint = "";
            NotifyAdvisorSuggestionProps();
            return;
        }

        var fp = string.Join("|", items.Select(i => i.Id));
        if (auto && fp == _lastAdvisorFingerprint && AdvisorSuggestions.Count == items.Count)
            return;
        _lastAdvisorFingerprint = fp;

        foreach (var a in items)
        {
            AdvisorSuggestions.Add(new AdviceRow
            {
                Id = a.Id,
                Title = a.Title,
                Suggestion = a.Suggestion,
                SeverityLabel = a.Severity.ToString(),
                StepsText = string.Join(Environment.NewLine, a.Steps.Select((s, i) => $"{i + 1}. {s}")),
                ActionId = a.ActionId,
                ActionLabel = a.ActionLabel
            });
            _advisorMemory?.RecordShown(a.Id, source);
        }

        _primaryAdviceId = items[0].Id;
        AdvisorBannerTitle = (auto ? "Sugestie: " : "Asistent: ") + items[0].Title;
        HelpPanelText = items[0].Suggestion + Environment.NewLine +
                        string.Join(" · ", items[0].Steps.Take(3));

        // Suggestions/hints update in background; Ascunde stays until «Asistent lab».
        NotifyAdvisorSuggestionProps();
        AdvisorLearningSummary = _advisorMemory?.SummarizeLearning() ?? AdvisorLearningSummary;
    }

    private void RunAdvisorQuickAction(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;
        _advisorUserHidden = false;
        ShowAdvisorPanel = true;
        try
        {
            switch (actionId.Trim().ToLowerInvariant())
            {
                case "connect":
                    if (ConnectCommand?.CanExecute(null) == true) ConnectCommand.Execute(null);
                    else SetAdvisorStatus("Connect indisponibil (deja conectat sau în curs).");
                    break;
                case "preflight":
                    PreflightCommand?.Execute(null);
                    break;
                case "db15":
                    ShowDb15WiringGuideCommand?.Execute(null);
                    break;
                case "first-measure":
                    FirstMeasureWizardCommand?.Execute(null);
                    break;
                case "recordings":
                    OpenCsvFolderCommand?.Execute(null);
                    break;
                case "start":
                    if (StartCommand?.CanExecute(null) == true) StartCommand.Execute(null);
                    else SetAdvisorStatus("Start indisponibil — Connect mai întâi, sau deja în stream.");
                    break;
                case "shunt":
                    if (ShuntSelectedCommand?.CanExecute(null) == true) ShuntSelectedCommand.Execute(null);
                    else SetAdvisorStatus("Shunt necesită Connect + canal selectat.");
                    break;
                case "invert":
                    InvertSelectedChannelPolarityCommand?.Execute(null);
                    break;
                case "new-experiment":
                    StartExperimentCommand?.Execute(null);
                    break;
                case "zero":
                    if (TareAllCommand?.CanExecute(null) == true) TareAllCommand.Execute(null);
                    else SetAdvisorStatus("Zero necesită Connect.");
                    break;
                default:
                    SetAdvisorStatus("Acțiune necunoscută: " + actionId);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetAdvisorStatus("Acțiune asistent: " + ex.Message);
        }
    }

    private void FeedbackPrimary(bool helpful)
    {
        if (string.IsNullOrEmpty(_primaryAdviceId))
        {
            SetAdvisorStatus("Nicio sugestie activă pentru feedback.");
            return;
        }

        _advisorMemory?.RecordFeedback(_primaryAdviceId, helpful);
        AdvisorLearningSummary = _advisorMemory?.SummarizeLearning() ?? "";
        SetAdvisorStatus(helpful
            ? "Mulțumim — sugestia a fost marcată utilă (învățare locală)."
            : "Notat — nu vom mai insista pe această sugestie.");
        if (!helpful)
            RefreshLabAdvisorFromState(force: true);
    }

    private void NotifyAdvisorSuggestionProps()
    {
        OnPropertyChanged(nameof(HasAdvisorSuggestions));
        OnPropertyChanged(nameof(AdvisorStatusHint));
        OnPropertyChanged(nameof(ShowAdvisorStatusHint));
    }

    /// <summary>Mark last export as failed so the advisor can offer one next step.</summary>
    private void NoteAdvisorExportFailed(string message)
    {
        _advisorExportStatus = AdvisorExportStatus.Failed;
        _advisorLastExportError = message;
    }
    private void SetAdvisorStatus(string message)
    {
        _advisorStatusGuard = true;
        try { Status = message; }
        finally { _advisorStatusGuard = false; }
    }

    private static bool IsAdvisorMetaStatus(string status)
    {
        var s = status.TrimStart();
        return s.StartsWith("Asistent", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Mulțumim", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Multumim", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Notat", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Scrieți", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Scrieti", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Acțiune", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Actiune", StringComparison.OrdinalIgnoreCase)
               || s.Contains("Asistent lab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LoadAdvisorPanelPref()
    {
        try
        {
            var path = AppPaths.AdvisorPrefs;
            if (!File.Exists(path)) return true;
            var prefs = JsonSerializer.Deserialize<AdvisorUiPrefs>(File.ReadAllText(path), AdvisorPrefsJson);
            return prefs?.ShowPanel ?? true;
        }
        catch { return true; }
    }

    private static void PersistAdvisorPanelPref(bool show)
    {
        try
        {
            AppPaths.EnsureWritable(AppPaths.Data);
            var prefs = new AdvisorUiPrefs { ShowPanel = show };
            File.WriteAllText(AppPaths.AdvisorPrefs, JsonSerializer.Serialize(prefs, AdvisorPrefsJson));
        }
        catch { /* ignore */ }
    }

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";
}
