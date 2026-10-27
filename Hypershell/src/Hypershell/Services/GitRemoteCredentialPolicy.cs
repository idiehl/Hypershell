using System.IO;

namespace Hypershell.Services;

internal static class GitRemoteCredentialPolicy
{
    private static readonly HashSet<string> CredentialQueryNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "accesskey",
        "accesstoken",
        "apikey",
        "auth",
        "authorization",
        "key",
        "password",
        "passwd",
        "secret",
        "sig",
        "signature",
        "token"
    };

    public const string EmbeddedCredentialMessage =
        "Remote URLs must not embed credentials. Use Git Credential Manager for HTTPS remotes or SSH key authentication.";

    public static void EnsureCredentialFree(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            var isHttp = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                         || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            if ((!string.IsNullOrWhiteSpace(uri.UserInfo)
                 && (isHttp || uri.UserInfo.Contains(':')))
                || HasCredentialQuery(uri.Query))
            {
                throw new ArgumentException(EmbeddedCredentialMessage);
            }

            return;
        }

        // Preserve normal SCP-style SSH remotes such as git@host:owner/repo,
        // while rejecting the password-bearing user:secret@host:path form.
        var at = candidate.IndexOf('@');
        if (at > 0
            && candidate[..at].Contains(':')
            && !Path.IsPathFullyQualified(candidate))
        {
            throw new ArgumentException(EmbeddedCredentialMessage);
        }
    }

    public static string RedactForDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            var at = candidate.IndexOf('@');
            return at > 0
                   && candidate[..at].Contains(':')
                   && !Path.IsPathFullyQualified(candidate)
                ? "[credential-redacted remote]"
                : value;
        }

        var isHttp = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                     || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(uri.UserInfo)
            && string.IsNullOrWhiteSpace(uri.Query)
            && string.IsNullOrWhiteSpace(uri.Fragment))
        {
            return value;
        }

        try
        {
            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };
            if (isHttp || uri.UserInfo.Contains(':'))
            {
                builder.UserName = string.Empty;
                builder.Password = string.Empty;
            }

            return builder.Uri.AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return "[credential-redacted remote]";
        }
    }

    private static bool HasCredentialQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        foreach (var segment in query.TrimStart('?').Split('&'))
        {
            var separator = segment.IndexOf('=');
            var encodedName = separator < 0 ? segment : segment[..separator];
            string name;
            try
            {
                name = Uri.UnescapeDataString(encodedName);
            }
            catch (UriFormatException)
            {
                return true;
            }

            var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (CredentialQueryNames.Contains(normalized))
            {
                return true;
            }
        }

        return false;
    }
}
