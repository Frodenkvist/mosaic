using System.Globalization;
using System.Windows.Data;

namespace Mosaic.Converters;

/// <summary>
/// Returns the string "Active" when the bound section name equals the converter
/// parameter, otherwise null. Used to drive the navigation rail's active state.
/// </summary>
public class SectionActiveConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal) ? "Active" : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
