using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Go2HDR.Services;

namespace Go2HDR.Converters;

[ValueConversion(typeof(BuiltInHdrState), typeof(Brush))]
public class HdrStatusColorConverter : IValueConverter
{
    static readonly SolidColorBrush Active = Freeze(new SolidColorBrush(Color.FromRgb(76, 175, 80)));
    static readonly SolidColorBrush Inactive = Freeze(new SolidColorBrush(Color.FromRgb(128, 128, 128)));
    static readonly SolidColorBrush Unavailable = Freeze(new SolidColorBrush(Color.FromRgb(255, 167, 38)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            BuiltInHdrState.Active => Active,
            BuiltInHdrState.Inactive => Inactive,
            _ => Unavailable
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

[ValueConversion(typeof(BuiltInHdrState), typeof(Brush))]
public class HdrStatusBackgroundConverter : IValueConverter
{
    static readonly SolidColorBrush Active = Freeze(new SolidColorBrush(Color.FromArgb(34, 76, 175, 80)));
    static readonly SolidColorBrush Inactive = Freeze(new SolidColorBrush(Color.FromArgb(34, 128, 128, 128)));
    static readonly SolidColorBrush Unavailable = Freeze(new SolidColorBrush(Color.FromArgb(34, 255, 167, 38)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            BuiltInHdrState.Active => Active,
            BuiltInHdrState.Inactive => Inactive,
            _ => Unavailable
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
