using Hypershell.Profiles;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public enum WorkspaceAiContextAccessMode
{
    ChooseLocation,
    FullAccess
}

public enum WorkspaceAiComputerControlMode
{
    Off,
    Hypershell,
    Desktop
}

public sealed record WorkspaceAiContextPathPermission
{
    public WorkspaceAiContextAccessMode Mode { get; init; } =
        WorkspaceAiContextAccessMode.ChooseLocation;
    public string Location { get; init; } = string.Empty;
    public IReadOnlyList<string> WorkspaceUris { get; init; } = [];
}

public sealed record WorkspaceAiContextDefinition
{
    public const string DefaultId = "default";
    public const string CustomId = "custom";

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsBuiltIn { get; init; }
    public WorkspaceAiContextPathPermission Read { get; init; } = new();
    public WorkspaceAiContextPathPermission Create { get; init; } = new();
    public WorkspaceAiContextPathPermission Edit { get; init; } = new();
    public WorkspaceAiContextPathPermission Delete { get; init; } = new();
    public WorkspaceAiContextPathPermission Terminal { get; init; } = new();
    public string Model { get; init; } =
        WorkspaceAiProviderConfiguration.DefaultModel;
    public string ReasoningEffort { get; init; } = "medium";
    public string TextVerbosity { get; init; } = "medium";
    public string DefaultSaveDirectory { get; init; } = string.Empty;
    public string DefaultSaveWorkspaceUri { get; init; } = string.Empty;
    public bool AskBeforeChanges { get; init; } = true;
    public bool AllowInternet { get; init; }
    public bool AllowLiveAssistance { get; init; }
    public WorkspaceAiComputerControlMode ComputerControl { get; init; } =
        WorkspaceAiComputerControlMode.Off;
    public bool RiskWarningAcknowledged { get; init; }

    public bool RequiresRiskWarning =>
        !AskBeforeChanges
        || AllowInternet
        || ComputerControl != WorkspaceAiComputerControlMode.Off
        || Read.Mode == WorkspaceAiContextAccessMode.FullAccess
        || Create.Mode == WorkspaceAiContextAccessMode.FullAccess
        || Edit.Mode == WorkspaceAiContextAccessMode.FullAccess
        || Delete.Mode == WorkspaceAiContextAccessMode.FullAccess
        || Terminal.Mode == WorkspaceAiContextAccessMode.FullAccess;
}

public sealed record WorkspaceAiContextRoamingImportResult(
    string SelectedContextId,
    IReadOnlyList<string> RequiresDeviceConfirmationIds);

public sealed class WorkspaceAiContextState
{
    public int SchemaVersion { get; set; } =
        WorkspaceAiContextService.CurrentSchemaVersion;
    public string SelectedContextId { get; set; } =
        WorkspaceAiContextDefinition.DefaultId;
    public List<WorkspaceAiContextDefinition> Contexts { get; set; } = [];
}

