using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hypershell.Infrastructure;

public sealed class DepthToIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var depth = value is int entryDepth ? entryDepth : 0;
        return new Thickness(Math.Max(0, depth) * 18, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
