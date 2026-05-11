using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Go2HDR.Views.Controls;

public partial class SdrSpinner : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(SdrSpinner),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((SdrSpinner)d).OnExternalValueChange()));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SdrSpinner),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SdrSpinner),
            new PropertyMetadata(100.0));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
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

    public SdrSpinner()
    {
        InitializeComponent();
        SyncText(); // Value DP defaults to 0.0 — if binding also sets 0, WPF skips the change callback
    }

    private void OnExternalValueChange()
    {
        if (!ValueBox.IsFocused) SyncText();
    }

    private void SyncText()
    {
        string s = IntVal.ToString();
        if (ValueBox.Text != s) ValueBox.Text = s;
    }

    private int IntVal => (int)Math.Round(Value);

    // ── User interactions ────────────────────────────────────────────────────

    private void OnDecrement(object sender, RoutedEventArgs e) => Step(-1);
    private void OnIncrement(object sender, RoutedEventArgs e) => Step(+1);

    private void Step(int delta) => Value = Math.Clamp(Value + delta, Minimum, Maximum);

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ValueBox.IsFocused) return;
        if (int.TryParse(ValueBox.Text, out int v))
            SetValue(ValueProperty, (double)Math.Clamp(v, Minimum, Maximum));
    }

    private void OnLostFocus(object sender, RoutedEventArgs e) => SyncText();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)   { Step(+1); SyncText(); e.Handled = true; }
        if (e.Key == Key.Down) { Step(-1); SyncText(); e.Handled = true; }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Step(Math.Sign(e.Delta));
        SyncText();
        e.Handled = true;
    }
}
