using Hypershell.Models;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed class AtlasMyNetWebService : IDisposable
{
    private const int MaximumProbeBodyBytes = 64 * 1024;
    private static readonly Regex TitleExpression = new(
        @"<title\b[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly HttpClient _client;
    private bool _isDisposed;

    public AtlasMyNetWebService()
    {
        _client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Hypershell-AtlasMyNet/1.0");
    }

    public async Task<AtlasMyNetDiscoveredWebApp?> ProbeListenerAsync(
        NetworkSocketSnapshot socket,
        CancellationToken cancellationToken = default)
    {
        if (!socket.IsListener || socket.LocalPort <= 0)
        {
            return null;
        }

        var host = NormalizeListenerAddress(
            socket.LocalAddress,
            socket.AddressFamily);
        foreach (var scheme in new[] { "http", "https" })
        {
            var url = BuildUrl(scheme, host, socket.LocalPort, "/");
            var probe = await ProbeAsync(
                url,
                expectedStatusCode: null,
                headerName: string.Empty,
                headerValue: string.Empty,
                TimeSpan.FromMilliseconds(850),
                includeTitle: true,
                cancellationToken);
            if (!probe.Succeeded)
            {
                continue;
            }

            var displayName = !string.IsNullOrWhiteSpace(probe.Title)
                ? WebUtility.HtmlDecode(probe.Title).Trim()
                : !string.IsNullOrWhiteSpace(socket.ProcessName)
                    ? socket.ProcessName
                    : $"Local web service on {socket.LocalPort}";
            return new AtlasMyNetDiscoveredWebApp(
                socket.Key,
                displayName,
                url,
                socket.ProcessId,
                socket.ProcessName,
                probe.StatusCode,
                DateTimeOffset.UtcNow);
        }

        return null;
    }

    public async Task<bool> CheckHealthAsync(
        AtlasMyNetAppDefinition app,
        int port,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveUrl(app, port);
        var health = app.HealthCheck;
        var healthUrl = health is null
            ? baseUrl
            : new Uri(new Uri(baseUrl), NormalizePath(health.Path)).AbsoluteUri;
        var probe = await ProbeAsync(
            healthUrl,
            health?.ExpectedStatusCode ?? 200,
            health?.HeaderName ?? string.Empty,
            health?.HeaderValue ?? string.Empty,
            TimeSpan.FromSeconds(2),
            includeTitle: false,
            cancellationToken);
        return probe.Succeeded;
    }

    public static string ResolveUrl(AtlasMyNetAppDefinition app, int port) =>
        app.UrlTemplate.Replace(
            "{port}",
            port.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static string NormalizeListenerAddress(
        string address,
        AddressFamily family)
    {
        if (string.IsNullOrWhiteSpace(address)
            || string.Equals(address, "0.0.0.0", StringComparison.Ordinal)
            || string.Equals(address, "::", StringComparison.Ordinal))
        {
            return family == AddressFamily.InterNetworkV6 ? "::1" : "127.0.0.1";
        }

        return address;
    }

    private async Task<WebProbeResult> ProbeAsync(
        string url,
        int? expectedStatusCode,
        string headerName,
        string headerValue,
        TimeSpan timeout,
        bool includeTitle,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            var statusCode = (int)response.StatusCode;
            if (expectedStatusCode.HasValue
                && statusCode != expectedStatusCode.Value)
            {
                return new WebProbeResult(false, statusCode, string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(headerName))
            {
                var values = response.Headers.TryGetValues(headerName, out var responseValues)
                    ? responseValues
                    : response.Content.Headers.TryGetValues(
                        headerName,
                        out var contentValues)
                        ? contentValues
                        : [];
                if (!values.Any(value => string.Equals(
                        value,
                        headerValue,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return new WebProbeResult(false, statusCode, string.Empty);
                }
            }

            if (!includeTitle)
            {
                return new WebProbeResult(true, statusCode, string.Empty);
            }

            var title = await ReadTitleAsync(response, timeoutSource.Token);
            return new WebProbeResult(true, statusCode, title);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                           or IOException
                                           or TaskCanceledException
                                           or UriFormatException
                                           or RegexMatchTimeoutException)
        {
            return new WebProbeResult(false, 0, string.Empty);
        }
    }

    private static async Task<string> ReadTitleAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (output.Length < MaximumProbeBodyBytes)
        {
            var remaining = (int)Math.Min(
                buffer.Length,
                MaximumProbeBodyBytes - output.Length);
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, remaining),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
        }

        var html = Encoding.UTF8.GetString(output.ToArray());
        var match = TitleExpression.Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string BuildUrl(
        string scheme,
        string host,
        int port,
        string path) =>
        new UriBuilder(scheme, host, port, NormalizePath(path)).Uri.AbsoluteUri;

    private static string NormalizePath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? "/"
            : path.StartsWith("/", StringComparison.Ordinal)
                ? path
                : "/" + path;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.Dispose();
    }

    private sealed record WebProbeResult(
        bool Succeeded,
        int StatusCode,
        string Title);
}
