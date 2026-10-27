using System.Text.Json.Serialization;
using System.IO;

namespace Hypershell.Models;

public static class WorkspaceProviderIds
{
    public const string Local = "local";
    public const string Ssh = "ssh";
}

/// <summary>
/// Provider-neutral identity for a local or remote workspace object. The
/// canonical URI is stable across UI navigation and must be used instead of
/// passing an SSH path through System.IO.
/// </summary>
public sealed record WorkspaceLocation
{
    public required string ProviderId { get; init; }

    public string Authority { get; init; } = string.Empty;

    public required string Path { get; init; }

    [JsonIgnore]
    public bool IsLocal => string.Equals(
        ProviderId,
        WorkspaceProviderIds.Local,
        StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsSsh => string.Equals(
        ProviderId,
        WorkspaceProviderIds.Ssh,
        StringComparison.Ordinal);

    [JsonIgnore]
    public string Identity => BuildIdentity(this);

    public static WorkspaceLocation Local(string path)
    {
        var normalized = System.IO.Path.GetFullPath(
            path?.Trim()
            ?? throw new ArgumentNullException(nameof(path)));
        return new WorkspaceLocation
        {
            ProviderId = WorkspaceProviderIds.Local,
            Path = normalized
        };
    }

    public static WorkspaceLocation Ssh(string hostId, string path) =>
        new()
        {
            ProviderId = WorkspaceProviderIds.Ssh,
            Authority = NormalizeAuthority(hostId),
            Path = NormalizeRemotePath(path)
        };

    public static WorkspaceLocation Parse(string identity)
    {
        if (!TryParse(identity, out var location))
        {
            throw new FormatException(
                "The workspace location URI is malformed or unsupported.");
        }

        return location;
    }

    public static bool TryParse(
        string? identity,
        out WorkspaceLocation location)
    {
        location = null!;
        if (string.IsNullOrWhiteSpace(identity)
            || !Uri.TryCreate(
                identity.Trim(),
                UriKind.Absolute,
                out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        try
        {
            if (string.Equals(
                    uri.Scheme,
                    WorkspaceProviderIds.Local,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(uri.Host))
                {
                    return false;
                }

                var value = Uri.UnescapeDataString(uri.AbsolutePath);
                if (OperatingSystem.IsWindows()
                    && value.Length >= 3
                    && value[0] == '/'
                    && char.IsAsciiLetter(value[1])
                    && value[2] == ':')
                {
                    value = value[1..];
                }

                location = Local(value.Replace('/', System.IO.Path.DirectorySeparatorChar));
                return true;
            }

            if (string.Equals(
                    uri.Scheme,
                    WorkspaceProviderIds.Ssh,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(uri.Host))
            {
                var value = Uri.UnescapeDataString(uri.AbsolutePath);
                if (value.Length >= 4
                    && value[0] == '/'
                    && char.IsAsciiLetter(value[1])
                    && value[2] == ':'
                    && value[3] == '/')
                {
                    value = value[1..];
                }

                location = Ssh(uri.Host, value);
                return true;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException
                                          or UriFormatException)
        {
            return false;
        }

        return false;
    }

    public static bool SameIdentity(
        WorkspaceLocation left,
        WorkspaceLocation right) =>
        string.Equals(
            left.Identity,
            right.Identity,
            left.IsLocal && right.IsLocal
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string BuildIdentity(WorkspaceLocation location)
    {
        if (location.IsLocal)
        {
            var path = System.IO.Path.GetFullPath(location.Path)
                .Replace('\\', '/');
            return "local:///" + EscapePath(path.TrimStart('/'));
        }

        if (location.IsSsh)
        {
            var path = NormalizeRemotePath(location.Path);
            return "ssh://"
                   + NormalizeAuthority(location.Authority)
                   + "/"
                   + EscapePath(path.TrimStart('/'));
        }

        throw new InvalidOperationException(
            $"Unsupported workspace provider '{location.ProviderId}'.");
    }

    private static string EscapePath(string path) =>
        string.Join(
            '/',
            path.Split('/', StringSplitOptions.None)
                .Select(segment => Uri.EscapeDataString(segment)
                    .Replace("%3A", ":", StringComparison.OrdinalIgnoreCase)));

    private static string NormalizeAuthority(string authority)
    {
        var value = authority?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length is < 1 or > 128
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException(
                "A workspace authority must contain 1-128 letters, numbers, periods, underscores, or hyphens.",
                nameof(authority));
        }

        return value;
    }

    internal static string NormalizeRemotePath(string path)
    {
        var value = path?.Trim().Replace('\\', '/')
                    ?? throw new ArgumentNullException(nameof(path));
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "A remote workspace path is required.",
                nameof(path));
        }

        var hasDrive = value.Length >= 2
                       && char.IsAsciiLetter(value[0])
                       && value[1] == ':';
        if (!hasDrive && !value.StartsWith('/'))
        {
            throw new ArgumentException(
                "A remote workspace path must be absolute.",
                nameof(path));
        }

        var prefix = hasDrive
            ? char.ToUpperInvariant(value[0]) + ":"
            : "/";
        var remainder = hasDrive
            ? value[2..].TrimStart('/')
            : value.TrimStart('/');
        var segments = new List<string>();
        foreach (var segment in remainder.Split(
                     '/',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException(
                        "A remote workspace path cannot traverse above its root.",
                        nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var joined = string.Join('/', segments);
        return hasDrive
            ? prefix + "/" + joined
            : segments.Count == 0
                ? "/"
                : "/" + joined;
    }
}

public sealed record WorkspaceEntry
{
    public required WorkspaceLocation Location { get; init; }

    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    public bool IsSymbolicLink { get; init; }

    public bool IsHidden { get; init; }

    public long Length { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public string Version { get; init; } = string.Empty;
}

public sealed class WorkspaceFileContent : IDisposable
{
    public required WorkspaceLocation Location { get; init; }

    public required byte[] Content { get; init; }

    public required string Version { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public void Dispose() =>
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(
            Content);
}

public sealed record WorkspaceWriteOptions
{
    public string ExpectedVersion { get; init; } = string.Empty;

    public bool AllowOverwriteWithoutVersion { get; init; }
}

public sealed record WorkspaceDeleteConfirmation
{
    public required string ConfirmedLocationIdentity { get; init; }

    public bool ConfirmPermanentDeletion { get; init; }

    public bool Recursive { get; init; }
}

public enum WorkspaceFailureKind
{
    InvalidLocation,
    NotFound,
    AlreadyExists,
    AccessDenied,
    Conflict,
    Connection,
    HostKeyMismatch,
    CredentialUnavailable,
    SizeLimit,
    Unsupported
}

public sealed class WorkspaceFileSystemException : Exception
{
    public WorkspaceFileSystemException(
        WorkspaceFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public WorkspaceFailureKind Kind { get; }
}
