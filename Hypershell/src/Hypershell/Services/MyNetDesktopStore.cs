using Hypershell.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed class MyNetDesktopStore
{
    private const int MaximumStateBytes = 1024 * 1024;
    private const int MaximumIcons = 4096;
    private const double MaximumIconY = 100000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _sync = new();
    private bool _writeBlocked;

    public MyNetDesktopStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StatePath = Path.GetFullPath(path);
    }

    public string StatePath { get; }

    public MyNetDesktopState Load()
    {
        lock (_sync)
        {
            try
            {
                var state = ReadExisting();
                _writeBlocked = false;
                return state;
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or ArgumentException)
            {
                _writeBlocked = true;
                throw CreateReadError(exception);
            }
        }
    }

    public void Save(MyNetDesktopState state)
    {
        var normalized = NormalizeAndValidate(state);
        lock (_sync)
        {
            if (_writeBlocked)
            {
                throw new InvalidDataException(
                    $"MyNet desktop settings at '{StatePath}' could not be read. "
                    + "The original file is preserved. Repair or move it and reload before saving.");
            }

            // A corrupt file introduced since Load must also remain recoverable.
            try
            {
                _ = ReadExisting();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or ArgumentException)
            {
                _writeBlocked = true;
                throw CreateReadError(exception);
            }

            WriteAtomically(JsonSerializer.Serialize(normalized, JsonOptions));
        }
    }

    public static MyNetDesktopState NormalizeAndValidate(MyNetDesktopState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != MyNetDesktopState.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"MyNet desktop settings schema {state.SchemaVersion} is not supported.");
        }

        if (!Enum.IsDefined(state.BackgroundMode))
        {
            throw new InvalidDataException("MyNet desktop background mode is invalid.");
        }

        var imagePath = (state.BackgroundImagePath ?? string.Empty).Trim();
        if (imagePath.Length > 0)
        {
            if (!Path.IsPathFullyQualified(imagePath)
                || imagePath.StartsWith(@"\\", StringComparison.Ordinal)
                || imagePath.Contains('\0'))
            {
                throw new InvalidDataException(
                    "MyNet desktop backgrounds must use a full path to an image on this device.");
            }

            try
            {
                imagePath = Path.GetFullPath(imagePath);
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
            {
                throw new InvalidDataException("The MyNet desktop background path is invalid.", exception);
            }
        }

        if (state.BackgroundMode == MyNetDesktopBackgroundMode.Image
            && imagePath.Length == 0)
        {
            throw new InvalidDataException("Choose an image for the MyNet desktop background.");
        }

        var normalized = new MyNetDesktopState
        {
            BackgroundMode = state.BackgroundMode,
            BackgroundImagePath = imagePath,
            Left = NormalizeCoordinate(state.Left),
            Top = NormalizeCoordinate(state.Top),
            Width = NormalizeDimension(state.Width, 360),
            Height = NormalizeDimension(state.Height, 720)
        };
        if (state.Icons is null)
        {
            return normalized;
        }

        if (state.Icons.Count > MaximumIcons)
        {
            throw new InvalidDataException("MyNet desktop settings contain too many icon positions.");
        }

        foreach (var (appId, position) in state.Icons)
        {
            var key = appId.Trim();
            if (key.Length is 0 or > 256
                || !normalized.Icons.TryAdd(
                    key,
                    // The desktop's canvas scrolls vertically and can exceed the window.
                    // Preserve those rows; the window clamps against its actual canvas.
                    ClampIcon(position, normalized.Width, MaximumIconY + 94)))
            {
                throw new InvalidDataException(
                    "MyNet desktop icon positions contain an invalid or repeated app ID.");
            }
        }

        return normalized;
    }

    public static MyNetDesktopIconPosition ClampIcon(
        MyNetDesktopIconPosition? position,
        double canvasWidth,
        double canvasHeight,
        double iconWidth = 88,
        double iconHeight = 94)
    {
        var width = double.IsFinite(canvasWidth) ? Math.Max(0, canvasWidth) : 0;
        var height = double.IsFinite(canvasHeight) ? Math.Max(0, canvasHeight) : 0;
        var tileWidth = double.IsFinite(iconWidth) ? Math.Max(0, iconWidth) : 88;
        var tileHeight = double.IsFinite(iconHeight) ? Math.Max(0, iconHeight) : 94;
        return new MyNetDesktopIconPosition
        {
            X = Math.Clamp(
                position is not null && double.IsFinite(position.X) ? position.X : 0,
                0,
                Math.Max(0, width - tileWidth)),
            Y = Math.Clamp(
                position is not null && double.IsFinite(position.Y) ? position.Y : 0,
                0,
                Math.Max(0, height - tileHeight))
        };
    }

    private MyNetDesktopState ReadExisting()
    {
        if (!File.Exists(StatePath))
        {
            return new MyNetDesktopState();
        }

        using var stream = new FileStream(
            StatePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length > MaximumStateBytes)
        {
            throw new InvalidDataException("MyNet desktop settings exceed the supported size.");
        }

        var state = JsonSerializer.Deserialize<MyNetDesktopState>(stream, JsonOptions)
                    ?? throw new InvalidDataException("MyNet desktop settings are empty.");
        return NormalizeAndValidate(state);
    }

    private void WriteAtomically(string content)
    {
        var bytes = new UTF8Encoding(false).GetBytes(content);
        if (bytes.Length > MaximumStateBytes)
        {
            throw new InvalidDataException("MyNet desktop settings exceed the supported size.");
        }

        var directory = Path.GetDirectoryName(StatePath)
                        ?? throw new InvalidDataException("MyNet desktop settings require a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(StatePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StatePath))
            {
                File.Replace(temporaryPath, StatePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private InvalidDataException CreateReadError(Exception exception) => new(
        $"MyNet desktop could not read '{StatePath}': {exception.Message} "
        + "The original file is preserved; desktop changes will not overwrite it.",
        exception);

    private static double NormalizeDimension(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 240, 16000) : fallback;

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate)
            ? Math.Clamp(coordinate, -100000, 100000)
            : null;
}
