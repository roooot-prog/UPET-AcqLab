using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Free-layout card: drag by title bar, resize by edges/corners (Thumb).
/// ContentControl (not UserControl) so x:Name on children stays in the parent namescope.
/// Must be a child of a Canvas.
/// </summary>
public class MovableResizableCard : ContentControl
{
    public const double DefaultMinWidth = 160;
    public const double DefaultMinHeight = 72;

    private Border? _dragHeader;
    private TextBlock? _titleBlock;
    private Button? _minimizeButton;
    private TextBlock? _minimizeGlyph;
    private FrameworkElement? _contentHost;
    private bool _templateReady;
    private bool _dragging;
    private Point _dragStartMouse;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _suppressLayoutEvent;

    static MovableResizableCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MovableResizableCard),
            new FrameworkPropertyMetadata(typeof(MovableResizableCard)));
    }

    public MovableResizableCard()
    {
        MinWidth = DefaultMinWidth;
        MinHeight = DefaultMinHeight;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => RaiseLayoutChanged();
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MovableResizableCard),
            new PropertyMetadata("Card", (d, e) =>
            {
                if (d is MovableResizableCard c && c._titleBlock is not null)
                    c._titleBlock.Text = e.NewValue as string ?? "";
            }));

    public static readonly DependencyProperty CardIdProperty =
        DependencyProperty.Register(nameof(CardId), typeof(string), typeof(MovableResizableCard),
            new PropertyMetadata(""));

    public static readonly DependencyProperty HeaderBackgroundProperty =
        DependencyProperty.Register(nameof(HeaderBackground), typeof(Brush), typeof(MovableResizableCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsMinimizedProperty =
        DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(MovableResizableCard),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsMinimizedChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string CardId
    {
        get => (string)GetValue(CardIdProperty);
        set => SetValue(CardIdProperty, value);
    }

    public Brush? HeaderBackground
    {
        get => (Brush?)GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public bool IsMinimized
    {
        get => (bool)GetValue(IsMinimizedProperty);
        set => SetValue(IsMinimizedProperty, value);
    }

    public event EventHandler? LayoutChanged;
    public event EventHandler? MinimizedChanged;

    private static void OnIsMinimizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovableResizableCard card)
            card.ApplyMinimizedState((bool)e.NewValue);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_dragHeader is not null)
        {
            _dragHeader.MouseLeftButtonDown -= Header_MouseLeftButtonDown;
            _dragHeader.MouseMove -= Header_MouseMove;
            _dragHeader.MouseLeftButtonUp -= Header_MouseLeftButtonUp;
        }

        if (_minimizeButton is not null)
            _minimizeButton.Click -= MinimizeButton_Click;

        _dragHeader = GetTemplateChild("PART_DragHeader") as Border;
        _titleBlock = GetTemplateChild("PART_Title") as TextBlock;
        _minimizeButton = GetTemplateChild("PART_MinimizeButton") as Button;
        _minimizeGlyph = GetTemplateChild("PART_MinimizeGlyph") as TextBlock;
        _contentHost = GetTemplateChild("PART_ContentHost") as FrameworkElement
                       ?? (GetTemplateChild("PART_ContentPresenter") as FrameworkElement);

        // ContentPresenter may be unnamed — find it in the visual tree under the outer border
        if (_contentHost is null && _dragHeader is not null)
        {
            var dock = VisualTreeHelper.GetParent(_dragHeader) as DockPanel;
            if (dock is not null)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(dock); i++)
                {
                    if (VisualTreeHelper.GetChild(dock, i) is ContentPresenter cp)
                    {
                        _contentHost = cp;
                        break;
                    }
                }
            }
        }

        if (_titleBlock is not null)
            _titleBlock.Text = Title;

        if (_minimizeButton is not null)
            _minimizeButton.Click += MinimizeButton_Click;

        if (_dragHeader is not null)
        {
            _dragHeader.MouseLeftButtonDown += Header_MouseLeftButtonDown;
            _dragHeader.MouseMove += Header_MouseMove;
            _dragHeader.MouseLeftButtonUp += Header_MouseLeftButtonUp;
            _dragHeader.LostMouseCapture += (_, _) => _dragging = false;
        }

        ApplyMinimizedState(IsMinimized);

        WireResize(GetTemplateChild("PART_ResizeN") as Thumb, ResizeEdges.N);
        WireResize(GetTemplateChild("PART_ResizeS") as Thumb, ResizeEdges.S);
        WireResize(GetTemplateChild("PART_ResizeW") as Thumb, ResizeEdges.W);
        WireResize(GetTemplateChild("PART_ResizeE") as Thumb, ResizeEdges.E);
        WireResize(GetTemplateChild("PART_ResizeNW") as Thumb, ResizeEdges.N | ResizeEdges.W);
        WireResize(GetTemplateChild("PART_ResizeNE") as Thumb, ResizeEdges.N | ResizeEdges.E);
        WireResize(GetTemplateChild("PART_ResizeSW") as Thumb, ResizeEdges.S | ResizeEdges.W);
        WireResize(GetTemplateChild("PART_ResizeSE") as Thumb, ResizeEdges.S | ResizeEdges.E);
        _templateReady = true;
    }

    private void WireResize(Thumb? thumb, ResizeEdges edges)
    {
        if (thumb is null) return;
        thumb.DragDelta += (_, args) => ApplyResize(edges, args.HorizontalChange, args.VerticalChange);
        thumb.DragStarted += (_, _) => BringToFront();
        thumb.DragCompleted += (_, _) => RaiseLayoutChanged();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        IsMinimized = !IsMinimized;
        e.Handled = true;
    }

    private void ApplyMinimizedState(bool minimized)
    {
        // Cardul dispare din canvas și apare pe bara de jos (orchestrat de MainWindow).
        // Nu atingem Window.WindowState — doar vizibilitatea acestui card.
        if (_minimizeGlyph is not null)
            _minimizeGlyph.Text = "―";

        if (_minimizeButton is not null)
            _minimizeButton.ToolTip = "Minimizează pe bara de jos";

        if (_contentHost is not null)
            _contentHost.Visibility = Visibility.Visible;

        SetResizeThumbsVisible(true);

        Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;

        MinimizedChanged?.Invoke(this, EventArgs.Empty);
        // Nu apelăm RaiseLayoutChanged aici: la Collapsed ActualWidth/Height=0 și cadranul
        // ar salva layout 0×0 / ar declanșa ApplyUiLayout greșit pe fereastră.
    }

    private void SetResizeThumbsVisible(bool visible)
    {
        var vis = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var name in new[]
                 {
                     "PART_ResizeN", "PART_ResizeS", "PART_ResizeW", "PART_ResizeE",
                     "PART_ResizeNW", "PART_ResizeNE", "PART_ResizeSW", "PART_ResizeSE"
                 })
        {
            if (GetTemplateChild(name) is UIElement el)
                el.Visibility = vis;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            // Double-click header toggles minimize
            if (e.OriginalSource is DependencyObject src0 && FindAncestor<ButtonBase>(src0) is null)
            {
                IsMinimized = !IsMinimized;
                e.Handled = true;
            }
            return;
        }
        if (e.OriginalSource is DependencyObject src && FindAncestor<ButtonBase>(src) is not null)
            return;

        var canvas = FindParentCanvas();
        if (canvas is null) return;

        _dragging = true;
        _dragStartMouse = e.GetPosition(canvas);
        _dragStartLeft = GetHostLeft();
        _dragStartTop = GetHostTop();
        BringToFront();
        _dragHeader?.CaptureMouse();
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var canvas = FindParentCanvas();
        if (canvas is null) return;

        var pos = e.GetPosition(canvas);
        var left = _dragStartLeft + (pos.X - _dragStartMouse.X);
        var top = _dragStartTop + (pos.Y - _dragStartMouse.Y);
        SetPositionClamped(left, top);
        e.Handled = true;
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_dragHeader?.IsMouseCaptured == true)
            _dragHeader.ReleaseMouseCapture();
        RaiseLayoutChanged();
        e.Handled = true;
    }

    [Flags]
    private enum ResizeEdges
    {
        None = 0,
        N = 1,
        S = 2,
        W = 4,
        E = 8
    }

    private void ApplyResize(ResizeEdges edges, double dx, double dy)
    {
        var canvas = FindParentCanvas();
        var left = GetHostLeft();
        var top = GetHostTop();

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w) || w <= 0) w = MinWidth;
        if (double.IsNaN(h) || h <= 0) h = MinHeight;

        var minW = double.IsNaN(MinWidth) || MinWidth <= 0 ? DefaultMinWidth : MinWidth;
        var minH = double.IsNaN(MinHeight) || MinHeight <= 0 ? DefaultMinHeight : MinHeight;

        if (edges.HasFlag(ResizeEdges.E))
            w = Math.Max(minW, w + dx);
        if (edges.HasFlag(ResizeEdges.S))
            h = Math.Max(minH, h + dy);
        if (edges.HasFlag(ResizeEdges.W))
        {
            var newW = Math.Max(minW, w - dx);
            left += w - newW;
            w = newW;
        }
        if (edges.HasFlag(ResizeEdges.N))
        {
            var newH = Math.Max(minH, h - dy);
            top += h - newH;
            h = newH;
        }

        _suppressLayoutEvent = true;
        Width = w;
        Height = h;
        SetPositionClamped(left, top, canvas);
        _suppressLayoutEvent = false;
    }

    private void SetPositionClamped(double left, double top, Canvas? canvas = null)
    {
        canvas ??= FindParentCanvas();
        var w = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? MinWidth : Width);
        var h = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? MinHeight : Height);

        if (canvas is not null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
        {
            left = Math.Max(0, Math.Min(left, Math.Max(0, canvas.ActualWidth - Math.Min(w, canvas.ActualWidth))));
            top = Math.Max(0, Math.Min(top, Math.Max(0, canvas.ActualHeight - Math.Min(h, canvas.ActualHeight))));
        }
        else
        {
            left = Math.Max(0, left);
            top = Math.Max(0, top);
        }

        SetHostLeft(left);
        SetHostTop(top);
        if (!_suppressLayoutEvent)
            RaiseLayoutChanged();
    }

    public void ApplyRect(double left, double top, double width, double height)
    {
        _suppressLayoutEvent = true;
        if (width >= MinWidth) Width = width;
        if (height >= MinHeight) Height = height;
        SetPositionClamped(left, top);
        _suppressLayoutEvent = false;
    }

    public (double Left, double Top, double Width, double Height) GetRect()
    {
        var left = GetHostLeft();
        var top = GetHostTop();
        var w = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? MinWidth : Width);
        var h = ActualHeight > 0 ? ActualHeight : (double.IsNaN(Height) ? MinHeight : Height);
        return (left, top, w, h);
    }

    private void BringToFront()
    {
        var canvas = FindParentCanvas();
        if (canvas is null) return;
        var host = GetCanvasPositionHost();
        var max = 0;
        foreach (UIElement child in canvas.Children)
        {
            var z = Panel.GetZIndex(child);
            if (z > max) max = z;
        }
        Panel.SetZIndex(host, max + 1);
    }

    /// <summary>Bring this card above other workspace panels.</summary>
    public void BringCardToFront() => BringToFront();

    /// <summary>
    /// Element that is a direct child of the workspace Canvas (self, or ContentPresenter host in ItemsControl).
    /// </summary>
    private FrameworkElement GetCanvasPositionHost()
    {
        var canvas = FindParentCanvas();
        if (canvas is null) return this;

        var parent = VisualTreeHelper.GetParent(this);
        if (parent is FrameworkElement fe && ReferenceEquals(VisualTreeHelper.GetParent(fe), canvas))
            return fe;

        if (ReferenceEquals(parent, canvas))
            return this;

        return this;
    }

    private double GetHostLeft()
    {
        var left = Canvas.GetLeft(GetCanvasPositionHost());
        return double.IsNaN(left) ? 0 : left;
    }

    private double GetHostTop()
    {
        var top = Canvas.GetTop(GetCanvasPositionHost());
        return double.IsNaN(top) ? 0 : top;
    }

    private void SetHostLeft(double left) => Canvas.SetLeft(GetCanvasPositionHost(), left);

    private void SetHostTop(double top) => Canvas.SetTop(GetCanvasPositionHost(), top);

    private Canvas? FindParentCanvas()
    {
        DependencyObject? p = VisualTreeHelper.GetParent(this);
        while (p is not null)
        {
            if (p is Canvas c) return c;
            p = VisualTreeHelper.GetParent(p);
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void RaiseLayoutChanged()
    {
        if (_suppressLayoutEvent || !_templateReady) return;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
