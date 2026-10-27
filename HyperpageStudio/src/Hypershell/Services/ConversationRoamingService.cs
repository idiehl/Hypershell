using Hypershell.Profiles;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

/// <summary>
/// Synchronizes complete local conversation archives incrementally. Provider
/// continuation context remains bounded, while the stored item list is
/// paginated and has no fixed conversation-length cap.
/// </summary>
public sealed partial class ConversationRoamingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32
    };

    [GeneratedRegex(
        "(?:sk|hps)_[A-Za-z0-9_-]{20,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(
        "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----.*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PrivateKeyPattern();

    private readonly LocalAiConversationStore _localStore;
    private readonly ProfileRoamingDeviceStateService _deviceState;

    public ConversationRoamingService(
        LocalAiConversationStore localStore,
        ProfileRoamingDeviceStateService deviceState)
    {
        _localStore = localStore;
        _deviceState = deviceState;
    }

    public async Task<ConversationRoamingSyncOutcome> UploadAllAsync(
        ICloudRoamingTransport transport,
        string endpoint,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var uploaded = 0;
        var unchanged = 0;
        var conflicts = new List<string>();
        foreach (var document in _localStore.Load(profileId))
        {
            var outcome = await UploadOneAsync(
                transport,
                endpoint,
                document,
                cancellationToken);
            if (outcome.Count == 0)
            {
                unchanged++;
            }
            else if (outcome.ConflictPaths.Count > 0)
            {
                conflicts.AddRange(outcome.ConflictPaths);
            }
            else
            {
                uploaded++;
            }
        }

        return new ConversationRoamingSyncOutcome(
            uploaded,
            DownloadedConversations: 0,
            unchanged,
            conflicts);
    }

    public async Task<ConversationRoamingSyncOutcome> DownloadAllAsync(
        ICloudRoamingTransport transport,
        string endpoint,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var summaries = await ListAllAsync(
            transport,
            profileId,
            cancellationToken);
        var local = _localStore.Load(profileId).ToDictionary(
            document => document.Id,
            StringComparer.OrdinalIgnoreCase);
        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var downloaded = 0;
        var unchanged = 0;
        var conflicts = new List<string>();
        foreach (var summary in summaries.Where(item => !item.IsDeleted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remote = await DownloadOneAsync(
                transport,
                summary.Id,
                cancellationToken);
            local.TryGetValue(remote.Id, out var current);
            var checkpoint = _deviceState.FindConversationCheckpoint(
                endpointKey,
                remote.Id);
            if (current is not null
                && checkpoint is not null
                && !string.Equals(
                    checkpoint.LocalFingerprint,
                    LocalAiConversationStore.Fingerprint(current),
                    StringComparison.Ordinal))
            {
                conflicts.Add(_localStore.WriteConflictCopy(current, "local"));
                conflicts.Add(_localStore.WriteConflictCopy(remote, "remote"));
                continue;
            }

            if (current is not null)
            {
                remote = remote with
                {
                    IsOpenOnThisDevice = current.IsOpenOnThisDevice,
                    ProviderId = current.ProviderId,
                    ModelId = current.ModelId,
                    WorkingDirectory = current.WorkingDirectory
                };
            }

            var fingerprint = LocalAiConversationStore.Fingerprint(remote);
            if (current is not null
                && string.Equals(
                    fingerprint,
                    LocalAiConversationStore.Fingerprint(current),
                    StringComparison.Ordinal))
            {
                unchanged++;
            }
            else
            {
                _localStore.Save(remote);
                downloaded++;
            }

            _deviceState.SaveConversationCheckpoint(
                new RoamingConversationCheckpoint
                {
                    EndpointKey = endpointKey,
                    ConversationId = remote.Id,
                    ETag = summary.ETag,
                    ServerRevision = summary.Revision,
                    LastUploadedSequence = summary.LastSequence,
                    LocalFingerprint = fingerprint,
                    LastPulledAtUtc = DateTimeOffset.UtcNow,
                    LastPushedAtUtc = checkpoint?.LastPushedAtUtc,
                    IsRemoteDeleted = false
                });
        }

        return new ConversationRoamingSyncOutcome(
            UploadedConversations: 0,
            downloaded,
            unchanged,
            conflicts);
    }

    private async Task<(int Count, IReadOnlyList<string> ConflictPaths)>
        UploadOneAsync(
            ICloudRoamingTransport transport,
            string endpoint,
            LocalAiConversationDocument document,
            CancellationToken cancellationToken)
    {
        LocalAiConversationStore.Validate(document);
        var endpointKey = ProfileSyncEndpoint.CreateEndpointKey(endpoint);
        var checkpoint = _deviceState.FindConversationCheckpoint(
            endpointKey,
            document.Id);
        var fingerprint = LocalAiConversationStore.Fingerprint(document);
        if (checkpoint is { IsRemoteDeleted: false }
            && string.Equals(
                checkpoint.LocalFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return (0, []);
        }

        var pending = document.Items
            .Where(item => item.Sequence
                           > (checkpoint?.LastUploadedSequence ?? 0))
            .Select(ToInput)
            .ToList();
        var expectedETag = checkpoint?.ETag;
        var localUploadedSequence = checkpoint?.LastUploadedSequence ?? 0;
        AiConversationSummary? remote = null;
        var mutationCount = 0;
        do
        {
            var batch = TakeBoundedBatch(
                document,
                pending,
                localUploadedSequence);
            var request = new AiConversationMutationRequest
            {
                ProfileId = document.ProfileId,
                Title = Sanitize(document.Title, 200),
                CompactedContext = SanitizeCompaction(
                    document.CompactedContext),
                Items = batch
            };
            var mutation = await transport.PutConversationAsync(
                document.Id,
                request,
                expectedETag,
                cancellationToken);
            if (mutation.Status == CloudMutationStatus.Conflict)
            {
                var paths = new List<string>
                {
                    _localStore.WriteConflictCopy(document, "local")
                };
                try
                {
                    paths.Add(_localStore.WriteConflictCopy(
                        await DownloadOneAsync(
                            transport,
                            document.Id,
                            cancellationToken),
                        "remote"));
                }
                catch (ProfileSyncException exception) when (
                    exception.Kind == ProfileSyncFailureKind.NotFound)
                {
                }

                return (1, paths);
            }

            remote = mutation.Conversation
                     ?? throw new ProfileSyncException(
                         ProfileSyncFailureKind.Protocol,
                         "The account service returned no conversation receipt.");
            mutationCount++;
            expectedETag = remote.ETag;
            localUploadedSequence += batch.Count;
            if (batch.Count > 0)
            {
                pending.RemoveRange(0, batch.Count);
            }
        }
        while (pending.Count > 0);

        _deviceState.SaveConversationCheckpoint(
            new RoamingConversationCheckpoint
            {
                EndpointKey = endpointKey,
                ConversationId = document.Id,
                ETag = remote!.ETag,
                ServerRevision = remote.Revision,
                LastUploadedSequence = localUploadedSequence,
                LocalFingerprint = fingerprint,
                LastPushedAtUtc = DateTimeOffset.UtcNow,
                LastPulledAtUtc = checkpoint?.LastPulledAtUtc,
                IsRemoteDeleted = false
            });
        return (mutationCount, []);
    }

    private static IReadOnlyList<AiConversationItemInput> TakeBoundedBatch(
        LocalAiConversationDocument document,
        IReadOnlyList<AiConversationItemInput> pending,
        long lastUploadedSequence)
    {
        if (pending.Count == 0)
        {
            return [];
        }

        var maximum = Math.Min(
            pending.Count,
            HypershellCloudContract.MaximumConversationItemsPerMutation);
        for (var count = maximum; count >= 1; count--)
        {
            var batch = pending.Take(count).ToList();
            var request = new AiConversationMutationRequest
            {
                ProfileId = document.ProfileId,
                Title = Sanitize(document.Title, 200),
                CompactedContext = SanitizeCompaction(
                    document.CompactedContext),
                Items = batch
            };
            if (JsonSerializer.SerializeToUtf8Bytes(
                    request,
                    JsonOptions).Length
                <= HypershellCloudContract.MaximumConversationMutationBytes)
            {
                return batch;
            }
        }

        throw new InvalidDataException(
            $"Conversation item {lastUploadedSequence + 1} cannot fit in a bounded cloud mutation.");
    }

    private static AiConversationItemInput ToInput(AiConversationItem item) =>
        new()
        {
            Role = item.Role,
            Kind = item.Kind,
            Text = Sanitize(
                item.Text,
                item.Kind == AiConversationItemKind.Message
                    ? HypershellCloudContract
                        .MaximumConversationTextCharacters
                    : 16 * 1024),
            ActionName = Sanitize(item.ActionName, 120),
            TargetUri = item.TargetUri,
            Result = Sanitize(item.Result, 16 * 1024),
            ContentSha256 = item.ContentSha256,
            CreatedAtUtc = item.CreatedAtUtc.ToUniversalTime()
        };

    private static string SanitizeCompaction(string value)
    {
        var sanitized = Sanitize(
            value,
            HypershellCloudContract.MaximumConversationCompactionCharacters);
        return sanitized.Contains("data:image/", StringComparison.OrdinalIgnoreCase)
               || sanitized.Contains(
                   "\"image_url\"",
                   StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : sanitized;
    }

    private static string Sanitize(string value, int maximumCharacters)
    {
        var normalized = (value ?? string.Empty).Replace("\0", string.Empty);
        normalized = PrivateKeyPattern().Replace(
            normalized,
            "[REDACTED_PRIVATE_KEY]");
        normalized = TokenPattern().Replace(
            normalized,
            "[REDACTED_TOKEN]");
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..Math.Max(0, maximumCharacters - 16)]
              + "\n[TRUNCATED]";
    }

    private static async Task<IReadOnlyList<AiConversationSummary>>
        ListAllAsync(
            ICloudRoamingTransport transport,
            string profileId,
            CancellationToken cancellationToken)
    {
        var result = new List<AiConversationSummary>();
        var cursor = string.Empty;
        do
        {
            var page = await transport.ListConversationsAsync(
                profileId,
                string.IsNullOrWhiteSpace(cursor) ? null : cursor,
                pageSize: 100,
                cancellationToken);
            result.AddRange(page.Conversations);
            cursor = page.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));
        return result;
    }

    private static async Task<LocalAiConversationDocument> DownloadOneAsync(
        ICloudRoamingTransport transport,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var items = new List<AiConversationItem>();
        AiConversationSummary? summary = null;
        var compacted = string.Empty;
        long after = 0;
        while (true)
        {
            var page = await transport.GetConversationPageAsync(
                conversationId,
                after,
                HypershellCloudContract.MaximumConversationItemsPerPage,
                cancellationToken);
            summary = page.Conversation;
            compacted = page.CompactedContext;
            items.AddRange(page.Items);
            if (page.NextAfterSequence is not { } next)
            {
                break;
            }

            if (next <= after)
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    "Conversation pagination did not advance.");
            }

            after = next;
        }

        return new LocalAiConversationDocument
        {
            Id = summary!.Id,
            ProfileId = summary.ProfileId,
            Title = summary.Title,
            IsOpenOnThisDevice = false,
            CompactedContext = compacted,
            Items = items,
            CreatedAtUtc = summary.CreatedAtUtc,
            UpdatedAtUtc = summary.UpdatedAtUtc
        };
    }
}
