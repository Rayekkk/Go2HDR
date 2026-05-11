using Go2HDR.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Go2HDR.Views.Controls;

public partial class CurveEditor : UserControl
{
    const double PadL = 52, PadR = 16, PadT = 16, PadB = 44;
    double PlotW => Math.Max(1, ActualWidth  - PadL - PadR);
    double PlotH => Math.Max(1, ActualHeight - PadT - PadB);

    CurvePoint? _dragPoint;
    bool        _dragIsEndpoint;

    // Frozen static brushes / collections — allocated once, shared across all instances and redraws.
    private static readonly SolidColorBrush GridFaint  = MakeFrozen(Color.FromArgb(40, 128, 128, 128));
    private static readonly SolidColorBrush GridMedium = MakeFrozen(Color.FromArgb(90, 128, 128, 128));
    private static readonly DoubleCollection GridDash  = MakeFrozenDash(3, 3);
    private static readonly DoubleCollection ActiveDash = MakeFrozenDash(4, 3);
    private static readonly FontFamily       SegoeUI   = new("Segoe UI");

    private static SolidColorBrush MakeFrozen(Color c)
    { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static DoubleCollection MakeFrozenDash(params double[] d)
    { var c = new DoubleCollection(d); c.Freeze(); return c; }

    // Per-instance accent brush cache — rebuilt only when accent colour changes.
    private Color             _lastAccent;
    private SolidColorBrush?  _accentBrush;
    private SolidColorBrush?  _accentFillBrush;

    // Debounce timer: collapses rapid batch property changes (e.g. Set All to 100) into one frame.
    private readonly DispatcherTimer _redrawDebounce;

    // Persistent canvas elements — created once, positions updated each Redraw.
    private Line[]?       _gridLines;   // 22 lines (11 vertical + 11 horizontal)
    private TextBlock[]?  _xLabels;     // 6 brightness tick labels
    private TextBlock[]?  _yLabels;     // 6 nits tick labels
    private TextBlock?    _xTitle;
    private TextBlock?    _yTitle;
    private bool          _persistentElementsReady;

    // Persistent curve elements — created once in EnsurePersistentElements, updated in place.
    private Polygon?  _fillPolygon;
    private Polyline? _curvePolyline;

    // Point Ellipses — pooled by CurvePoint to avoid recreation every Redraw.
    private readonly Dictionary<CurvePoint, Ellipse> _pointEllipses = [];

    // Single overlay ring that tracks the live-brightness ActivePoint.
    private Ellipse? _activeRing;

    // ── Dependency Properties ──────────────────────────────────────────────

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ObservableCollection<CurvePoint>), typeof(CurveEditor),
            new PropertyMetadata(null, OnPointsChanged));

    public static readonly DependencyProperty SelectedPointProperty =
        DependencyProperty.Register(nameof(SelectedPoint), typeof(CurvePoint), typeof(CurveEditor),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((CurveEditor)d).Redraw()));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(CurveEditor),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowPointsProperty =
        DependencyProperty.Register(nameof(ShowPoints), typeof(bool), typeof(CurveEditor),
            new PropertyMetadata(true, (d, _) => ((CurveEditor)d).Redraw()));

    public static readonly DependencyProperty MinBrightnessProperty =
        DependencyProperty.Register(nameof(MinBrightness), typeof(double), typeof(CurveEditor),
            new PropertyMetadata(0.0, (d, _) => ((CurveEditor)d).Redraw()));

    public static readonly DependencyProperty ActivePointProperty =
        DependencyProperty.Register(nameof(ActivePoint), typeof(CurvePoint), typeof(CurveEditor),
            new PropertyMetadata(null, (d, _) => ((CurveEditor)d).Redraw()));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowPoints
    {
        get => (bool)GetValue(ShowPointsProperty);
        set => SetValue(ShowPointsProperty, value);
    }

    public ObservableCollection<CurvePoint>? Points
    {
        get => (ObservableCollection<CurvePoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public CurvePoint? SelectedPoint
    {
        get => (CurvePoint?)GetValue(SelectedPointProperty);
        set => SetValue(SelectedPointProperty, value);
    }

    public double MinBrightness
    {
        get => (double)GetValue(MinBrightnessProperty);
        set => SetValue(MinBrightnessProperty, value);
    }

    public CurvePoint? ActivePoint
    {
        get => (CurvePoint?)GetValue(ActivePointProperty);
        set => SetValue(ActivePointProperty, value);
    }

    public event EventHandler?             CurveMoved;
    public event EventHandler<CurvePoint>? AddPointRequested;
    public event EventHandler<CurvePoint>? RemovePointRequested;

    // ── Constructor ────────────────────────────────────────────────────────

    public CurveEditor()
    {
        InitializeComponent();

        _redrawDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _redrawDebounce.Tick += (_, _) => { _redrawDebounce.Stop(); Redraw(); };

        Loaded      += (_, _) => { _redrawDebounce.Stop(); Redraw(); };
        SizeChanged += (_, _) => { _redrawDebounce.Stop(); _redrawDebounce.Start(); };
        MainCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
        MainCanvas.MouseMove           += OnCanvasMouseMove;
        MainCanvas.MouseLeftButtonUp   += OnCanvasMouseUp;
    }

    // ── Collection wiring ─────────────────────────────────────────────────

    static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ed = (CurveEditor)d;
        if (e.OldValue is ObservableCollection<CurvePoint> old)
        {
            old.CollectionChanged -= ed.OnCollectionChanged;
            foreach (var p in old) p.PropertyChanged -= ed.OnPointPropertyChanged;
        }
        if (e.NewValue is ObservableCollection<CurvePoint> @new)
        {
            @new.CollectionChanged += ed.OnCollectionChanged;
            foreach (var p in @new) p.PropertyChanged += ed.OnPointPropertyChanged;
        }
        ed.Redraw();
    }

    void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (CurvePoint p in e.OldItems) p.PropertyChanged -= OnPointPropertyChanged;
        if (e.NewItems != null) foreach (CurvePoint p in e.NewItems) p.PropertyChanged += OnPointPropertyChanged;
        Redraw();
    }

    // Use debounce to coalesce multiple rapid property changes (e.g. bulk operations)
    // into a single redraw.  During an active drag we bypass the debounce and redraw
    // directly in OnCanvasMouseMove so feedback remains immediate.
    void OnPointPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_dragPoint != null) return; // drag path redraws synchronously
        _redrawDebounce.Stop();
        _redrawDebounce.Start();
    }

    // ── Coordinate conversion ──────────────────────────────────────────────

    double NormB(double brightness) => (brightness - MinBrightness) / Math.Max(1, 100.0 - MinBrightness);

    Point ToCanvas(CurvePoint p) =>
        new(PadL + (1.0 - NormB(p.Brightness)) * PlotW,
            PadT + (1.0 - p.SdrValue / 100.0)  * PlotH);

    (double b, double s) FromCanvas(Point cp) =>
    (
        Math.Clamp(MinBrightness + (1.0 - (cp.X - PadL) / PlotW) * (100.0 - MinBrightness), MinBrightness, 100),
        Math.Clamp((1.0 - (cp.Y - PadT) / PlotH) * 100.0, 0, 100)
    );

    Color Accent => TryFindResource("SystemAccentColor") is Color c ? c : Color.FromRgb(0, 120, 212);

    private SolidColorBrush AccentBrush()
    {
        var a = Accent;
        if (_accentBrush == null || _lastAccent != a)
        {
            _lastAccent      = a;
            _accentBrush     = new SolidColorBrush(a);      _accentBrush.Freeze();
            _accentFillBrush = new SolidColorBrush(Color.FromArgb(35, a.R, a.G, a.B)); _accentFillBrush.Freeze();
        }
        return _accentBrush;
    }

    private SolidColorBrush AccentFillBrush() { AccentBrush(); return _accentFillBrush!; }

    // ── Drawing ────────────────────────────────────────────────────────────

    void Redraw() { if (!IsLoaded || ActualWidth < 1) return; RedrawCore(); }

    void RedrawCore()
    {
        var visible = Points?.Where(p => p.Brightness >= MinBrightness && p.Brightness <= 100)
                             .OrderBy(p => p.Brightness).ToList();

        EnsurePersistentElements();
        UpdateGrid();
        UpdateAxisLabels();
        if (visible is { Count: > 1 })
            DrawCurve(visible);
        else
        {
            if (_fillPolygon   != null) _fillPolygon.Visibility   = Visibility.Collapsed;
            if (_curvePolyline != null) _curvePolyline.Visibility = Visibility.Collapsed;
        }
        UpdatePoints(visible);
        UpdateActiveIndicator();
    }

    void UpdateActiveIndicator()
    {
        if (_activeRing == null) return;

        var ap = ActivePoint;
        if (ap == null || !ShowPoints || Points == null ||
            ap.Brightness < MinBrightness || ap.Brightness > 100)
        {
            _activeRing.Visibility = Visibility.Collapsed;
            return;
        }

        var cp = ToCanvas(ap);
        const double r = 13;
        _activeRing.Stroke     = AccentBrush();
        _activeRing.Width      = r * 2;
        _activeRing.Height     = r * 2;
        _activeRing.Visibility = Visibility.Visible;
        Canvas.SetLeft(_activeRing, cp.X - r);
        Canvas.SetTop(_activeRing, cp.Y - r);
    }

    // Creates all static canvas elements exactly once; subsequent calls are no-ops.
    void EnsurePersistentElements()
    {
        if (_persistentElementsReady) return;

        _gridLines = new Line[22];
        for (int i = 0; i < 22; i++)
        {
            var line = new Line { StrokeThickness = 1 };
            Panel.SetZIndex(line, 0);
            _gridLines[i] = line;
            MainCanvas.Children.Add(line);
        }

        _xLabels = new TextBlock[6];
        _yLabels = new TextBlock[6];
        for (int i = 0; i < 6; i++)
        {
            _xLabels[i] = MakeLabel("", 10);
            _yLabels[i] = MakeLabel("", 10);
            Panel.SetZIndex(_xLabels[i], 1);
            Panel.SetZIndex(_yLabels[i], 1);
            MainCanvas.Children.Add(_xLabels[i]);
            MainCanvas.Children.Add(_yLabels[i]);
        }

        _xTitle = MakeLabel("Screen Brightness (%)", 10.5);
        _yTitle = MakeLabel("Nits", 10.5);
        _yTitle.RenderTransform       = new RotateTransform(-90);
        _yTitle.RenderTransformOrigin = new Point(0.5, 0.5);
        Panel.SetZIndex(_xTitle, 1);
        Panel.SetZIndex(_yTitle, 1);
        MainCanvas.Children.Add(_xTitle);
        MainCanvas.Children.Add(_yTitle);

        _fillPolygon = new Polygon { Visibility = Visibility.Collapsed };
        Panel.SetZIndex(_fillPolygon, 2);
        MainCanvas.Children.Add(_fillPolygon);

        _curvePolyline = new Polyline
        {
            StrokeThickness = 2.5,
            StrokeLineJoin  = PenLineJoin.Round,
            Visibility      = Visibility.Collapsed
        };
        Panel.SetZIndex(_curvePolyline, 3);
        MainCanvas.Children.Add(_curvePolyline);

        _activeRing = new Ellipse
        {
            IsHitTestVisible  = false,
            Fill              = Brushes.Transparent,
            StrokeThickness   = 2,
            StrokeDashArray   = ActiveDash,
            Visibility        = Visibility.Collapsed
        };
        Panel.SetZIndex(_activeRing, 11);
        MainCanvas.Children.Add(_activeRing);

        _persistentElementsReady = true;
    }

    void UpdateGrid()
    {
        for (int i = 0; i <= 10; i++)
        {
            bool major = i % 5 == 0;
            var  br    = major ? GridMedium : GridFaint;
            double x = PadL + i * PlotW / 10;
            double y = PadT + i * PlotH / 10;

            var vLine = _gridLines![i * 2];
            vLine.X1 = x; vLine.Y1 = PadT; vLine.X2 = x; vLine.Y2 = PadT + PlotH;
            vLine.Stroke = br; vLine.StrokeDashArray = major ? null : GridDash;

            var hLine = _gridLines[i * 2 + 1];
            hLine.X1 = PadL; hLine.Y1 = y; hLine.X2 = PadL + PlotW; hLine.Y2 = y;
            hLine.Stroke = br; hLine.StrokeDashArray = major ? null : GridDash;
        }
    }

    // sorted is already sorted ascending by Redraw(); no re-sort needed.
    void DrawCurve(List<CurvePoint> sorted)
    {
        var fillPts = new PointCollection { new(ToCanvas(sorted[0]).X, PadT + PlotH) };
        foreach (var p in sorted) fillPts.Add(ToCanvas(p));
        fillPts.Add(new(ToCanvas(sorted[^1]).X, PadT + PlotH));
        _fillPolygon!.Points    = fillPts;
        _fillPolygon.Fill       = AccentFillBrush();
        _fillPolygon.Visibility = Visibility.Visible;

        _curvePolyline!.Points     = new PointCollection(sorted.Select(ToCanvas));
        _curvePolyline.Stroke      = AccentBrush();
        _curvePolyline.Visibility  = Visibility.Visible;
    }

    // Reuses pooled Ellipses; creates or removes only when the visible set changes.
    void UpdatePoints(List<CurvePoint>? visible)
    {
        var visibleSet = (visible != null && ShowPoints)
            ? new HashSet<CurvePoint>(visible) : [];

        // Remove ellipses for points that are no longer visible / shown.
        var toRemove = _pointEllipses.Keys.Where(k => !visibleSet.Contains(k)).ToList();
        foreach (var pt in toRemove)
        {
            MainCanvas.Children.Remove(_pointEllipses[pt]);
            _pointEllipses.Remove(pt);
        }

        if (visibleSet.Count == 0) return;

        var ab = AccentBrush();
        foreach (var pt in visible!)
        {
            bool   selected = pt == SelectedPoint;
            double r        = selected ? 9 : 7;
            var    cp       = ToCanvas(pt);

            if (!_pointEllipses.TryGetValue(pt, out var el))
            {
                el = new Ellipse { Cursor = Cursors.Hand, Tag = pt };
                el.MouseLeftButtonDown  += OnPointMouseDown;
                el.MouseRightButtonDown += OnPointRightClick;
                Panel.SetZIndex(el, 10);
                MainCanvas.Children.Add(el);
                _pointEllipses[pt] = el;
            }

            el.Width           = r * 2;
            el.Height          = r * 2;
            el.Fill            = selected ? ab : Brushes.White;
            el.Stroke          = ab;
            el.StrokeThickness = selected ? 3 : 2;
            el.ToolTip         = $"Brightness: {(int)pt.Brightness}%   SDR: {(int)pt.SdrValue}   ({pt.Nits} nits)";
            Canvas.SetLeft(el, cp.X - r);
            Canvas.SetTop(el, cp.Y - r);
        }
    }

    void UpdateAxisLabels()
    {
        var fg = (Brush)(TryFindResource("TextFillColorSecondaryBrush")
            ?? new SolidColorBrush(Color.FromArgb(180, 180, 180, 180)));

        // Use pre-computed font metrics for Segoe UI 10pt to avoid calling Measure()
        // on every Redraw — those calls block the UI thread and drop animation frames.
        const double charW  = 6.5;   // average glyph advance width (DIP)
        const double labelH = 14.0;  // single-line cap height + descenders (DIP)

        double range = 100.0 - MinBrightness;

        for (int i = 0; i <= 5; i++)
        {
            double b    = MinBrightness + i * range / 5;
            string text = $"{(int)Math.Round(b)}%";
            double canvasX = PadL + (1.0 - (b - MinBrightness) / range) * PlotW;
            var tb = _xLabels![i];
            tb.Text = text; tb.Foreground = fg;
            Canvas.SetLeft(tb, canvasX - text.Length * charW / 2);
            Canvas.SetTop(tb, PadT + PlotH + 5);
        }

        for (int i = 0; i <= 5; i++)
        {
            int    sdr    = i * 20;
            int    nits   = 80 + sdr * 4;
            string text   = $"{nits}";
            double canvasY = PadT + (1.0 - sdr / 100.0) * PlotH;
            var tb = _yLabels![i];
            tb.Text = text; tb.Foreground = fg;
            Canvas.SetLeft(tb, PadL - text.Length * charW - 5);
            Canvas.SetTop(tb, canvasY - labelH / 2);
        }

        _xTitle!.Foreground = fg;
        Canvas.SetLeft(_xTitle, PadL + PlotW / 2 - _xTitle.Text.Length * 6.3 / 2);
        Canvas.SetTop(_xTitle, PadT + PlotH + 22);

        _yTitle!.Foreground = fg;
        Canvas.SetLeft(_yTitle, 2);
        Canvas.SetTop(_yTitle, PadT + PlotH / 2 + _yTitle.Text.Length * 6.3 / 2);
    }

    TextBlock MakeLabel(string text, double size) =>
        new() { Text = text, FontFamily = SegoeUI, FontSize = size };

    // ── Mouse handling ─────────────────────────────────────────────────────

    void OnPointMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not CurvePoint pt) return;
        if (IsReadOnly) { e.Handled = true; return; }
        SelectedPoint   = pt;
        _dragPoint      = pt;
        _dragIsEndpoint = pt.Brightness <= MinBrightness || pt.Brightness >= 100;
        Mouse.Capture(MainCanvas);
        e.Handled = true;
    }

    void OnPointRightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly) return;
        if (sender is not FrameworkElement el || el.Tag is not CurvePoint pt) return;
        if (pt.Brightness <= MinBrightness || pt.Brightness >= 100) return;
        RemovePointRequested?.Invoke(this, pt);
        e.Handled = true;
    }

    void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly) { SelectedPoint = null; return; }
        if (e.ClickCount == 2 && Points != null)
        {
            var (b, s) = FromCanvas(e.GetPosition(MainCanvas));
            AddPointRequested?.Invoke(this, new CurvePoint(b, s));
        }
        else if (e.ClickCount == 1)
        {
            SelectedPoint = null;
        }
    }

    void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (IsReadOnly) return;
        if (_dragPoint == null || !MainCanvas.IsMouseCaptured) return;
        var (b, s) = FromCanvas(e.GetPosition(MainCanvas));

        s = Math.Round(Math.Clamp(s, 0, 100));

        if (_dragIsEndpoint)
        {
            if (_dragPoint.Brightness >= 100) _dragPoint.SdrValue = s;
        }
        else
        {
            var sorted = Points!.Where(p => p.Brightness >= MinBrightness && p.Brightness <= 100)
                                .OrderBy(p => p.Brightness).ToList();
            int idx = sorted.IndexOf(_dragPoint);
            double minB = idx > 0               ? sorted[idx - 1].Brightness + 1 : MinBrightness + 1;
            double maxB = idx < sorted.Count - 1 ? sorted[idx + 1].Brightness - 1 : 99;
            if (minB > maxB) return;
            _dragPoint.Brightness = Math.Round(Math.Clamp(b, minB, maxB));
            _dragPoint.SdrValue   = s;
        }

        // Bypass debounce during drag — immediate visual feedback is needed.
        _redrawDebounce.Stop();
        Redraw();
    }

    void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragPoint != null) { CurveMoved?.Invoke(this, EventArgs.Empty); _dragPoint = null; }
        Mouse.Capture(null);
    }
}
