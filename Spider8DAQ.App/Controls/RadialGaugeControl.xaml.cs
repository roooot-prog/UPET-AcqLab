using System.Globalization;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Shapes;



namespace Spider8DAQ.App.Controls;



public partial class RadialGaugeControl : UserControl

{

    public static readonly RoutedEvent DialActivatedEvent = EventManager.RegisterRoutedEvent(

        nameof(DialActivated), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(RadialGaugeControl));



    public static readonly DependencyProperty ValueProperty =

        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RadialGaugeControl),

            new PropertyMetadata(double.NaN, OnGaugeChanged));



    public static readonly DependencyProperty MinimumProperty =

        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RadialGaugeControl),

            new PropertyMetadata(0.0, OnGaugeChanged));



    public static readonly DependencyProperty MaximumProperty =

        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RadialGaugeControl),

            new PropertyMetadata(100.0, OnGaugeChanged));



    public static readonly DependencyProperty UnitProperty =

        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RadialGaugeControl),

            new PropertyMetadata("kg", OnGaugeChanged));



    public static readonly DependencyProperty TickCountProperty =

        DependencyProperty.Register(nameof(TickCount), typeof(int), typeof(RadialGaugeControl),

            new PropertyMetadata(21, OnGaugeChanged));



    private const double StartAngleDeg = 135;

    private const double SweepDeg = 270;



    public RadialGaugeControl()

    {

        InitializeComponent();

        Loaded += (_, _) => Redraw();

        SizeChanged += (_, _) => Redraw();

        MouseLeftButtonUp += OnMouseLeftButtonUp;

    }



    public event RoutedEventHandler DialActivated

    {

        add => AddHandler(DialActivatedEvent, value);

        remove => RemoveHandler(DialActivatedEvent, value);

    }



    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)

    {

        if (e.ChangedButton != MouseButton.Left) return;

        RaiseEvent(new RoutedEventArgs(DialActivatedEvent, this));

        e.Handled = true;

    }



    public double Value

    {

        get => (double)GetValue(ValueProperty);

        set => SetValue(ValueProperty, value);

    }



    public double Minimum

    {

        get => (double)GetValue(MinimumProperty);

        set => SetValue(MinimumProperty, value);

    }



    public double Maximum

    {

        get => (double)GetValue(MaximumProperty);

        set => SetValue(MaximumProperty, value);

    }



    public string Unit

    {

        get => (string)GetValue(UnitProperty);

        set => SetValue(UnitProperty, value);

    }



    public int TickCount

    {

        get => (int)GetValue(TickCountProperty);

        set => SetValue(TickCountProperty, value);

    }



    private static void OnGaugeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)

    {

        if (d is RadialGaugeControl g) g.Redraw();

    }



    private void Redraw()

    {

        if (TrackArc is null || ValueArc is null || ValueText is null || TickCanvas is null) return;



        TickCanvas.Children.Clear();



        var min = Minimum;

        var max = Math.Max(min + 1e-9, Maximum);

        var val = double.IsNaN(Value) ? min : Math.Clamp(Value, min, max);

        var frac = (val - min) / (max - min);

        var bipolar = min < 0 && max > 0;

        var zeroFrac = bipolar ? Math.Clamp((0.0 - min) / (max - min), 0, 1) : 0;



        TrackArc.Data = BuildArcGeometry(0, 1);

        // Bipolar (±Capacity): fill from 0 (center) to reading — near-zero ≈ empty arc, not half-full.

        // Unipolar (0…max): fill from min to reading.

        if (bipolar)

        {

            var a = Math.Min(zeroFrac, frac);

            var b = Math.Max(zeroFrac, frac);

            ValueArc.Data = Math.Abs(b - a) < 1e-6

                ? BuildArcGeometry(zeroFrac, zeroFrac + 1e-4)

                : BuildArcGeometry(a, b);

            DrawNeedle(frac);

            DrawZeroMarker(zeroFrac);

        }

        else

        {

            ValueArc.Data = BuildArcGeometry(0, frac);

            DrawNeedle(frac);

        }



        DrawTicks(min, max);



        ValueText.Text = double.IsNaN(Value) ? "—" : FormatValue(Value, Unit);

        UnitText.Text = string.IsNullOrWhiteSpace(Unit) ? "" : Unit.Trim();

        // Shrink value font slightly for wide bipolar ranges (e.g. -5250); unit stays large.

        ValueText.FontSize = Math.Abs(val) >= 1000 || Math.Abs(max) >= 1000 || Math.Abs(min) >= 1000 ? 22 : 26;

        UnitText.FontSize = IsForceUnit(Unit) ? 18 : 14;

        UnitText.FontWeight = IsForceUnit(Unit) ? FontWeights.Bold : FontWeights.SemiBold;

        UnitText.Foreground = IsForceUnit(Unit)

            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x26))

            : new SolidColorBrush(Color.FromRgb(0x4E, 0x58, 0x64));

    }



    private void DrawNeedle(double frac)

    {

        const double cx = 100;

        const double cy = 96;

        const double rHub = 5;

        const double rTip = 72;

        var angle = StartAngleDeg + SweepDeg * Math.Clamp(frac, 0, 1);

        var hub = PointOnCircle(cx, cy, rHub, angle);

        var tip = PointOnCircle(cx, cy, rTip, angle);

        // Fine industrial needle (thin tip, dark).

        TickCanvas.Children.Add(new Line

        {

            X1 = hub.X, Y1 = hub.Y, X2 = tip.X, Y2 = tip.Y,

            Stroke = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30)),

            StrokeThickness = 1.6,

            StrokeStartLineCap = PenLineCap.Round,

            StrokeEndLineCap = PenLineCap.Triangle

        });

        var hubDot = new Ellipse

        {

            Width = 8, Height = 8,

            Fill = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30)),

            Stroke = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E)),

            StrokeThickness = 1.2

        };

        Canvas.SetLeft(hubDot, cx - 4);

        Canvas.SetTop(hubDot, cy - 4);

        TickCanvas.Children.Add(hubDot);

    }



    private void DrawZeroMarker(double zeroFrac)

    {

        const double cx = 100;

        const double cy = 96;

        var angle = StartAngleDeg + SweepDeg * zeroFrac;

        var p1 = PointOnCircle(cx, cy, 66, angle);

        var p2 = PointOnCircle(cx, cy, 86, angle);

        TickCanvas.Children.Add(new Line

        {

            X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,

            Stroke = new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x2B)),

            StrokeThickness = 2.2

        });

    }



    private void DrawTicks(double min, double max)

    {

        const double cx = 100;

        const double cy = 96;

        const double rOuter = 86;

        const double rInnerMajor = 70;

        const double rInnerMid = 74;

        const double rInnerMinor = 78;

        // Prefer denser industrial divisions (major every 5, mid every mid-step).

        var majors = Math.Clamp((TickCount - 1) / 2 + 1, 5, 13);

        if (majors % 2 == 0) majors++;

        var minorPerMajor = 4;

        var totalSteps = (majors - 1) * minorPerMajor;

        var brushMajor = new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x3E));

        var brushMid = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));

        var brushMinor = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));



        for (var i = 0; i <= totalSteps; i++)

        {

            var t = i / (double)totalSteps;

            var angle = StartAngleDeg + SweepDeg * t;

            var isMajor = i % minorPerMajor == 0;

            var isMid = !isMajor && i % (minorPerMajor / 2) == 0;

            var rIn = isMajor ? rInnerMajor : isMid ? rInnerMid : rInnerMinor;

            var p1 = PointOnCircle(cx, cy, rIn, angle);

            var p2 = PointOnCircle(cx, cy, rOuter, angle);

            TickCanvas.Children.Add(new Line

            {

                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,

                Stroke = isMajor ? brushMajor : isMid ? brushMid : brushMinor,

                StrokeThickness = isMajor ? 2.2 : isMid ? 1.4 : 0.9

            });



            if (isMajor)

            {

                var labelVal = min + (max - min) * t;

                var lp = PointOnCircle(cx, cy, 56, angle);

                var label = FormatValue(labelVal, Unit);

                var tb = new TextBlock

                {

                    Text = label,

                    FontSize = 9.5,

                    FontWeight = FontWeights.SemiBold,

                    Foreground = brushMajor,

                    Width = 48,

                    TextAlignment = TextAlignment.Center,

                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New")

                };

                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                var tw = tb.DesiredSize.Width;

                var th = tb.DesiredSize.Height;

                tb.RenderTransform = new TranslateTransform(lp.X - tw / 2, lp.Y - th / 2);

                TickCanvas.Children.Add(tb);

            }

        }

    }



    private static string FormatValue(double v, string? unit)

    {

        // Primary kg readout: whole kilograms only (round nearest).

        if (IsKgUnit(unit))

            return Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);



        var a = Math.Abs(v);

        if (a >= 1000) return v.ToString("0.#", CultureInfo.InvariantCulture);

        if (a >= 100) return v.ToString("0.##", CultureInfo.InvariantCulture);

        if (a >= 1) return v.ToString("0.###", CultureInfo.InvariantCulture);

        return v.ToString("0.####", CultureInfo.InvariantCulture);

    }



    private static bool IsKgUnit(string? unit) =>

        !string.IsNullOrWhiteSpace(unit)

        && unit.Trim().Equals("kg", StringComparison.OrdinalIgnoreCase);



    private static bool IsForceUnit(string? unit)

    {

        if (string.IsNullOrWhiteSpace(unit)) return false;

        var u = unit.Trim();

        return u.Equals("N", StringComparison.OrdinalIgnoreCase)

               || u.Equals("kN", StringComparison.OrdinalIgnoreCase)

               || u.Equals("kg", StringComparison.OrdinalIgnoreCase)

               || u.Equals("kgf", StringComparison.OrdinalIgnoreCase);

    }



    private PathGeometry BuildArcGeometry(double startFrac, double endFrac)

    {

        const double cx = 100;

        const double cy = 96;

        const double r = 76;



        var startAngle = StartAngleDeg + SweepDeg * startFrac;

        var endAngle = StartAngleDeg + SweepDeg * endFrac;

        var start = PointOnCircle(cx, cy, r, startAngle);

        var end = PointOnCircle(cx, cy, r, endAngle);

        var large = (endAngle - startAngle) > 180;



        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };

        fig.Segments.Add(new ArcSegment

        {

            Point = end,

            Size = new Size(r, r),

            IsLargeArc = large,

            SweepDirection = SweepDirection.Clockwise

        });



        return new PathGeometry { Figures = { fig } };

    }



    private static Point PointOnCircle(double cx, double cy, double r, double angleDeg)

    {

        var rad = angleDeg * Math.PI / 180.0;

        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));

    }

}


