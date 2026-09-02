using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.Core.Projects;

/// <summary>Per-channel / sensor snapshot embedded in reports (Excel, PDF, HTML, .upet).</summary>
public sealed class ChannelReportInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool Enabled { get; set; }
    public bool RecordEnabled { get; set; }
    public string? SensorId { get; set; }
    public string? SensorName { get; set; }
    public string? SensorCode { get; set; }
    public string? SensorCategory { get; set; }
    public string? TransducerType { get; set; }
    public string Bridge { get; set; } = "";
    public double Capacity { get; set; }
    public double Sensitivity { get; set; }
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    public double TareValue { get; set; }
    public double ExcitationV { get; set; }
    public double RangeMvPerV { get; set; }
    public double FilterHz { get; set; }
    public int ChannelSampleRateHz { get; set; }
    public double ShuntKohm { get; set; }
    public string? SensorNotes { get; set; }
    public bool IsMathChannel { get; set; }
    public string? MathExpression { get; set; }

    public string SensorDisplay =>
        !string.IsNullOrWhiteSpace(SensorName) ? SensorName!
        : !string.IsNullOrWhiteSpace(SensorCode) ? SensorCode!
        : !string.IsNullOrWhiteSpace(SensorId) ? SensorId!
        : "—";

    public string OneLineSummary()
    {
        var sb = new StringBuilder();
        sb.Append(Name);
        if (!string.IsNullOrWhiteSpace(Unit)) sb.Append(" [").Append(Unit).Append(']');
        sb.Append(": ").Append(SensorDisplay);
        if (!string.IsNullOrWhiteSpace(SensorCode) &&
            !string.Equals(SensorCode, SensorName, StringComparison.OrdinalIgnoreCase))
            sb.Append(" (").Append(SensorCode).Append(')');
        if (!string.IsNullOrWhiteSpace(SensorCategory))
            sb.Append(" · ").Append(SensorCategory);
        if (!string.IsNullOrWhiteSpace(Bridge) && Bridge != nameof(BridgeType.None))
            sb.Append(" · ").Append(Bridge);
        if (Capacity > 0)
            sb.Append(" · Cap=").Append(Capacity.ToString("G6", CultureInfo.InvariantCulture));
        if (Sensitivity > 0 && Math.Abs(Sensitivity - 1.0) > 1e-9)
            sb.Append(" · Sens=").Append(Sensitivity.ToString("G6", CultureInfo.InvariantCulture));
        sb.Append(" · Scale=").Append(Scale.ToString("G4", CultureInfo.InvariantCulture));
        if (Math.Abs(Offset) > 1e-12)
            sb.Append(" Offset=").Append(Offset.ToString("G4", CultureInfo.InvariantCulture));
        if (Math.Abs(TareValue) > 1e-12)
            sb.Append(" Zero=").Append(TareValue.ToString("G4", CultureInfo.InvariantCulture));
        if (ExcitationV > 0)
            sb.Append(" · Uexc=").Append(ExcitationV.ToString("G4", CultureInfo.InvariantCulture)).Append(" V");
        if (RangeMvPerV > 0)
            sb.Append(" · Range=").Append(RangeMvPerV.ToString("G4", CultureInfo.InvariantCulture)).Append(" mV/V");
        if (FilterHz > 0)
            sb.Append(" · Filt=").Append(FilterHz.ToString("G4", CultureInfo.InvariantCulture)).Append(" Hz");
        if (ChannelSampleRateHz > 0)
            sb.Append(" · fs_ch=").Append(ChannelSampleRateHz).Append(" Hz");
        if (ShuntKohm > 0)
            sb.Append(" · Shunt=").Append(ShuntKohm.ToString("G4", CultureInfo.InvariantCulture)).Append(" kΩ");
        if (IsMathChannel && !string.IsNullOrWhiteSpace(MathExpression))
            sb.Append(" · Math=").Append(MathExpression);
        return sb.ToString();
    }
}

