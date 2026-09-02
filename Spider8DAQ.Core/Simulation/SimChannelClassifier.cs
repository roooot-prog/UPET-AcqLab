using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Simulation;

public enum SimChannelRole
{
    Generic = 0,
    Force = 1,
    Stroke = 2,
    RadialDisplacement = 3,
    Strain = 4,
    Voltage = 5
}

/// <summary>Classifies simulator channels from ContourConfig / units / names.</summary>
public static class SimChannelClassifier
{
    public static SimulatorScenarioKind ResolveKind(SimulatorScenarioHint? hint, IReadOnlyList<ChannelConfig> channels)
    {
        if (hint is { Kind: SimulatorScenarioKind.Generic or SimulatorScenarioKind.CylinderContour })
            return hint.Kind;

        if (hint is not null && ExperimentTypes.IsCylinderContour(hint.ExperimentType))
            return SimulatorScenarioKind.CylinderContour;

        // Heuristic: many length channels + optional force → contour compression demo.
        var mm = 0;
        var forceLike = 0;
        for (var i = 0; i < channels.Count; i++)
        {
            var u = channels[i].Unit ?? "";
            if (IsLengthUnit(u)) mm++;
            if (IsForceUnit(u) || IsForceName(channels[i].Name)) forceLike++;
        }

        if (mm >= 3 && (forceLike >= 1 || mm >= 4))
            return SimulatorScenarioKind.CylinderContour;

        return SimulatorScenarioKind.Generic;
    }

    public static SimChannelRole[] ClassifyAll(
        IReadOnlyList<ChannelConfig> channels,
        SimulatorScenarioHint? hint,
        SimulatorScenarioKind kind)
    {
        var n = channels.Count;
        var roles = new SimChannelRole[n];
        var claimed = new bool[n];

        void Claim(int idx, SimChannelRole role)
        {
            if (idx < 0 || idx >= n || claimed[idx]) return;
            roles[idx] = role;
            claimed[idx] = true;
        }

        if (kind == SimulatorScenarioKind.CylinderContour)
        {
            // Claim radials first so 8-sensor / 8-channel demos keep S1…S8 (stroke must not steal CH0).
            if (hint?.RadialChannelIndices is { Count: > 0 } radials)
            {
                foreach (var r in radials)
                    Claim(r, SimChannelRole.RadialDisplacement);
            }
            if (hint?.ForceChannelIndex is int f && f >= 0)
                Claim(f, SimChannelRole.Force);
            if (hint is { StrokeChannelIndex: >= 0 })
                Claim(hint.StrokeChannelIndex, SimChannelRole.Stroke);

            // Fill unclaimed by units / names.
            for (var i = 0; i < n; i++)
            {
                if (claimed[i]) continue;
                var ch = channels[i];
                if (IsForceUnit(ch.Unit) || IsForceName(ch.Name))
                    Claim(i, SimChannelRole.Force);
                else if (IsStrokeName(ch.Name))
                    Claim(i, SimChannelRole.Stroke);
                else if (IsLengthUnit(ch.Unit) || IsRadialName(ch.Name))
                    Claim(i, SimChannelRole.RadialDisplacement);
            }

            // If still no force but we have length channels, promote the last unused non-radial as stroke/force.
            var preserveRadials = hint?.RadialChannelIndices is { Count: >= 4 };
            EnsureContourRoles(roles, claimed, channels, preserveHintRadials: preserveRadials);
        }

        for (var i = 0; i < n; i++)
        {
            if (claimed[i]) continue;
            roles[i] = ClassifyGeneric(channels[i]);
            claimed[i] = true;
        }

        return roles;
    }

