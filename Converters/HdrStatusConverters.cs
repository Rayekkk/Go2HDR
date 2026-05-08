using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Go2HDR.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class HdrStatusColorConverter : IValueConverter
{
    static readonly SolidColorBrush Active   = new(Color.FromRgb(76, 175, 80));
    static readonly SolidColorBrush Inactive = new(Color.FromRgb(128, 128, 128));

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? Active : Inactive;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

[ValueConversion(typeof(bool), typeof(Brush))]
public class HdrStatusBackgroundConverter : IValueConverter
{
    static readonly SolidColorBrush Active   = new(Color.FromArgb(34, 76, 175, 80));
    static readonly SolidColorBrush Inactive = new(Color.FromArgb(34, 128, 128, 128));

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? Active : Inactive;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
