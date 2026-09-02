using System.Globalization;
using System.Text.RegularExpressions;

namespace Spider8DAQ.Core.MathChannels;

public enum MathOp
{
    Copy,
    Add,
    Subtract,
    Multiply,
    Average,
    MinWindow,
    MaxWindow,
    Formula,
    RosetteEx,
    RosetteEy,
    RosetteGamma
}

public sealed class MathChannelDefinition
{
    public string Name { get; set; } = "Math";
    public string Unit { get; set; } = "";
    public MathOp Operation { get; set; } = MathOp.Average;
    public int SourceA { get; set; }
    public int? SourceB { get; set; }
    public int? SourceC { get; set; }
    public int WindowSize { get; set; } = 25;
    public bool Enabled { get; set; } = true;
    /// <summary>Simple formula using CHn tokens matching grid names (0-based), e.g. (CH0-CH1)*2.5+CH2</summary>
    public string? Formula { get; set; }
}

public sealed class MathChannelEngine
{
    private readonly Dictionary<int, Queue<double>> _windows = new();
    private static readonly Regex Token = new(@"CH(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public double[] Evaluate(IReadOnlyList<MathChannelDefinition> defs, double[] physicalValues)
    {
        var result = new double[defs.Count];
        for (var i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            if (!d.Enabled)
            {
                result[i] = double.NaN;
                continue;
            }

            var a = Get(physicalValues, d.SourceA);
            var b = d.SourceB is int sb ? Get(physicalValues, sb) : 0;
            var c = d.SourceC is int sc ? Get(physicalValues, sc) : 0;

            result[i] = d.Operation switch
            {
                MathOp.Copy => a,
                MathOp.Add => a + b,
                MathOp.Subtract => a - b,
                MathOp.Multiply => a * b,
                MathOp.Average => WindowStat(i, a, d.WindowSize, static q => q.Average()),
                MathOp.MinWindow => WindowStat(i, a, d.WindowSize, static q => q.Min()),
                MathOp.MaxWindow => WindowStat(i, a, d.WindowSize, static q => q.Max()),
                MathOp.Formula => EvaluateFormula(d.Formula, physicalValues),
                MathOp.RosetteEx => a, // A = ε0 (0°)
                MathOp.RosetteEy => d.SourceC is not null ? c : b, // C = ε90; fallback B
                MathOp.RosetteGamma => 2 * b - a - (d.SourceC is not null ? c : 0), // γxy = 2·ε45 − ε0 − ε90
                _ => a
            };
        }

        return result;
    }

    public static double EvaluateFormula(string? formula, double[] physicalValues)
    {
        if (string.IsNullOrWhiteSpace(formula)) return double.NaN;
        try
        {
            var expr = Token.Replace(formula, m =>
            {
                // CHn = hardware / grid index (CH0…CH7), same as channel Name.
                var idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var v = Get(physicalValues, idx);
                return double.IsNaN(v) ? "0" : v.ToString(CultureInfo.InvariantCulture);
            });
            // very small expression evaluator: + - * / ( )
            return SimpleExpr.Eval(expr);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static double Get(double[] values, int index)
        => index >= 0 && index < values.Length ? values[index] : double.NaN;

    private double WindowStat(int id, double value, int size, Func<IEnumerable<double>, double> agg)
    {
        if (!_windows.TryGetValue(id, out var q))
        {
            q = new Queue<double>();
            _windows[id] = q;
        }

        q.Enqueue(value);
        while (q.Count > Math.Max(1, size))
            q.Dequeue();
        return agg(q);
    }

    public void Reset() => _windows.Clear();
}

internal static class SimpleExpr
{
    public static double Eval(string expr)
    {
        var values = new Stack<double>();
        var ops = new Stack<char>();
        for (var i = 0; i < expr.Length; i++)
        {
            var ch = expr[i];
            if (char.IsWhiteSpace(ch)) continue;
            if (char.IsDigit(ch) || ch == '.' || (ch == '-' && (i == 0 || (ops.Count > 0 && values.Count == ops.Count))))
            {
                var start = i;
                if (expr[i] == '-') i++;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.')) i++;
                values.Push(double.Parse(expr[start..i], CultureInfo.InvariantCulture));
                i--;
            }
            else if (ch == '(') ops.Push(ch);
            else if (ch == ')')
            {
                while (ops.Count > 0 && ops.Peek() != '(') Apply(values, ops.Pop());
                if (ops.Count > 0) ops.Pop();
            }
            else if (ch is '+' or '-' or '*' or '/')
            {
                while (ops.Count > 0 && Prec(ops.Peek()) >= Prec(ch))
                    Apply(values, ops.Pop());
                ops.Push(ch);
            }
        }
        while (ops.Count > 0) Apply(values, ops.Pop());
        return values.Count == 1 ? values.Pop() : double.NaN;
    }

    private static int Prec(char op) => op switch { '+' or '-' => 1, '*' or '/' => 2, _ => 0 };

    private static void Apply(Stack<double> values, char op)
    {
        if (values.Count < 2) return;
        var b = values.Pop();
        var a = values.Pop();
        values.Push(op switch
        {
            '+' => a + b,
            '-' => a - b,
            '*' => a * b,
            '/' => b == 0 ? double.NaN : a / b,
            _ => double.NaN
        });
    }
}
