using Hypershell.Models;
using Hypershell.Profiles;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

/// <summary>
/// Keeps sync checkpoints and client-local workspace mappings outside every
/// roaming document. No absolute local path is sent to the account service.
/// </summary>
public sealed class ProfileRoamingDeviceStateService
{
    private const long MaximumStateBytes = 2 * 1024 * 1024;
    private const int MaximumConflictBytes = 2 * 1024 * 1024;
    private const int MaximumRecords = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();
    private ProfileRoamingDeviceState _state;

    public ProfileRoamingDeviceStateService(
        string statePath,
        string conflictDirectory)
    {
        StatePath = Path.GetFullPath(statePath);
        ConflictDirectory = Path.GetFullPath(conflictDirectory);
        _state = Load();
    }

    public string StatePath { get; }

    public string ConflictDirectory { get; }

    public string LastLoadWarning { get; private set; } = string.Empty;

    public RoamingProfileCheckpoint? FindProfileCheckpoint(
        string endpoint,
        string profileId)
    {
        var key = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        lock (_gate)
        {
            return _state.ProfileCheckpoints.FirstOrDefault(checkpoint =>
                checkpoint.EndpointKey == key
                && string.Equals(
                    checkpoint.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SaveProfileCheckpoint(RoamingProfileCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateProfileCheckpoint(checkpoint);
        lock (_gate)
        {
            _state = CopyState(
                profileCheckpoints: _state.ProfileCheckpoints
                    .Where(existing =>
                        existing.EndpointKey != checkpoint.EndpointKey
                        || !string.Equals(
                            existing.ProfileId,
                            checkpoint.ProfileId,
                            StringComparison.OrdinalIgnoreCase))
                    .Append(checkpoint)
                    .OrderByDescending(existing =>
                        existing.LastPushedAtUtc
                        ?? existing.LastPulledAtUtc
                        ?? DateTimeOffset.MinValue)
                    .Take(MaximumRecords)
                    .ToList());
            Save();
        }
    }

    public RoamingConversationCheckpoint? FindConversationCheckpoint(
        string endpoint,
        string conversationId)
    {
        var key = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        lock (_gate)
        {
            return _state.ConversationCheckpoints.FirstOrDefault(checkpoint =>
                checkpoint.EndpointKey == key
                && string.Equals(
                    checkpoint.ConversationId,
                    conversationId,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SaveConversationCheckpoint(
        RoamingConversationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateConversationCheckpoint(checkpoint);
        lock (_gate)
        {
            _state = CopyState(
                conversationCheckpoints: _state.ConversationCheckpoints
                    .Where(existing =>
                        existing.EndpointKey != checkpoint.EndpointKey
                        || !string.Equals(
                            existing.ConversationId,
                            checkpoint.ConversationId,
                            StringComparison.OrdinalIgnoreCase))
                    .Append(checkpoint)
                    .OrderByDescending(existing =>
                        existing.LastPushedAtUtc
                        ?? existing.LastPulledAtUtc
                        ?? DateTimeOffset.MinValue)
                    .Take(MaximumRecords)
                    .ToList());
            Save();
        }
    }

    public void CaptureProjectMappings(
        string profileId,
        IEnumerable<ProjectDefinition> projects)
    {
        ValidateId(profileId, "profile");
        ArgumentNullException.ThrowIfNull(projects);
        IReadOnlyList<WorkspaceProjectDeviceMapping> existingMappings;
        lock (_gate)
        {
            existingMappings = _state.ProjectMappings
                .Where(mapping => string.Equals(
                    mapping.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var captured = projects.Select(project =>
            {
                ValidateId(project.Id, "project");
                var existing = existingMappings.FirstOrDefault(mapping =>
                    string.Equals(
                        mapping.ProjectId,
                        project.Id,
                        StringComparison.OrdinalIgnoreCase));
                return new WorkspaceProjectDeviceMapping
                {
                    ProfileId = profileId,
                    ProjectId = project.Id,
                    LocalDirectories = project.Directories
                        .Where(directory =>
                            !string.IsNullOrWhiteSpace(directory)
                            && !IsRoamingWorkspaceUri(directory))
                        .Select(TryNormalizeLocalPath)
                        .Where(directory => directory is not null)
                        .Select(directory => directory!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    RemoteWorkspaceUri = project.Directories
                        .FirstOrDefault(IsRoamingWorkspaceUri)
                        ?? existing?.RemoteWorkspaceUri
                        ?? string.Empty
                };
            })
            .ToList();
        lock (_gate)
        {
            _state = CopyState(
                projectMappings: _state.ProjectMappings
                    .Where(mapping => !string.Equals(
                        mapping.ProfileId,
                        profileId,
                        StringComparison.OrdinalIgnoreCase))
                    .Concat(captured)
                    .Take(MaximumRecords)
                    .ToList());
            Save();
        }
    }

    public IReadOnlyList<string> GetProjectDirectories(
        string profileId,
        string projectId)
    {
        lock (_gate)
        {
            return _state.ProjectMappings.FirstOrDefault(mapping =>
                       string.Equals(
                           mapping.ProfileId,
                           profileId,
                           StringComparison.OrdinalIgnoreCase)
                       && string.Equals(
                           mapping.ProjectId,
                           projectId,
                           StringComparison.OrdinalIgnoreCase))
                   ?.LocalDirectories
                   .Where(Directory.Exists)
                   .ToList()
                   ?? [];
        }
    }

    public string GetProjectWorkspaceUri(
        string profileId,
        string projectId)
    {
        lock (_gate)
        {
            return _state.ProjectMappings.FirstOrDefault(mapping =>
                       string.Equals(
                           mapping.ProfileId,
                           profileId,
                           StringComparison.OrdinalIgnoreCase)
                       && string.Equals(
                           mapping.ProjectId,
                           projectId,
                           StringComparison.OrdinalIgnoreCase))
                   ?.RemoteWorkspaceUri
                   ?? string.Empty;
        }
    }

    public void ApplyRoamingProjectReferences(
        string profileId,
        IEnumerable<WorkspaceProjectReference> projects)
    {
        ValidateId(profileId, "profile");
        ArgumentNullException.ThrowIfNull(projects);
        lock (_gate)
        {
            var next = _state.ProjectMappings.ToList();
            foreach (var project in projects)
            {
                ValidateId(project.Id, "project");
                if (!string.IsNullOrWhiteSpace(project.WorkspaceLocationUri)
                    && !IsRoamingWorkspaceUri(
                        project.WorkspaceLocationUri))
                {
                    throw new InvalidDataException(
                        "A roaming project URI is malformed.");
                }

                var existing = next.FirstOrDefault(mapping =>
                    string.Equals(
                        mapping.ProfileId,
                        profileId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        mapping.ProjectId,
                        project.Id,
                        StringComparison.OrdinalIgnoreCase));
                var replacement = new WorkspaceProjectDeviceMapping
                {
                    ProfileId = profileId,
                    ProjectId = project.Id,
                    LocalDirectories = existing?.LocalDirectories ?? [],
                    RemoteWorkspaceUri = project.WorkspaceLocationUri
                };
                if (existing is not null)
                {
                    next.Remove(existing);
                }

                next.Add(replacement);
            }

            _state = CopyState(
                projectMappings: next.Take(MaximumRecords).ToList());
            Save();
        }
    }

    public LocalProfileRevision AdvanceLocalRevision(
        string profileId,
        string contentFingerprint)
    {
        ValidateId(profileId, "profile");
        ValidateSha256(contentFingerprint, "local content fingerprint");
        lock (_gate)
        {
            var current = _state.LocalProfileRevisions.FirstOrDefault(item =>
                string.Equals(
                    item.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase));
            if (current is not null
                && string.Equals(
                    current.ContentFingerprint,
                    contentFingerprint,
                    StringComparison.Ordinal))
            {
                return current;
            }

            var next = new LocalProfileRevision
            {
                ProfileId = profileId,
                Revision = checked((current?.Revision ?? 0) + 1),
                ContentFingerprint = contentFingerprint,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _state = CopyState(
                localProfileRevisions: _state.LocalProfileRevisions
                    .Where(item => !string.Equals(
                        item.ProfileId,
                        profileId,
                        StringComparison.OrdinalIgnoreCase))
                    .Append(next)
                    .Take(MaximumRecords)
                    .ToList());
            Save();
            return next;
        }
    }

    public string WriteConflictJson(
        string profileName,
        string role,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length is <= 0
            or > MaximumConflictBytes)
        {
            throw new InvalidOperationException(
                "A roaming-profile conflict copy has an invalid size.");
        }

        Directory.CreateDirectory(ConflictDirectory);
        var fileName =
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Safe(profileName)}-{Safe(role)}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(ConflictDirectory, fileName);
        WriteAtomic(path, payload);
        return path;
    }

    private ProfileRoamingDeviceState Load()
    {
        if (!File.Exists(StatePath))
        {
            return new ProfileRoamingDeviceState();
        }

        try
        {
            var info = new FileInfo(StatePath);
            if (info.Length is <= 0 or > MaximumStateBytes)
            {
                throw new InvalidDataException(
                    "The roaming device-state file has an invalid size.");
            }

            var state = JsonSerializer.Deserialize<ProfileRoamingDeviceState>(
                            File.ReadAllBytes(StatePath),
                            JsonOptions)
                        ?? throw new JsonException(
                            "The roaming device-state file was empty.");
            Validate(state);
            return state;
        }
        catch (Exception exception) when (exception is
                                          IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or InvalidDataException
                                          or ProfileSyncException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            LastLoadWarning =
                $"Roaming device state could not be loaded; no cloud data was overwritten: {exception.Message}";
            return new ProfileRoamingDeviceState();
        }
    }

    private void Save()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions);
        if (payload.Length > MaximumStateBytes)
        {
            throw new InvalidOperationException(
                "The roaming device-state file exceeds its 2 MiB limit.");
        }

        WriteAtomic(StatePath, payload);
    }

    private static void Validate(ProfileRoamingDeviceState state)
    {
        if (state.SchemaVersion
            != ProfileRoamingDeviceState.CurrentSchemaVersion
            || state.ProfileCheckpoints.Count > MaximumRecords
            || state.ConversationCheckpoints.Count > MaximumRecords
            || state.ProjectMappings.Count > MaximumRecords
            || state.LocalProfileRevisions.Count > MaximumRecords)
        {
            throw new InvalidDataException(
                "The roaming device-state schema or record count is unsupported.");
        }

        foreach (var checkpoint in state.ProfileCheckpoints)
        {
            ValidateProfileCheckpoint(checkpoint);
        }

        foreach (var checkpoint in state.ConversationCheckpoints)
        {
            ValidateConversationCheckpoint(checkpoint);
        }

        foreach (var mapping in state.ProjectMappings)
        {
            ValidateId(mapping.ProfileId, "profile");
            ValidateId(mapping.ProjectId, "project");
            if (mapping.LocalDirectories.Count > 128
                || mapping.LocalDirectories.Any(path =>
                    string.IsNullOrWhiteSpace(path)
                    || !Path.IsPathFullyQualified(path)
                    || IsRoamingWorkspaceUri(path))
                || (!string.IsNullOrWhiteSpace(mapping.RemoteWorkspaceUri)
                    && !IsRoamingWorkspaceUri(
                        mapping.RemoteWorkspaceUri)))
            {
                throw new InvalidDataException(
                    "A device-local project mapping is malformed.");
            }
        }

        foreach (var revision in state.LocalProfileRevisions)
        {
            ValidateId(revision.ProfileId, "profile");
            ValidateSha256(
                revision.ContentFingerprint,
                "local content fingerprint");
            if (revision.Revision < 1
                || revision.UpdatedAtUtc == default
                || revision.UpdatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                throw new InvalidDataException(
                    "A local profile revision is malformed.");
            }
        }
    }

    private static void ValidateProfileCheckpoint(
        RoamingProfileCheckpoint checkpoint)
    {
        ValidateEndpointAndId(
            checkpoint.EndpointKey,
            checkpoint.ProfileId,
            "profile");
        ValidateStrongETag(checkpoint.ETag);
        ValidateSha256(checkpoint.ContentSha256, "content hash");
        ValidateSha256(checkpoint.LocalFingerprint, "local fingerprint");
        if (checkpoint.ServerRevision < 1)
        {
            throw new InvalidDataException(
                "A roaming profile checkpoint has no server revision.");
        }
    }

    private static void ValidateConversationCheckpoint(
        RoamingConversationCheckpoint checkpoint)
    {
        ValidateEndpointAndId(
            checkpoint.EndpointKey,
            checkpoint.ConversationId,
            "conversation");
        ValidateStrongETag(checkpoint.ETag);
        ValidateSha256(
            checkpoint.LocalFingerprint,
            "conversation local fingerprint");
        if (checkpoint.ServerRevision < 1
            || checkpoint.LastUploadedSequence < 0)
        {
            throw new InvalidDataException(
                "A conversation checkpoint has invalid revisions.");
        }
    }

    private static void ValidateEndpointAndId(
        string endpoint,
        string id,
        string label)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                "A roaming checkpoint endpoint is malformed.");
        }

        ValidateId(id, label);
    }

    private static void ValidateId(string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Length > 128
            || !char.IsAsciiLetterOrDigit(id[0])
            || id.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new InvalidDataException($"The {label} ID is malformed.");
        }
    }

    private static void ValidateSha256(string value, string label)
    {
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException($"The {label} is malformed.");
        }
    }

    private static void ValidateStrongETag(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            || value.Length is < 3 or > 258
            || value[0] != '"'
            || value[^1] != '"'
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "A roaming checkpoint requires a strong ETag.");
        }
    }

    private static bool IsRoamingWorkspaceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "ssh" or "workspace";

    private static string? TryNormalizeLocalPath(string value)
    {
        try
        {
            var path = Path.GetFullPath(value);
            return Path.IsPathFullyQualified(path)
                ? Path.TrimEndingDirectorySeparator(path)
                : null;
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            return null;
        }
    }

    private ProfileRoamingDeviceState CopyState(
        IReadOnlyList<RoamingProfileCheckpoint>? profileCheckpoints = null,
        IReadOnlyList<RoamingConversationCheckpoint>?
            conversationCheckpoints = null,
        IReadOnlyList<WorkspaceProjectDeviceMapping>? projectMappings = null,
        IReadOnlyList<LocalProfileRevision>? localProfileRevisions = null) =>
        new()
        {
            ProfileCheckpoints = profileCheckpoints
                                 ?? _state.ProfileCheckpoints,
            ConversationCheckpoints = conversationCheckpoints
                                      ?? _state.ConversationCheckpoints,
            ProjectMappings = projectMappings ?? _state.ProjectMappings,
            LocalProfileRevisions = localProfileRevisions
                                    ?? _state.LocalProfileRevisions
        };

    private static void WriteAtomic(string path, ReadOnlySpan<byte> payload)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException(
                            "The roaming state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Safe(string value)
    {
        var result = new string((value ?? string.Empty)
            .Take(80)
            .Select(character => char.IsLetterOrDigit(character)
                || character is '-' or '_'
                ? character
                : '-')
            .ToArray())
            .Trim('-', ' ');
        return string.IsNullOrWhiteSpace(result) ? "profile" : result;
    }
}
