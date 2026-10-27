using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Hypershell.Infrastructure;

public sealed class FontNameToFontFamilyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fontName = value as string;
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return SystemFonts.MessageFontFamily;
        }

        if (string.Equals(fontName, "VT323", StringComparison.OrdinalIgnoreCase)
            && Application.Current?.TryFindResource("TerminalFontFamily") is FontFamily terminalFontFamily)
        {
            return terminalFontFamily;
        }

        if (string.Equals(fontName, "JetBrains Mono", StringComparison.OrdinalIgnoreCase)
            && Application.Current?.TryFindResource("CodeFontFamily") is FontFamily codeFontFamily)
        {
            return codeFontFamily;
        }

        try
        {
            return new FontFamily(fontName);
        }
        catch
        {
            return SystemFonts.MessageFontFamily;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
