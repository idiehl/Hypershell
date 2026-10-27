using Hypershell.ViewModels;
using System.IO;

namespace Hypershell.Services;

public static class BrowserAddressService
{
    public static string Normalize(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return BrowserTabViewModel.DefaultHomeAddress;
        }

        if (File.Exists(value))
        {
            return new Uri(Path.GetFullPath(value)).AbsoluteUri;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https" or "file" or "about")
        {
            return absolute.AbsoluteUri;
        }

        if (!value.Contains(' ')
            && (value.StartsWith(
                    "localhost",
                    StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("127.0.0.1", StringComparison.Ordinal)
                || value.StartsWith("[::1]", StringComparison.Ordinal)))
        {
            return $"http://{value}";
        }

        if (!value.Contains(' ') && value.Contains('.'))
        {
            return $"https://{value}";
        }

        return $"https://www.google.com/search?q={Uri.EscapeDataString(value)}";
    }
}
