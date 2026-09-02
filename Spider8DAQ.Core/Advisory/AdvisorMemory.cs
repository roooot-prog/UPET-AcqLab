using System.Text.Json;

namespace Spider8DAQ.Core.Advisory;

/// <summary>Persists which advice rules helped — simple local learning, not model training.</summary>
public sealed class AdvisorMemory
{
    private readonly string _path;
    private readonly object _gate = new();
    private AdvisorFeedbackStore _store = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AdvisorMemory(string path)
    {
        _path = path;
        Load();
    }

    public void RecordShown(string ruleId, string statusSnippet)
    {
        lock (_gate)
        {
            var s = Stats(ruleId);
            s.Shown++;
            _store.Recent.Add(new AdviceEvent
            {
                Utc = DateTime.UtcNow.ToString("o"),
                RuleId = ruleId,
                StatusSnippet = Trunc(statusSnippet, 160)
            });
            TrimRecent();
            Save();
        }
    }

    public void RecordFeedback(string ruleId, bool helpful)
    {
        lock (_gate)
        {
            var s = Stats(ruleId);
            if (helpful) s.Helpful++;
            else s.NotHelpful++;
            var last = _store.Recent.LastOrDefault(e => e.RuleId == ruleId);
            if (last is not null)
                last.Feedback = helpful ? "helpful" : "not";
            Save();
        }
    }

    /// <summary>Positive boost for helpful rules; penalty for rejected ones. Shown count does not nag.</summary>
    public double Boost(string ruleId)
    {
        lock (_gate)
        {
            if (!_store.Stats.TryGetValue(ruleId, out var s)) return 0;
            return s.Helpful * 0.35 - s.NotHelpful * 0.55;
        }
    }

    /// <summary>True when the operator marked this tip «Nu» more often than «Utilă».</summary>
    public bool IsRejected(string ruleId)
    {
        lock (_gate)
        {
            if (!_store.Stats.TryGetValue(ruleId, out var s)) return false;
            return s.NotHelpful > s.Helpful;
        }
    }

    public int TimesShown(string ruleId)
    {
        lock (_gate)
        {
            return _store.Stats.TryGetValue(ruleId, out var s) ? s.Shown : 0;
        }
    }

    public string SummarizeLearning()
    {
        lock (_gate)
        {
            if (_store.Stats.Count == 0)
                return "Încă fără feedback — marcați sugestiile ca Utile / Nu.";
            var top = _store.Stats.OrderByDescending(kv => kv.Value.Helpful - kv.Value.NotHelpful).Take(3);
            return "Învățare locală: " + string.Join("; ",
                top.Select(kv => $"{kv.Key} (+{kv.Value.Helpful}/−{kv.Value.NotHelpful})"));
        }
    }

    private AdviceStats Stats(string ruleId)
    {
        if (!_store.Stats.TryGetValue(ruleId, out var s))
        {
            s = new AdviceStats();
            _store.Stats[ruleId] = s;
        }
        return s;
    }

    private void TrimRecent()
    {
        const int max = 200;
        if (_store.Recent.Count > max)
            _store.Recent.RemoveRange(0, _store.Recent.Count - max);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _store = JsonSerializer.Deserialize<AdvisorFeedbackStore>(json, JsonOpts) ?? new();
            _store.Stats ??= new();
            _store.Recent ??= new();
        }
        catch
        {
            _store = new();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_store, JsonOpts));
        }
        catch
        {
            /* ignore disk errors */
        }
    }

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";
}
