using System.Globalization;
using System.Windows.Data;

namespace Go2HDR.Converters;

[ValueConversion(typeof(double), typeof(string))]
public class SdrToNitsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return $"{80 + (int)d * 4} nits";
        return "—";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
