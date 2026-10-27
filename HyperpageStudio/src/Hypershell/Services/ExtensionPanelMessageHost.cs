using Hypershell.Extensibility;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed record ExtensionPanelMessageOutcome(
    string ResponseJson,
    bool IsSuccess,
    string Diagnostic);

public sealed class ExtensionPanelMessageHost
{
    private static readonly Regex RequestIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex MethodPattern = new(
        "^[a-z][A-Za-z0-9.]{1,63}$",
        RegexOptions.CultureInvariant);
    private static readonly IReadOnlySet<string> RequestPropertyNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "protocol",
            "version",
            "type",
            "id",
            "method",
            "params"
        };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32
    };

    private readonly object _sync = new();
    private readonly ExtensionPanelCandidate _candidate;
    private readonly IReadOnlySet<ExtensionCapability> _approvedCapabilities;
    private readonly string _expectedVirtualHostName;
    private readonly Func<ExtensionPanelHostContext> _contextProvider;
    private readonly ExtensionPanelStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    private readonly Queue<DateTimeOffset> _requestTimes = new();
    private readonly Queue<string> _recentRequestIds = new();
    private readonly HashSet<string> _recentRequestIdSet =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _subscriptions =
        new(StringComparer.Ordinal);
    private bool _isReady;
    private string _lastContextJson = string.Empty;

    public ExtensionPanelMessageHost(
        ExtensionPanelCandidate candidate,
        IReadOnlyCollection<ExtensionCapability> approvedCapabilities,
        string expectedVirtualHostName,
        string stateRootDirectory,
        Func<ExtensionPanelHostContext> contextProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(approvedCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVirtualHostName);
        ArgumentNullException.ThrowIfNull(contextProvider);

        var capabilitySet = approvedCapabilities.ToHashSet();
        if (!capabilitySet.Contains(ExtensionCapability.WorkspacePanel))
        {
            throw new InvalidOperationException(
                "Panel messaging requires an approved WorkspacePanel capability.");
        }

        _candidate = candidate;
        _approvedCapabilities = capabilitySet;
        _expectedVirtualHostName = expectedVirtualHostName;
        _contextProvider = contextProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stateStore = new ExtensionPanelStateStore(
            stateRootDirectory,
            candidate.Package.Manifest?.Id
            ?? throw new InvalidOperationException(
                "Panel messaging requires a readable extension identity."),
            candidate.PanelId);
    }

    public bool IsReady
    {
        get
        {
            lock (_sync)
            {
                return _isReady;
            }
        }
    }

    public void ResetSession()
    {
        lock (_sync)
        {
            _isReady = false;
            _subscriptions.Clear();
            _requestTimes.Clear();
            _recentRequestIds.Clear();
            _recentRequestIdSet.Clear();
            _lastContextJson = string.Empty;
        }
    }

    public async Task<ExtensionPanelMessageOutcome?> HandleAsync(
        string sourceUri,
        string rawMessage,
        CancellationToken cancellationToken = default)
    {
        rawMessage ??= string.Empty;
        if (!IsExpectedOrigin(sourceUri))
        {
            return null;
        }

        lock (_sync)
        {
            if (!AcceptRequestAtCurrentRate())
            {
                return Error(
                    null,
                    "RATE_LIMITED",
                    $"A panel may send at most {ExtensionPanelMessagingContract.MaximumRequestsPerMinute} requests per minute.");
            }
        }

        if (Encoding.UTF8.GetByteCount(rawMessage)
            > ExtensionPanelMessagingContract.MaximumInboundMessageBytes)
        {
            return Error(
                null,
                "MESSAGE_TOO_LARGE",
                $"Panel messages cannot exceed {ExtensionPanelMessagingContract.MaximumInboundMessageBytes / 1024:N0} KiB.");
        }

        PanelRequest request;
        try
        {
            request = ParseRequest(rawMessage);
        }
        catch (PanelProtocolException exception)
        {
            return Error(exception.RequestId, exception.Code, exception.Message);
        }

        lock (_sync)
        {
            if (!_recentRequestIdSet.Add(request.Id))
            {
                return Error(
                    request.Id,
                    "REQUEST_ID_REUSED",
                    "Every request in a panel session requires a unique ID.");
            }

            _recentRequestIds.Enqueue(request.Id);
            while (_recentRequestIds.Count > 256)
            {
                _recentRequestIdSet.Remove(_recentRequestIds.Dequeue());
            }

            if (!_isReady
                && !string.Equals(
                    request.Method,
                    "host.ready",
                    StringComparison.Ordinal))
            {
                return Error(
                    request.Id,
                    "HANDSHAKE_REQUIRED",
                    "Call host.ready before using the panel message API.");
            }
        }

        try
        {
            return request.Method switch
            {
                "host.ready" => HandleReady(request),
                "host.getContext" => HandleGetContext(request),
                "state.get" => await HandleStateGetAsync(
                    request,
                    cancellationToken),
                "state.set" => await HandleStateSetAsync(
                    request,
                    cancellationToken),
                "events.subscribe" => HandleSubscribe(request),
                _ => Error(
                    request.Id,
                    "METHOD_NOT_FOUND",
                    "The requested panel host method is not available.")
            };
        }
        catch (PanelProtocolException exception)
        {
            return Error(request.Id, exception.Code, exception.Message);
        }
        catch (ExtensionPanelStateException exception)
        {
            return Error(request.Id, exception.Code, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error(
                request.Id,
                "HOST_ERROR",
                "The panel host could not complete this request.");
        }
    }

    public string? TryCreateContextChangedEvent()
    {
        lock (_sync)
        {
            if (!_isReady
                || !_approvedCapabilities.Contains(
                    ExtensionCapability.WorkspaceMetadata)
                || !_subscriptions.Contains(
                    ExtensionPanelMessagingContract.ContextChangedEvent))
            {
                return null;
            }
        }

        var context = _contextProvider();
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        lock (_sync)
        {
            if (string.Equals(
                contextJson,
                _lastContextJson,
                StringComparison.Ordinal))
            {
                return null;
            }

            _lastContextJson = contextJson;
        }

        return JsonSerializer.Serialize(
            new
            {
                protocol = ExtensionPanelMessagingContract.Protocol,
                version = ExtensionPanelMessagingContract.CurrentVersion,
                type = "event",
                @event = ExtensionPanelMessagingContract.ContextChangedEvent,
                data = context
            },
            JsonOptions);
    }

    private ExtensionPanelMessageOutcome HandleReady(PanelRequest request)
    {
        EnsureNoParameters(request);
        lock (_sync)
        {
            if (_isReady)
            {
                throw new PanelProtocolException(
                    "HANDSHAKE_COMPLETE",
                    "host.ready can be called only once per navigation.",
                    request.Id);
            }

            _isReady = true;
        }

        var methods = new List<string>
        {
            "host.ready",
            "state.get",
            "state.set"
        };
        var events = Array.Empty<string>();
        if (_approvedCapabilities.Contains(ExtensionCapability.WorkspaceMetadata))
        {
            methods.Add("host.getContext");
            methods.Add("events.subscribe");
            events = [ExtensionPanelMessagingContract.ContextChangedEvent];
        }

        var manifest = _candidate.Package.Manifest!;
        return Success(
            request.Id,
            new
            {
                hostVersion = typeof(ExtensionPanelMessageHost).Assembly
                                  .GetName().Version?.ToString(3)
                              ?? "0.0.0",
                extension = new
                {
                    id = manifest.Id,
                    version = manifest.Version,
                    panelId = _candidate.PanelId
                },
                approvedCapabilities = _approvedCapabilities
                    .OrderBy(capability => capability.ToString(), StringComparer.Ordinal)
                    .Select(capability => capability.ToString())
                    .ToArray(),
                methods,
                events,
                statePersistence = "deviceLocal",
                limits = new
                {
                    messageBytes = ExtensionPanelMessagingContract.MaximumInboundMessageBytes,
                    stateBytes = ExtensionPanelMessagingContract.MaximumStateBytes,
                    requestsPerMinute = ExtensionPanelMessagingContract.MaximumRequestsPerMinute
                }
            });
    }

    private ExtensionPanelMessageOutcome HandleGetContext(PanelRequest request)
    {
        EnsureNoParameters(request);
        RequireCapability(ExtensionCapability.WorkspaceMetadata, request.Id);
        var context = _contextProvider();
        lock (_sync)
        {
            _lastContextJson = JsonSerializer.Serialize(context, JsonOptions);
        }

        return Success(request.Id, context);
    }

    private async Task<ExtensionPanelMessageOutcome> HandleStateGetAsync(
        PanelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureNoParameters(request);
        var value = await _stateStore.ReadAsync(cancellationToken);
        return Success(request.Id, new { value });
    }

    private async Task<ExtensionPanelMessageOutcome> HandleStateSetAsync(
        PanelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureOnlyParameters(request, "value");
        if (!request.Parameters.TryGetProperty("value", out var value))
        {
            throw new PanelProtocolException(
                "PARAMS_INVALID",
                "state.set requires a value property.",
                request.Id);
        }

        var bytes = await _stateStore.WriteAsync(value, cancellationToken);
        return Success(
            request.Id,
            new
            {
                saved = true,
                bytes,
                persistence = "deviceLocal"
            });
    }

    private ExtensionPanelMessageOutcome HandleSubscribe(PanelRequest request)
    {
        RequireCapability(ExtensionCapability.WorkspaceMetadata, request.Id);
        EnsureOnlyParameters(request, "events");
        if (!request.Parameters.TryGetProperty("events", out var eventsElement)
            || eventsElement.ValueKind != JsonValueKind.Array)
        {
            throw new PanelProtocolException(
                "PARAMS_INVALID",
                "events.subscribe requires an events array.",
                request.Id);
        }

        var requestedEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventElement in eventsElement.EnumerateArray())
        {
            if (eventElement.ValueKind != JsonValueKind.String
                || eventElement.GetString() is not { } eventName
                || !requestedEvents.Add(eventName)
                || !string.Equals(
                    eventName,
                    ExtensionPanelMessagingContract.ContextChangedEvent,
                    StringComparison.Ordinal))
            {
                throw new PanelProtocolException(
                    "EVENT_UNSUPPORTED",
                    "The subscription contains an unknown or duplicate event.",
                    request.Id);
            }
        }

        lock (_sync)
        {
            _subscriptions.Clear();
            _subscriptions.UnionWith(requestedEvents);
        }

        return Success(
            request.Id,
            new { events = requestedEvents.Order(StringComparer.Ordinal).ToArray() });
    }

    private bool IsExpectedOrigin(string sourceUri) =>
        Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri)
        && string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            uri.Host,
            _expectedVirtualHostName,
            StringComparison.OrdinalIgnoreCase)
        && (uri.IsDefaultPort || uri.Port == 443);

    private bool AcceptRequestAtCurrentRate()
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - TimeSpan.FromMinutes(1);
        while (_requestTimes.TryPeek(out var timestamp) && timestamp <= cutoff)
        {
            _requestTimes.Dequeue();
        }

        if (_requestTimes.Count
            >= ExtensionPanelMessagingContract.MaximumRequestsPerMinute)
        {
            return false;
        }

        _requestTimes.Enqueue(now);
        return true;
    }

    private void RequireCapability(
        ExtensionCapability capability,
        string requestId)
    {
        if (!_approvedCapabilities.Contains(capability))
        {
            throw new PanelProtocolException(
                "CAPABILITY_DENIED",
                $"This package is not approved for {capability}.",
                requestId);
        }
    }

    private static void EnsureNoParameters(PanelRequest request) =>
        EnsureOnlyParameters(request);

    private static void EnsureOnlyParameters(
        PanelRequest request,
        params string[] allowedNames)
    {
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in request.Parameters.EnumerateObject())
        {
            if (!names.Add(property.Name) || !allowed.Contains(property.Name))
            {
                throw new PanelProtocolException(
                    "PARAMS_INVALID",
                    "The request contains duplicate or unsupported parameters.",
                    request.Id);
            }
        }
    }

    private static PanelRequest ParseRequest(string rawMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(
                rawMessage,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PanelProtocolException(
                    "REQUEST_INVALID",
                    "The panel message must be a JSON object.");
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name)
                    || !RequestPropertyNames.Contains(property.Name))
                {
                    throw new PanelProtocolException(
                        "REQUEST_INVALID",
                        "The request contains duplicate or unsupported fields.");
                }
            }

            string? requestId = null;
            if (root.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.String)
            {
                requestId = idElement.GetString();
            }

            if (!root.TryGetProperty("protocol", out var protocolElement)
                || protocolElement.ValueKind != JsonValueKind.String
                || !string.Equals(
                    protocolElement.GetString(),
                    ExtensionPanelMessagingContract.Protocol,
                    StringComparison.Ordinal))
            {
                throw new PanelProtocolException(
                    "PROTOCOL_INVALID",
                    "The request does not use the Hypershell panel protocol.",
                    requestId);
            }

            if (!root.TryGetProperty("version", out var versionElement)
                || !versionElement.TryGetInt32(out var version)
                || version != ExtensionPanelMessagingContract.CurrentVersion)
            {
                throw new PanelProtocolException(
                    "VERSION_UNSUPPORTED",
                    $"This host accepts panel message API version {ExtensionPanelMessagingContract.CurrentVersion}.",
                    requestId);
            }

            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !string.Equals(
                    typeElement.GetString(),
                    "request",
                    StringComparison.Ordinal))
            {
                throw new PanelProtocolException(
                    "REQUEST_INVALID",
                    "The panel message type must be request.",
                    requestId);
            }

            if (requestId is null || !RequestIdPattern.IsMatch(requestId))
            {
                throw new PanelProtocolException(
                    "REQUEST_ID_INVALID",
                    "Request IDs require 1–64 safe ASCII characters.");
            }

            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || methodElement.GetString() is not { } method
                || !MethodPattern.IsMatch(method))
            {
                throw new PanelProtocolException(
                    "METHOD_INVALID",
                    "The panel method name is invalid.",
                    requestId);
            }

            JsonElement parameters;
            if (root.TryGetProperty("params", out var paramsElement))
            {
                if (paramsElement.ValueKind != JsonValueKind.Object)
                {
                    throw new PanelProtocolException(
                        "PARAMS_INVALID",
                        "Request params must be a JSON object.",
                        requestId);
                }

                parameters = paramsElement.Clone();
            }
            else
            {
                using var emptyDocument = JsonDocument.Parse("{}");
                parameters = emptyDocument.RootElement.Clone();
            }

            return new PanelRequest(requestId, method, parameters);
        }
        catch (PanelProtocolException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new PanelProtocolException(
                "JSON_INVALID",
                "The panel message is not valid bounded JSON.");
        }
    }

    private static ExtensionPanelMessageOutcome Success(
        string requestId,
        object result) =>
        new(
            JsonSerializer.Serialize(
                new
                {
                    protocol = ExtensionPanelMessagingContract.Protocol,
                    version = ExtensionPanelMessagingContract.CurrentVersion,
                    type = "response",
                    id = requestId,
                    ok = true,
                    result
                },
                JsonOptions),
            true,
            string.Empty);

    private static ExtensionPanelMessageOutcome Error(
        string? requestId,
        string code,
        string message) =>
        new(
            JsonSerializer.Serialize(
                new
                {
                    protocol = ExtensionPanelMessagingContract.Protocol,
                    version = ExtensionPanelMessagingContract.CurrentVersion,
                    type = "response",
                    id = requestId,
                    ok = false,
                    error = new { code, message }
                },
                JsonOptions),
            false,
            $"{code}: {message}");

    private sealed record PanelRequest(
        string Id,
        string Method,
        JsonElement Parameters);

    private sealed class PanelProtocolException : Exception
    {
        public PanelProtocolException(
            string code,
            string message,
            string? requestId = null)
            : base(message)
        {
            Code = code;
            RequestId = requestId;
        }

        public string Code { get; }
        public string? RequestId { get; }
    }
}
