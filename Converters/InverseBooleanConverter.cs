using System.Globalization;
using System.Windows.Data;

namespace Mosaic.Converters;

/// <summary>Negates a boolean (e.g. enable a button while a flag is false).</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