    private static void EnsureContourRoles(
        SimChannelRole[] roles,
        bool[] claimed,
        IReadOnlyList<ChannelConfig> channels,
        bool preserveHintRadials = false)
    {
        var n = channels.Count;
        var hasForce = false;
        var hasStroke = false;
        var radialCount = 0;
        for (var i = 0; i < n; i++)
        {
            if (roles[i] == SimChannelRole.Force) hasForce = true;
            if (roles[i] == SimChannelRole.Stroke) hasStroke = true;
            if (roles[i] == SimChannelRole.RadialDisplacement) radialCount++;
        }

        // Prefer CH0.. as radials when nothing classified yet.
        if (radialCount == 0)
        {
            var take = n >= 9 ? 8 : Math.Min(4, Math.Max(0, n - 1));
            for (var i = 0; i < take; i++)
            {
                if (claimed[i]) continue;
                roles[i] = SimChannelRole.RadialDisplacement;
                claimed[i] = true;
                radialCount++;
            }
        }

        if (!hasStroke)
        {
            // Prefer index = radialCount when free, else last channel.
            var strokeIdx = radialCount < n && !claimed[radialCount]
                ? radialCount
                : FindLastUnclaimed(claimed);
            if (strokeIdx < 0 && radialCount >= 2 && !preserveHintRadials)
            {
                // Demote last radial to cursă (need ≥1 radial left).
                for (var i = n - 1; i >= 0; i--)
                {
                    if (roles[i] == SimChannelRole.RadialDisplacement)
                    {
                        strokeIdx = i;
                        break;
                    }
                }
            }

            if (strokeIdx >= 0)
            {
                roles[strokeIdx] = SimChannelRole.Stroke;
                claimed[strokeIdx] = true;
                hasStroke = true;
            }
        }

        if (!hasForce)
        {
            var forceIdx = FindLastUnclaimed(claimed);
            if (forceIdx < 0)
            {
                // Steal a generic / voltage channel if needed.
                for (var i = n - 1; i >= 0; i--)
                {
                    if (roles[i] is SimChannelRole.Generic or SimChannelRole.Voltage or SimChannelRole.Strain)
                    {
                        forceIdx = i;
                        break;
                    }
                }
            }

            if (forceIdx >= 0)
            {
                roles[forceIdx] = SimChannelRole.Force;
                claimed[forceIdx] = true;
            }
        }
    }

    private static int FindLastUnclaimed(bool[] claimed)
    {
        for (var i = claimed.Length - 1; i >= 0; i--)
            if (!claimed[i]) return i;
        return -1;
    }

    private static SimChannelRole ClassifyGeneric(ChannelConfig ch)
    {
        if (IsForceUnit(ch.Unit) || IsForceName(ch.Name)) return SimChannelRole.Force;
        if (IsStrokeName(ch.Name)) return SimChannelRole.Stroke;
        if (IsLengthUnit(ch.Unit) || IsRadialName(ch.Name)) return SimChannelRole.RadialDisplacement;
        if (IsStrainUnit(ch.Unit)) return SimChannelRole.Strain;
        if (IsVoltageUnit(ch.Unit)) return SimChannelRole.Voltage;
        return SimChannelRole.Generic;
    }

    public static bool IsLengthUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Equals("mm", StringComparison.OrdinalIgnoreCase)
               || u.Equals("µm", StringComparison.OrdinalIgnoreCase)
               || u.Equals("um", StringComparison.OrdinalIgnoreCase)
               || u.Equals("m", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsForceUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Equals("N", StringComparison.OrdinalIgnoreCase)
               || u.Equals("kN", StringComparison.OrdinalIgnoreCase)
               || u.Equals("kgf", StringComparison.OrdinalIgnoreCase)
               || u.Equals("kg", StringComparison.OrdinalIgnoreCase)
               || u.Equals("lbf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrainUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
               || u.Contains("um/m", StringComparison.OrdinalIgnoreCase)
               || u.Equals("microstrain", StringComparison.OrdinalIgnoreCase)
               || u.Equals("με", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVoltageUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Contains("mV/V", StringComparison.OrdinalIgnoreCase)
               || u.Equals("mV", StringComparison.OrdinalIgnoreCase)
               || u.Equals("V", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsForceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("forț", StringComparison.OrdinalIgnoreCase)
               || n.Contains("fort", StringComparison.OrdinalIgnoreCase)
               || n.Contains("force", StringComparison.OrdinalIgnoreCase)
               || n.StartsWith("F", StringComparison.OrdinalIgnoreCase) && n.Length <= 3;
    }

    public static bool IsStrokeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("curs", StringComparison.OrdinalIgnoreCase)
               || n.Contains("stroke", StringComparison.OrdinalIgnoreCase)
               || n.Contains("piston", StringComparison.OrdinalIgnoreCase)
               || n.Contains("axial", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRadialName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Contains("radial", StringComparison.OrdinalIgnoreCase)
               || n.Contains("contur", StringComparison.OrdinalIgnoreCase)
               || n.StartsWith("S", StringComparison.OrdinalIgnoreCase)
                  && n.Length <= 3
                  && char.IsDigit(n[^1]);
    }
}