public static class ChannelReportBuilder
{
    /// <summary>
    /// Channels used in the experiment: Enabled + RecordEnabled (fallback: all Enabled).
    /// Catalog enriches Code / Sensitivity / TransducerType / Notes when SensorId/Name/Code match.
    /// </summary>
    public static List<ChannelReportInfo> FromChannels(
        IEnumerable<ChannelConfig> channels,
        IEnumerable<SensorDefinition>? catalog = null)
    {
        var list = channels.ToList();
        var used = list.Where(c => c.Enabled && c.RecordEnabled).ToList();
        if (used.Count == 0)
            used = list.Where(c => c.Enabled).ToList();

        var byId = new Dictionary<string, SensorDefinition>(StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, SensorDefinition>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, SensorDefinition>(StringComparer.OrdinalIgnoreCase);
        if (catalog is not null)
        {
            foreach (var s in catalog)
            {
                if (!string.IsNullOrWhiteSpace(s.Id) && !byId.ContainsKey(s.Id))
                    byId[s.Id] = s;
                if (!string.IsNullOrWhiteSpace(s.Code) && !byCode.ContainsKey(s.Code))
                    byCode[s.Code] = s;
                if (!string.IsNullOrWhiteSpace(s.Name) && !byName.ContainsKey(s.Name))
                    byName[s.Name] = s;
            }
        }

        var result = new List<ChannelReportInfo>(used.Count);
        foreach (var ch in used.OrderBy(c => c.Index))
        {
            SensorDefinition? def = null;
            if (!string.IsNullOrWhiteSpace(ch.SensorId) && byId.TryGetValue(ch.SensorId!, out var d1))
                def = d1;
            else if (!string.IsNullOrWhiteSpace(ch.SensorName) && byCode.TryGetValue(ch.SensorName!, out var d2))
                def = d2;
            else if (!string.IsNullOrWhiteSpace(ch.SensorId) && byCode.TryGetValue(ch.SensorId!, out var d3))
                def = d3;
            else if (!string.IsNullOrWhiteSpace(ch.SensorName) && byName.TryGetValue(ch.SensorName!, out var d4))
                def = d4;

            result.Add(new ChannelReportInfo
            {
                Index = ch.Index,
                Name = ch.Name,
                Unit = ch.Unit,
                Enabled = ch.Enabled,
                RecordEnabled = ch.RecordEnabled,
                SensorId = ch.SensorId ?? def?.Id,
                SensorName = ch.SensorName ?? def?.Name,
                SensorCode = def?.Code,
                SensorCategory = ch.SensorCategory ?? def?.Category,
                TransducerType = def?.TransducerType,
                Bridge = ch.Bridge == BridgeType.None && def is not null
                    ? def.Bridge
                    : ch.Bridge.ToString(),
                Capacity = ch.Capacity > 0 ? ch.Capacity : def?.Capacity ?? 0,
                Sensitivity = def?.Sensitivity ?? 0,
                Scale = ch.Scale,
                Offset = ch.Offset,
                TareValue = ch.TareValue,
                ExcitationV = ch.ExcitationV > 0 ? ch.ExcitationV : def?.ExcitationV ?? 0,
                RangeMvPerV = ch.RangeMvPerV > 0 ? ch.RangeMvPerV : def?.RangeMvPerV ?? 0,
                FilterHz = ch.FilterHz > 0 ? ch.FilterHz : def?.FilterHz ?? 0,
                ChannelSampleRateHz = ch.ChannelSampleRateHz > 0
                    ? ch.ChannelSampleRateHz
                    : def?.ChannelSampleRateHz ?? 0,
                ShuntKohm = ch.ShuntKohm > 0 ? ch.ShuntKohm : def?.ShuntKohm ?? 0,
                SensorNotes = def?.Notes,
                IsMathChannel = ch.IsMathChannel,
                MathExpression = ch.MathExpression
            });
        }

        return result;
    }

    public static string BuildSensorSummary(IReadOnlyList<ChannelReportInfo> channels) =>
        string.Join("; ", channels.Select(c => c.OneLineSummary()));
}