public sealed class WorkspaceAiContextService
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumContexts = 128;

    private static readonly HashSet<string> ReasoningEfforts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "none", "low", "medium", "high", "xhigh", "max"
        };
    private static readonly HashSet<string> TextVerbosities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "low", "medium", "high"
        };

    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private WorkspaceAiContextState _state;

    public WorkspaceAiContextService(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        _state = LoadState();
    }

    public string StatePath => _statePath;
    public string LastLoadWarning { get; private set; } = string.Empty;

    public string SelectedContextId
    {
        get
        {
            lock (_gate)
            {
                return _state.SelectedContextId;
            }
        }
    }

    public IReadOnlyList<WorkspaceAiContextDefinition> GetContexts()
    {
        lock (_gate)
        {
            return _state.Contexts
                .OrderBy(ContextOrder)
                .ThenBy(context => context.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
    }

    public WorkspaceAiContextDefinition? GetContext(string? contextId)
    {
        if (string.IsNullOrWhiteSpace(contextId))
        {
            return null;
        }

        lock (_gate)
        {
            var context = _state.Contexts.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    contextId,
                    StringComparison.OrdinalIgnoreCase));
            return context is null ? null : Clone(context);
        }
    }

    public IReadOnlyList<RoamingAiContextDefinition>
        ExportRoamingDefinitions()
    {
        lock (_gate)
        {
            return _state.Contexts.Select(context =>
                    new RoamingAiContextDefinition
                    {
                        Id = context.Id,
                        Name = context.Name,
                        IsBuiltIn = context.IsBuiltIn,
                        Read = ToRoamingPermission(context.Read),
                        Create = ToRoamingPermission(context.Create),
                        Edit = ToRoamingPermission(context.Edit),
                        Delete = ToRoamingPermission(context.Delete),
                        Terminal = ToRoamingPermission(context.Terminal),
                        Model = context.Model,
                        ReasoningEffort = context.ReasoningEffort,
                        TextVerbosity = context.TextVerbosity,
                        DefaultSaveWorkspaceUri =
                            context.DefaultSaveWorkspaceUri,
                        AskBeforeChanges = context.AskBeforeChanges,
                        AllowInternet = context.AllowInternet,
                        AllowLiveAssistance = context.AllowLiveAssistance,
                        ComputerControl = context.ComputerControl switch
                        {
                            WorkspaceAiComputerControlMode.Hypershell =>
                                RoamingAiComputerControlMode.Hypershell,
                            WorkspaceAiComputerControlMode.Desktop =>
                                RoamingAiComputerControlMode.Desktop,
                            _ => RoamingAiComputerControlMode.Off
                        }
                    })
                .ToList();
        }
    }

    public WorkspaceAiContextRoamingImportResult ImportRoamingDefinitions(
        IReadOnlyList<RoamingAiContextDefinition> definitions,
        string selectedContextId)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count > MaximumContexts)
        {
            throw new InvalidDataException(
                $"The roaming profile contains more than {MaximumContexts} AI contexts.");
        }

        lock (_gate)
        {
            var defaultContext = _state.Contexts.FirstOrDefault(context =>
                                     context.Id
                                     == WorkspaceAiContextDefinition.DefaultId)
                                 ?? CreateDefaultContext();
            var imported = new List<WorkspaceAiContextDefinition>();
            var requiresConfirmation = new List<string>();
            foreach (var remote in definitions)
            {
                var existing = _state.Contexts.FirstOrDefault(context =>
                    string.Equals(
                        context.Id,
                        remote.Id,
                        StringComparison.OrdinalIgnoreCase));
                var fallback = existing ?? defaultContext;
                var candidate = Normalize(new WorkspaceAiContextDefinition
                {
                    Id = remote.Id,
                    Name = remote.Name,
                    IsBuiltIn = remote.IsBuiltIn,
                    Read = FromRoamingPermission(
                        remote.Read,
                        fallback.Read),
                    Create = FromRoamingPermission(
                        remote.Create,
                        fallback.Create),
                    Edit = FromRoamingPermission(
                        remote.Edit,
                        fallback.Edit),
                    Delete = FromRoamingPermission(
                        remote.Delete,
                        fallback.Delete),
                    Terminal = FromRoamingPermission(
                        remote.Terminal,
                        fallback.Terminal),
                    Model = remote.Model,
                    ReasoningEffort = remote.ReasoningEffort,
                    TextVerbosity = remote.TextVerbosity,
                    DefaultSaveDirectory =
                        Directory.Exists(fallback.DefaultSaveDirectory)
                            ? fallback.DefaultSaveDirectory
                            : defaultContext.DefaultSaveDirectory,
                    DefaultSaveWorkspaceUri =
                        NormalizeOptionalWorkspaceUri(
                            remote.DefaultSaveWorkspaceUri),
                    AskBeforeChanges = remote.AskBeforeChanges,
                    AllowInternet = remote.AllowInternet,
                    AllowLiveAssistance = remote.AllowLiveAssistance,
                    ComputerControl = remote.ComputerControl switch
                    {
                        RoamingAiComputerControlMode.Hypershell =>
                            WorkspaceAiComputerControlMode.Hypershell,
                        RoamingAiComputerControlMode.Desktop =>
                            WorkspaceAiComputerControlMode.Desktop,
                        _ => WorkspaceAiComputerControlMode.Off
                    }
                });
                var preserveApproval = existing is not null
                                       && existing.RiskWarningAcknowledged
                                       && PoliciesEquivalent(
                                           existing,
                                           candidate);
                candidate = candidate with
                {
                    RiskWarningAcknowledged = preserveApproval
                };
                if (candidate.RequiresRiskWarning
                    && !candidate.RiskWarningAcknowledged)
                {
                    requiresConfirmation.Add(candidate.Id);
                }

                imported.Add(candidate);
            }

            EnsureBuiltIns(imported);
            var requested = imported.FirstOrDefault(context => string.Equals(
                context.Id,
                selectedContextId,
                StringComparison.OrdinalIgnoreCase));
            var selected = requested is not null
                           && (!requested.RequiresRiskWarning
                               || requested.RiskWarningAcknowledged)
                ? requested.Id
                : WorkspaceAiContextDefinition.DefaultId;
            var next = new WorkspaceAiContextState
            {
                SelectedContextId = selected,
                Contexts = imported
            };
            SaveState(next);
            _state = next;
            return new WorkspaceAiContextRoamingImportResult(
                selected,
                requiresConfirmation);
        }
    }

    public WorkspaceAiContextDefinition CreateDraft(
        WorkspaceAiContextDefinition? source = null)
    {
        source ??= GetContext(WorkspaceAiContextDefinition.DefaultId)
                   ?? CreateDefaultContext();
        return Clone(source) with
        {
            Id = $"context:{Guid.NewGuid():N}",
            Name = "New Context",
            IsBuiltIn = false,
            RiskWarningAcknowledged = false
        };
    }

    public WorkspaceAiContextDefinition SaveContext(
        WorkspaceAiContextDefinition context,
        bool select = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        var normalized = Normalize(context);
        lock (_gate)
        {
            var next = CloneState(_state);
            var index = next.Contexts.FindIndex(candidate => string.Equals(
                candidate.Id,
                normalized.Id,
                StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                next.Contexts[index] = normalized;
            }
            else
            {
                if (next.Contexts.Count >= MaximumContexts)
                {
                    throw new InvalidOperationException(
                        $"At most {MaximumContexts} AI contexts can be stored on this device.");
                }

                next.Contexts.Add(normalized);
            }

            if (select)
            {
                next.SelectedContextId = normalized.Id;
            }

            SaveState(next);
            _state = next;
        }

        return Clone(normalized);
    }

    public WorkspaceAiContextDefinition UpdateCustomBehavior(
        WorkspaceAiContextDefinition source,
        string model,
        string reasoningEffort,
        string textVerbosity)
    {
        ArgumentNullException.ThrowIfNull(source);
        var custom = source with
        {
            Id = WorkspaceAiContextDefinition.CustomId,
            Name = "Custom",
            IsBuiltIn = true,
            Model = model,
            ReasoningEffort = reasoningEffort,
            TextVerbosity = textVerbosity
        };
        return SaveContext(custom);
    }

    public void Select(string contextId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        lock (_gate)
        {
            var selected = _state.Contexts.FirstOrDefault(context =>
                string.Equals(
                    context.Id,
                    contextId,
                    StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new InvalidOperationException(
                    "The selected AI context is no longer available.");
            }

            if (selected.RequiresRiskWarning
                && !selected.RiskWarningAcknowledged)
            {
                throw new InvalidOperationException(
                    "Review and save this Context once on this PC before activating its unrestricted permissions.");
            }

            if (string.Equals(
                    _state.SelectedContextId,
                    contextId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var next = CloneState(_state);
            next.SelectedContextId = contextId;
            SaveState(next);
            _state = next;
        }
    }

    private WorkspaceAiContextState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                var initial = CreateInitialState();
                SaveState(initial);
                return initial;
            }

            var info = new FileInfo(_statePath);
            if (info.Length is <= 0 or > 1024 * 1024)
            {
                throw new InvalidDataException(
                    "The AI context file has an invalid size.");
            }

            var state = JsonSerializer.Deserialize<WorkspaceAiContextState>(
                            File.ReadAllText(_statePath),
                            _jsonOptions)
                        ?? throw new JsonException(
                            "The AI context file was empty.");
            if (state.SchemaVersion != CurrentSchemaVersion)
            {
                throw new JsonException(
                    $"AI context schema {state.SchemaVersion} is unsupported.");
            }

            state.Contexts ??= [];
            var contexts = state.Contexts
                .Select(Normalize)
                .GroupBy(context => context.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Take(MaximumContexts)
                .ToList();
            EnsureBuiltIns(contexts);
            state.Contexts = contexts;
            if (!contexts.Any(context => string.Equals(
                    context.Id,
                    state.SelectedContextId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                state.SelectedContextId = WorkspaceAiContextDefinition.DefaultId;
            }

            return state;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or InvalidDataException
                                         or JsonException
                                         or ArgumentException
                                         or NotSupportedException
                                         or InvalidOperationException)
        {
            LastLoadWarning =
                $"AI contexts could not be loaded; safe defaults are active: {exception.Message}";
            return CreateInitialState();
        }
    }

    private void SaveState(WorkspaceAiContextState state)
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException(
                            "The AI context path has no parent directory.");
        Directory.CreateDirectory(directory);
        state.SchemaVersion = CurrentSchemaVersion;
        var temporaryPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                state,
                _jsonOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static WorkspaceAiContextState CreateInitialState()
    {
        var defaultContext = CreateDefaultContext();
        var custom = defaultContext with
        {
            Id = WorkspaceAiContextDefinition.CustomId,
            Name = "Custom",
            IsBuiltIn = true
        };
        return new WorkspaceAiContextState
        {
            SelectedContextId = WorkspaceAiContextDefinition.DefaultId,
            Contexts = [defaultContext, custom]
        };
    }

    private static WorkspaceAiContextDefinition CreateDefaultContext()
    {
        var documents = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents)
            || !Directory.Exists(documents))
        {
            documents = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
        }

        documents = Path.GetFullPath(documents);
        var permission = new WorkspaceAiContextPathPermission
        {
            Mode = WorkspaceAiContextAccessMode.ChooseLocation,
            Location = documents
        };
        return new WorkspaceAiContextDefinition
        {
            Id = WorkspaceAiContextDefinition.DefaultId,
            Name = "Default",
            IsBuiltIn = true,
            Read = permission,
            Create = permission,
            Edit = permission,
            Delete = permission,
            Terminal = permission,
            DefaultSaveDirectory = string.Empty,
            AskBeforeChanges = true,
            AllowInternet = false
        };
    }

    private static void EnsureBuiltIns(
        List<WorkspaceAiContextDefinition> contexts)
    {
        var fallback = CreateDefaultContext();
        if (!contexts.Any(context => string.Equals(
                context.Id,
                WorkspaceAiContextDefinition.DefaultId,
                StringComparison.OrdinalIgnoreCase)))
        {
            contexts.Insert(0, fallback);
        }

        if (!contexts.Any(context => string.Equals(
                context.Id,
                WorkspaceAiContextDefinition.CustomId,
                StringComparison.OrdinalIgnoreCase)))
        {
            contexts.Insert(1, fallback with
            {
                Id = WorkspaceAiContextDefinition.CustomId,
                Name = "Custom"
            });
        }
    }

    private static WorkspaceAiContextDefinition Normalize(
        WorkspaceAiContextDefinition context)
    {
        var id = (context.Id ?? string.Empty).Trim().ToLowerInvariant();
        if (id.Length is < 1 or > 160
            || id.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not ':' and not '-'))
        {
            throw new ArgumentException("The AI context ID is malformed.");
        }

        var name = (context.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 96 || name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Context names must contain 1-96 printable characters.");
        }

        var model = (context.Model ?? string.Empty).Trim();
        if (model.Length is < 1 or > 100
            || model.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not ':' and not '-'))
        {
            throw new ArgumentException("The OpenAI model identifier is malformed.");
        }

        var effort = (context.ReasoningEffort ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var verbosity = (context.TextVerbosity ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!ReasoningEfforts.Contains(effort)
            || !TextVerbosities.Contains(verbosity))
        {
            throw new ArgumentException(
                "The context reasoning effort or verbosity is unsupported.");
        }

        if (!Enum.IsDefined(context.ComputerControl))
        {
            throw new ArgumentException(
                "The context computer-control mode is unsupported.");
        }

        var isBuiltIn = id is WorkspaceAiContextDefinition.DefaultId
            or WorkspaceAiContextDefinition.CustomId;
        return context with
        {
            Id = id,
            Name = id == WorkspaceAiContextDefinition.DefaultId
                ? "Default"
                : id == WorkspaceAiContextDefinition.CustomId
                    ? "Custom"
                    : name,
            IsBuiltIn = isBuiltIn,
            Read = NormalizePermission(context.Read, "Read"),
            Create = NormalizePermission(context.Create, "Create"),
            Edit = NormalizePermission(context.Edit, "Edit"),
            Delete = NormalizePermission(context.Delete, "Delete"),
            Terminal = NormalizePermission(context.Terminal, "Terminal"),
            Model = model,
            ReasoningEffort = effort,
            TextVerbosity = verbosity,
            // Project roots belong to individual AI conversations. Clear the
            // retired Context-level save target during normalization so old
            // device state cannot silently redirect future work.
            DefaultSaveDirectory = string.Empty,
            DefaultSaveWorkspaceUri = string.Empty,
            ComputerControl = context.ComputerControl
        };
    }

    private static WorkspaceAiContextPathPermission NormalizePermission(
        WorkspaceAiContextPathPermission? permission,
        string label)
    {
        permission ??= new WorkspaceAiContextPathPermission();
        var workspaceUris = permission.WorkspaceUris ?? [];
        if (!Enum.IsDefined(permission.Mode))
        {
            throw new ArgumentException($"The {label} access mode is unsupported.");
        }

        return permission.Mode == WorkspaceAiContextAccessMode.FullAccess
            ? permission with
            {
                Location = string.Empty,
                WorkspaceUris = []
            }
            : permission with
            {
                Location = string.IsNullOrWhiteSpace(permission.Location)
                    && workspaceUris.Count > 0
                    ? string.Empty
                    : NormalizeDirectory(permission.Location, label),
                WorkspaceUris = NormalizeWorkspaceUris(
                    workspaceUris)
            };
    }

    private static RoamingAiPathPermission ToRoamingPermission(
        WorkspaceAiContextPathPermission permission) => new()
    {
        AllowFullAccess =
            permission.Mode == WorkspaceAiContextAccessMode.FullAccess,
        WorkspaceUris = permission.Mode
                        == WorkspaceAiContextAccessMode.FullAccess
            ? []
            : permission.WorkspaceUris.ToList()
    };

    private static WorkspaceAiContextPathPermission FromRoamingPermission(
        RoamingAiPathPermission permission,
        WorkspaceAiContextPathPermission localFallback)
    {
        ArgumentNullException.ThrowIfNull(permission);
        if (permission.AllowFullAccess
            && permission.WorkspaceUris.Count > 0)
        {
            throw new InvalidDataException(
                "A full-access roaming permission also contained workspace URI scopes.");
        }

        return permission.AllowFullAccess
            ? new WorkspaceAiContextPathPermission
            {
                Mode = WorkspaceAiContextAccessMode.FullAccess
            }
            : new WorkspaceAiContextPathPermission
            {
                Mode = WorkspaceAiContextAccessMode.ChooseLocation,
                Location = localFallback.Mode
                           == WorkspaceAiContextAccessMode.ChooseLocation
                           && Directory.Exists(localFallback.Location)
                    ? localFallback.Location
                    : string.Empty,
                WorkspaceUris = NormalizeWorkspaceUris(
                    permission.WorkspaceUris)
            };
    }

    private static IReadOnlyList<string> NormalizeWorkspaceUris(
        IReadOnlyList<string>? values)
    {
        values ??= [];
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var uri = NormalizeOptionalWorkspaceUri(value);
            if (string.IsNullOrWhiteSpace(uri) || !seen.Add(uri))
            {
                throw new InvalidDataException(
                    "A roaming AI permission contains an empty or duplicate workspace URI.");
            }

            normalized.Add(uri);
        }

        return normalized;
    }

    private static string NormalizeOptionalWorkspaceUri(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Length > 2048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("ssh" or "workspace")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException(
                "A roaming AI workspace URI is malformed or device-local.");
        }

        return uri.AbsoluteUri;
    }

    private static bool PoliciesEquivalent(
        WorkspaceAiContextDefinition left,
        WorkspaceAiContextDefinition right) =>
        left.AskBeforeChanges == right.AskBeforeChanges
        && left.AllowInternet == right.AllowInternet
        && left.AllowLiveAssistance == right.AllowLiveAssistance
        && left.ComputerControl == right.ComputerControl
        && PermissionPoliciesEquivalent(left.Read, right.Read)
        && PermissionPoliciesEquivalent(left.Create, right.Create)
        && PermissionPoliciesEquivalent(left.Edit, right.Edit)
        && PermissionPoliciesEquivalent(left.Delete, right.Delete)
        && PermissionPoliciesEquivalent(left.Terminal, right.Terminal);

    private static bool PermissionPoliciesEquivalent(
        WorkspaceAiContextPathPermission left,
        WorkspaceAiContextPathPermission right) =>
        left.Mode == right.Mode
        && left.WorkspaceUris.SequenceEqual(
            right.WorkspaceUris,
            StringComparer.Ordinal);

    private static string NormalizeDirectory(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Choose a location for {label} access.");
        }

        var path = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(value.Trim()));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"The {label} location does not exist: {path}");
        }

        return path;
    }

    private static int ContextOrder(WorkspaceAiContextDefinition context) =>
        context.Id switch
        {
            WorkspaceAiContextDefinition.DefaultId => 0,
            WorkspaceAiContextDefinition.CustomId => 1,
            _ => 2
        };

    private static WorkspaceAiContextDefinition Clone(
        WorkspaceAiContextDefinition context) =>
        context with
        {
            Read = ClonePermission(context.Read),
            Create = ClonePermission(context.Create),
            Edit = ClonePermission(context.Edit),
            Delete = ClonePermission(context.Delete),
            Terminal = ClonePermission(context.Terminal)
        };

    private static WorkspaceAiContextPathPermission ClonePermission(
        WorkspaceAiContextPathPermission permission) => permission with
    {
        WorkspaceUris = permission.WorkspaceUris.ToList()
    };

    private static WorkspaceAiContextState CloneState(
        WorkspaceAiContextState state) =>
        new()
        {
            SchemaVersion = state.SchemaVersion,
            SelectedContextId = state.SelectedContextId,
            Contexts = state.Contexts.Select(Clone).ToList()
        };
}
