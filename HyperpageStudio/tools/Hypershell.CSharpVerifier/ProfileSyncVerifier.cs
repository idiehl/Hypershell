using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class ProfileSyncVerifier
{
    private const string Endpoint = "https://profiles.example.test/team-a/";
    private const string TokenSentinel = "sync-token-must-never-reach-disk";

    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "profile-sync");
        var configurationPath = Path.Combine(root, "profile-sync.device.json");
        var statePath = Path.Combine(root, "profile-sync-checkpoints.device.json");
        var conflictDirectory = Path.Combine(root, "conflicts");
        Directory.CreateDirectory(root);

        var portableProfiles = new PortableProfileService();
        var extensions = new ExtensionDiscoverySnapshot
        {
            RootDirectory = Path.Combine(root, "extensions"),
            ScannedAtUtc = DateTimeOffset.UtcNow
        };
        var deviceState = new ProfileSyncDeviceStateService(
            configurationPath,
            statePath,
            conflictDirectory);
        var coordinator = new ProfileSyncCoordinator(
            portableProfiles,
            deviceState);
        var server = new InMemoryProfileServer(portableProfiles, extensions);

        deviceState.SaveConfiguration(
            new ProfileSyncDeviceConfiguration
            {
                ServerEndpoint = Endpoint,
                AccountId = "verifier.test"
            });
        var savedConfiguration = deviceState.LoadConfiguration();
        Require(
            savedConfiguration.ServerEndpoint == Endpoint
            && savedConfiguration.AccountId == "verifier.test"
            && File.ReadAllText(configurationPath).Contains(
                "\"accountLabel\"",
                StringComparison.Ordinal),
            "The account ID did not round-trip through the compatible device-configuration wire field.");
        RequireThrows(
            () => deviceState.SaveConfiguration(
                new ProfileSyncDeviceConfiguration
                {
                    ServerEndpoint = Endpoint,
                    AccountId = "invalid@account"
                }),
            ProfileSyncFailureKind.Configuration);

        var settings = CreateSettings(
            "profile-sync-verifier",
            "Verifier profile",
            revision: 1,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var terminalProfiles = settings.TerminalProfiles.ToList();

        var created = await coordinator.UploadAsync(
            server,
            Endpoint,
            settings,
            extensions,
            terminalProfiles);
        Require(
            created.Kind == ProfileSyncUploadOutcomeKind.Created
            && created.RemoteProfile is { ServerRevision: 1 },
            "The first profile upload was not a conditional create.");
        Require(server.PutCount == 1, "The create did not reach the server exactly once.");
        var createdCheckpoint = RequireCheckpoint(
            deviceState,
            settings.PortableProfileId);
        Require(
            createdCheckpoint.ETag == created.RemoteProfile!.ETag,
            "The successful create ETag was not checkpointed.");

        var unchanged = await coordinator.UploadAsync(
            server,
            Endpoint,
            settings,
            extensions,
            terminalProfiles);
        Require(
            unchanged.Kind == ProfileSyncUploadOutcomeKind.Unchanged
            && server.PutCount == 1,
            "An unchanged profile was uploaded again instead of using its stable hash.");

        settings.Application.ShowSaveStatusInTitleBar = false;
        AdvancePortableRevision(settings);
        var updated = await coordinator.UploadAsync(
            server,
            Endpoint,
            settings,
            extensions,
            terminalProfiles);
        Require(
            updated.Kind == ProfileSyncUploadOutcomeKind.Updated
            && server.LastPutExpectedETag == createdCheckpoint.ETag,
            "The changed profile was not uploaded with its prior strong ETag.");
        var preConflictCheckpoint = RequireCheckpoint(
            deviceState,
            settings.PortableProfileId);

        var remoteSettings = CreateSettings(
            settings.PortableProfileId,
            "Verifier profile",
            revision: settings.PortableProfileRevision + 1,
            settings.PortableProfileUpdatedAtUtc.AddMinutes(1));
        remoteSettings.Explorer.ShowHiddenItems = true;
        server.ForceReplace(
            remoteSettings.PortableProfileId,
            CreatePayload(portableProfiles, remoteSettings, extensions));

        settings.CommandBuilder.OpenBuilderWhenPathsAreInserted = false;
        AdvancePortableRevision(settings);
        var localHashBeforeConflict = ComputeHash(
            portableProfiles,
            settings,
            extensions);
        var conflict = await coordinator.UploadAsync(
            server,
            Endpoint,
            settings,
            extensions,
            terminalProfiles);
        Require(
            conflict.Kind == ProfileSyncUploadOutcomeKind.Conflict,
            "A stale upload did not produce an explicit conflict.");
        Require(
            File.Exists(conflict.LocalConflictPath)
            && File.Exists(conflict.RemoteConflictPath),
            "The local and remote conflict bundles were not both preserved.");
        Require(
            conflict.RemoteConflictPreview?.CanImport == true,
            "The preserved remote conflict bundle did not pass the local inspector.");
        Require(
            portableProfiles.Inspect(
                    conflict.LocalConflictPath,
                    extensions,
                    terminalProfiles)
                .CanImport,
            "The preserved local conflict bundle was invalid.");
        Require(
            ComputeHash(portableProfiles, settings, extensions)
                == localHashBeforeConflict,
            "Conflict handling mutated the active local profile.");
        Require(
            RequireCheckpoint(deviceState, settings.PortableProfileId).ETag
                == preConflictCheckpoint.ETag,
            "A conflict silently advanced the local ETag checkpoint.");
        var partialConflict = await coordinator.UploadAsync(
            new ConflictFetchFailureTransport(),
            Endpoint,
            settings,
            extensions,
            terminalProfiles);
        Require(
            partialConflict.Kind == ProfileSyncUploadOutcomeKind.Conflict
            && File.Exists(partialConflict.LocalConflictPath)
            && partialConflict.RemoteConflictPath.Length == 0
            && partialConflict.RemoteConflictFailure.Length > 0,
            "A post-conflict remote fetch failure obscured the conflict or lost the local copy.");
        Require(
            RequireCheckpoint(deviceState, settings.PortableProfileId).ETag
                == preConflictCheckpoint.ETag,
            "A partially preserved conflict advanced the checkpoint.");

        var checkpointBeforeDownload = RequireCheckpoint(
            deviceState,
            settings.PortableProfileId);
        var download = await coordinator.DownloadPreviewAsync(
            server,
            Endpoint,
            settings.PortableProfileId,
            extensions,
            terminalProfiles);
        Require(download.Preview.CanImport, "The valid remote profile failed download inspection.");
        Require(
            RequireCheckpoint(deviceState, settings.PortableProfileId).ETag
                == checkpointBeforeDownload.ETag,
            "Previewing a download advanced the sync checkpoint before Save.");

        var adoptedSettings = portableProfiles.CreateMergedSettings(
            settings,
            download.Preview.Envelope!.Profile,
            WorkspacePanelRegistry.CreateWithBuiltIns());
        coordinator.AcceptDownloadedProfile(
            download,
            adoptedSettings,
            extensions);
        Require(
            RequireCheckpoint(deviceState, settings.PortableProfileId).ETag
                == download.ETag,
            "Saving an inspected remote profile did not adopt its ETag checkpoint.");

        var deleteConflictSettings = CreateSettings(
            "delete-conflict-verifier",
            "Delete conflict verifier",
            revision: 1,
            new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero));
        await coordinator.UploadAsync(
            server,
            Endpoint,
            deleteConflictSettings,
            extensions,
            deleteConflictSettings.TerminalProfiles.ToList());
        var staleDeleteSelection = (await server.ListAsync())
            .Single(profile => profile.Id == deleteConflictSettings.PortableProfileId);
        var newerDeleteSettings = CreateSettings(
            deleteConflictSettings.PortableProfileId,
            deleteConflictSettings.PortableProfileDisplayName,
            revision: 2,
            deleteConflictSettings.PortableProfileUpdatedAtUtc.AddMinutes(1));
        newerDeleteSettings.Application.CreateTerminalOnStartup = false;
        server.ForceReplace(
            newerDeleteSettings.PortableProfileId,
            CreatePayload(portableProfiles, newerDeleteSettings, extensions));
        var deleteConflict = await coordinator.DeleteAsync(
            server,
            Endpoint,
            staleDeleteSelection);
        Require(
            deleteConflict.Kind == ProfileSyncDeleteOutcomeKind.Conflict,
            "A stale delete was not blocked by its ETag precondition.");

        var selectedCurrent = (await server.ListAsync())
            .Single(profile => profile.Id == adoptedSettings.PortableProfileId);
        var localHashBeforeDelete = ComputeHash(
            portableProfiles,
            adoptedSettings,
            extensions);
        var deleted = await coordinator.DeleteAsync(
            server,
            Endpoint,
            selectedCurrent);
        Require(
            deleted.Kind == ProfileSyncDeleteOutcomeKind.Deleted
            && deleted.Tombstone?.IsDeleted == true,
            "A current ETag did not produce a remote tombstone.");
        Require(
            ComputeHash(portableProfiles, adoptedSettings, extensions)
                == localHashBeforeDelete,
            "Remote tombstoning changed the local profile.");
        Require(
            RequireCheckpoint(deviceState, adoptedSettings.PortableProfileId)
                .IsRemoteDeleted,
            "The successful tombstone was not recorded in device-local state.");

        var stateBeforeFailures = File.ReadAllBytes(statePath);
        await ExpectFailureAsync(
            () => coordinator.ListAsync(
                new ThrowingProfileSyncTransport(
                    ProfileSyncFailureKind.Authentication)),
            ProfileSyncFailureKind.Authentication);
        var offlineSettings = CreateSettings(
            "offline-profile-verifier",
            "Offline verifier",
            revision: 1,
            DateTimeOffset.UtcNow);
        var offlineHash = ComputeHash(
            portableProfiles,
            offlineSettings,
            extensions);
        await ExpectFailureAsync(
            () => coordinator.UploadAsync(
                new ThrowingProfileSyncTransport(
                    ProfileSyncFailureKind.Network),
                Endpoint,
                offlineSettings,
                extensions,
                offlineSettings.TerminalProfiles.ToList()),
            ProfileSyncFailureKind.Network);
        Require(
            stateBeforeFailures.SequenceEqual(File.ReadAllBytes(statePath))
            && offlineHash == ComputeHash(
                portableProfiles,
                offlineSettings,
                extensions),
            "Authentication or offline failure changed profile or checkpoint state.");

        await VerifyHttpTransportAsync(
            CreatePayload(portableProfiles, offlineSettings, extensions),
            offlineSettings);
        await VerifyCloudDeviceAuthorizationHttpAsync();
        await ProfileSessionLifecycleVerifier.VerifyAsync(root);
        Require(
            !Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Any(path => File.ReadAllText(path).Contains(
                    TokenSentinel,
                    StringComparison.Ordinal)),
            "The session-only bearer token leaked to a device-local file.");

        Console.WriteLine(
            "PROFILE_SYNC_OK|conditional-create-update|stable-noop|conflict-copies|inspected-download|tombstones|auth-offline-safe|http-guards|device-auth-http|dpapi-credentials|app-session-restore-renew-forget");
    }

    private static async Task VerifyCloudDeviceAuthorizationHttpAsync()
    {
        const string deviceCode =
            "hsd_dddddddddddddddddddddddddddddddddddddddddddd";
        const string firstRefreshToken =
            "hsr_1111rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr";
        const string secondRefreshToken =
            "hsr_2222rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr";
        const string clientDeviceId =
            "0123456789abcdef0123456789abcdef";
        var now = DateTimeOffset.UtcNow;
        var requestNumber = 0;
        var handler = new RecordingHandler(async request =>
        {
            requestNumber++;
            var expectedPath = requestNumber switch
            {
                1 => "/team-a/api/hypershell/v2/auth/device",
                2 or 3 => "/team-a/api/hypershell/v2/auth/device/token",
                4 => "/team-a/api/hypershell/v2/auth/refresh",
                5 =>
                    $"/team-a/api/hypershell/v2/client-devices/{clientDeviceId}",
                _ => string.Empty
            };
            Require(
                request.RequestUri?.AbsolutePath == expectedPath,
                "The cloud device client did not preserve the configured endpoint path.");

            if (requestNumber == 5)
            {
                Require(
                    request.Method == HttpMethod.Delete
                    && request.Headers.Authorization?.Scheme == "Bearer"
                    && request.Headers.Authorization.Parameter
                    == "hps_2222aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "Device revocation omitted its bearer token or target identity.");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            Require(
                request.Method == HttpMethod.Post
                && request.Content?.Headers.ContentType?.MediaType
                == "application/json",
                "A cloud authorization request used the wrong method or media type.");
            var requestContent = request.Content
                ?? throw new InvalidOperationException(
                    "A cloud authorization request had no JSON payload.");
            var body = await requestContent.ReadAsStringAsync();
            Require(
                body.Contains("\"protocolVersion\":2", StringComparison.Ordinal),
                "A cloud authorization request omitted its protocol version.");

            return requestNumber switch
            {
                1 => CreateJsonResponse(
                    HttpStatusCode.Created,
                    new DeviceAuthorizationResponse
                    {
                        DeviceCode = deviceCode,
                        UserCode = "ABCD-1234",
                        VerificationUri =
                            "https://profiles.example.test/Device",
                        ExpiresAtUtc = now.AddMinutes(10),
                        PollIntervalSeconds = 2
                    }),
                2 => new HttpResponseMessage(HttpStatusCode.Accepted),
                3 => CreateJsonResponse(
                    HttpStatusCode.OK,
                    CreateCloudSession(
                        now,
                        clientDeviceId,
                        "hps_1111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        firstRefreshToken)),
                4 => CreateJsonResponse(
                    HttpStatusCode.OK,
                    CreateCloudSession(
                        now,
                        clientDeviceId,
                        "hps_2222aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        secondRefreshToken)),
                _ => throw new InvalidOperationException(
                    "Unexpected cloud authorization request.")
            };
        });

        await using (var client = new HttpCloudDeviceAuthorizationClient(
                         Endpoint,
                         handler))
        {
            var challenge = await client.StartAsync(
                new DeviceAuthorizationRequest
                {
                    DeviceName = "Verifier PC",
                    Platform = "Windows verifier",
                    DeviceFingerprint =
                        "hsi_0123456789abcdef0123456789abcdef"
                });
            Require(
                challenge.UserCode == "ABCD-1234"
                && HttpCloudDeviceAuthorizationClient
                    .CreateCompleteVerificationUri(challenge)
                    .Query == "?userCode=ABCD-1234",
                "The desktop did not construct the complete portal verification URL.");
            Require(
                (await client.PollAsync(deviceCode)).Kind
                == CloudDeviceAuthorizationPollKind.Pending,
                "HTTP 202 was not classified as pending device approval.");
            var approved = await client.PollAsync(deviceCode);
            Require(
                approved is
                {
                    Kind: CloudDeviceAuthorizationPollKind.Approved,
                    Session.RefreshToken: firstRefreshToken
                },
                "The approved device response was not validated and classified.");
            var refreshed = await client.RefreshAsync(firstRefreshToken);
            Require(
                refreshed.RefreshToken == secondRefreshToken,
                "The rotated cloud session response was not returned.");
            await client.RevokeDeviceAsync(
                refreshed.AccessToken,
                clientDeviceId);
            Require(
                requestNumber == 5,
                "The cloud authorization HTTP lifecycle was incomplete.");
        }

        await ExpectFailureAsync(
            async () =>
            {
                await using var redirect =
                    new HttpCloudDeviceAuthorizationClient(
                        Endpoint,
                        new RecordingHandler(_ => Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.Redirect))));
                await redirect.StartAsync(
                    new DeviceAuthorizationRequest());
            },
            ProfileSyncFailureKind.Protocol);
        await ExpectFailureAsync(
            async () =>
            {
                await using var oversized =
                    new HttpCloudDeviceAuthorizationClient(
                        Endpoint,
                        new RecordingHandler(_ => Task.FromResult(
                            new HttpResponseMessage(HttpStatusCode.Created)
                            {
                                Content = new ByteArrayContent(
                                    new byte[
                                        HypershellCloudContract
                                            .MaximumDeviceAuthorizationResponseBytes
                                        + 1])
                            })));
                await oversized.StartAsync(
                    new DeviceAuthorizationRequest());
            },
            ProfileSyncFailureKind.Protocol);
        await ExpectFailureAsync(
            async () =>
            {
                await using var malformed =
                    new HttpCloudDeviceAuthorizationClient(
                        Endpoint,
                        new RecordingHandler(_ => Task.FromResult(
                            CreateJsonResponse(
                                HttpStatusCode.Created,
                                new
                                {
                                    protocolVersion = 2,
                                    unexpected = true
                                }))));
                await malformed.StartAsync(
                    new DeviceAuthorizationRequest());
            },
            ProfileSyncFailureKind.Protocol);
        RequireThrows(
            () => new HttpCloudDeviceAuthorizationClient(
                "http://profiles.example.test/"),
            ProfileSyncFailureKind.Configuration);
    }

    private static CloudSessionResponse CreateCloudSession(
        DateTimeOffset now,
        string clientDeviceId,
        string accessToken,
        string refreshToken) =>
        new()
        {
            AccountId = "verifier.test",
            AccountDisplayName = "Verifier",
            ClientDeviceId = clientDeviceId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = now.AddMinutes(10),
            RefreshTokenExpiresAtUtc = now.AddDays(30)
        };

    private static HttpResponseMessage CreateJsonResponse<T>(
        HttpStatusCode statusCode,
        T value)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(
                JsonSerializer.SerializeToUtf8Bytes(
                    value,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }))
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return response;
    }

    private static async Task VerifyHttpTransportAsync(
        ReadOnlyMemory<byte> payload,
        AppSettings profileSettings)
    {
        var responseRevision = 1L;
        var requestNumber = 0;
        var handler = new RecordingHandler(async request =>
        {
            requestNumber++;
            Require(
                request.Headers.Authorization?.Scheme == "Bearer"
                && request.Headers.Authorization.Parameter == TokenSentinel,
                "The HTTP transport did not apply the session bearer token.");
            Require(
                request.RequestUri?.AbsolutePath
                    == $"/team-a/{ProfileSyncContract.RelativeProfilesPath}/{profileSettings.PortableProfileId}",
                "The HTTP transport did not preserve the configured endpoint path.");

            if (requestNumber == 1)
            {
                Require(
                    request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*"),
                    "A first HTTP upload did not send If-None-Match: *.");
            }
            else
            {
                Require(
                    request.Headers.IfMatch.Single().Tag == $"\"http-{requestNumber - 1}\"",
                    "An HTTP update/delete did not send the expected If-Match ETag.");
            }

            if (request.Content is not null)
            {
                _ = await request.Content.ReadAsByteArrayAsync();
                Require(
                    request.Content.Headers.ContentType?.MediaType
                        == ProfileSyncContract.ProfileMediaType,
                    "An HTTP upload used the wrong media type.");
            }

            var isDelete = request.Method == HttpMethod.Delete;
            var summary = CreateSummary(
                profileSettings,
                $"\"http-{requestNumber}\"",
                ++responseRevision,
                isDelete);
            return CreateReceiptResponse(
                isDelete ? HttpStatusCode.OK : requestNumber == 1
                    ? HttpStatusCode.Created
                    : HttpStatusCode.OK,
                summary);
        });
        await using (var transport = new HttpProfileSyncTransport(
                         Endpoint,
                         TokenSentinel,
                         handler))
        {
            var create = await transport.PutAsync(
                profileSettings.PortableProfileId,
                payload,
                expectedETag: null);
            Require(
                create.Status == ProfileSyncMutationStatus.Created,
                "The HTTP create response was not classified as Created.");
            var update = await transport.PutAsync(
                profileSettings.PortableProfileId,
                payload,
                "\"http-1\"");
            Require(
                update.Status == ProfileSyncMutationStatus.Updated,
                "The HTTP update response was not classified as Updated.");
            var delete = await transport.DeleteAsync(
                profileSettings.PortableProfileId,
                "\"http-2\"");
            Require(
                delete.Status == ProfileSyncMutationStatus.Deleted,
                "The HTTP delete response was not classified as Deleted.");
        }

        await ExpectFailureAsync(
            async () =>
            {
                await using var redirect = new HttpProfileSyncTransport(
                    Endpoint,
                    TokenSentinel,
                    new RecordingHandler(_ => Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.Redirect))));
                await redirect.ListAsync();
            },
            ProfileSyncFailureKind.Protocol);
        await ExpectFailureAsync(
            async () =>
            {
                await using var oversized = new HttpProfileSyncTransport(
                    Endpoint,
                    TokenSentinel,
                    new RecordingHandler(_ => Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(
                                new byte[ProfileSyncContract.MaximumListResponseBytes + 1])
                        })));
                await oversized.ListAsync();
            },
            ProfileSyncFailureKind.Protocol);
        await ExpectFailureAsync(
            async () =>
            {
                await using var weakETag = new HttpProfileSyncTransport(
                    Endpoint,
                    TokenSentinel,
                    new RecordingHandler(_ =>
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(payload.ToArray())
                        };
                        response.Headers.TryAddWithoutValidation("ETag", "W/\"weak\"");
                        response.Headers.TryAddWithoutValidation(
                            ProfileSyncContract.ServerRevisionHeader,
                            "1");
                        return Task.FromResult(response);
                    }));
                await weakETag.FetchAsync(profileSettings.PortableProfileId);
            },
            ProfileSyncFailureKind.Protocol);
        await ExpectFailureAsync(
            async () =>
            {
                await using var unauthorized = new HttpProfileSyncTransport(
                    Endpoint,
                    TokenSentinel,
                    new RecordingHandler(_ => Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.Unauthorized))));
                await unauthorized.ListAsync();
            },
            ProfileSyncFailureKind.Authentication);

        RequireThrows(
            () => new HttpProfileSyncTransport(
                "http://profiles.example.test/",
                TokenSentinel),
            ProfileSyncFailureKind.Configuration);
        RequireThrows(
            () => new HttpProfileSyncTransport(
                Endpoint,
                TokenSentinel + "\ninvalid"),
            ProfileSyncFailureKind.Authentication);
    }

    private static HttpResponseMessage CreateReceiptResponse(
        HttpStatusCode statusCode,
        RemoteProfileSummary summary)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(
                JsonSerializer.SerializeToUtf8Bytes(
                    new ProfileSyncMutationReceipt
                    {
                        Profile = summary
                    },
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }))
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        response.Headers.ETag = new EntityTagHeaderValue(summary.ETag);
        response.Headers.TryAddWithoutValidation(
            ProfileSyncContract.ServerRevisionHeader,
            summary.ServerRevision.ToString());
        return response;
    }

    private static RemoteProfileSummary CreateSummary(
        AppSettings settings,
        string eTag,
        long serverRevision,
        bool isDeleted) =>
        new()
        {
            Id = settings.PortableProfileId,
            DisplayName = settings.PortableProfileDisplayName,
            ServerRevision = serverRevision,
            ProfileRevision = settings.PortableProfileRevision,
            UpdatedAtUtc = settings.PortableProfileUpdatedAtUtc,
            ProfileSha256 = new string('A', 64),
            ETag = eTag,
            IsDeleted = isDeleted
        };

    private static AppSettings CreateSettings(
        string id,
        string displayName,
        long revision,
        DateTimeOffset updatedAtUtc)
    {
        var settings = new AppSettings
        {
            PortableProfileId = id,
            PortableProfileDisplayName = displayName,
            PortableProfileRevision = revision,
            PortableProfileUpdatedAtUtc = updatedAtUtc
        };
        settings.WorkspaceLayout = WorkspaceLayoutService.CreateDefault(
            WorkspacePanelRegistry.CreateWithBuiltIns());
        return settings;
    }

    private static void AdvancePortableRevision(AppSettings settings)
    {
        settings.PortableProfileRevision++;
        settings.PortableProfileUpdatedAtUtc =
            settings.PortableProfileUpdatedAtUtc.AddMinutes(1);
    }

    private static ReadOnlyMemory<byte> CreatePayload(
        PortableProfileService service,
        AppSettings settings,
        ExtensionDiscoverySnapshot extensions) =>
        service.SerializeEnvelope(
            service.CreateEnvelope(
                service.CreateSnapshot(settings, extensions)));

    private static string ComputeHash(
        PortableProfileService service,
        AppSettings settings,
        ExtensionDiscoverySnapshot extensions) =>
        service.ComputeProfileSha256(
            service.CreateSnapshot(settings, extensions));

    private static ProfileSyncCheckpoint RequireCheckpoint(
        ProfileSyncDeviceStateService state,
        string profileId) =>
        state.FindCheckpoint(Endpoint, profileId)
        ?? throw new InvalidOperationException(
            $"No device-local checkpoint exists for '{profileId}'.");

    private static async Task ExpectFailureAsync(
        Func<Task> action,
        ProfileSyncFailureKind expectedKind)
    {
        try
        {
            await action();
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == expectedKind)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected profile-sync failure '{expectedKind}'.");
    }

    private static void RequireThrows(
        Action action,
        ProfileSyncFailureKind expectedKind)
    {
        try
        {
            action();
        }
        catch (ProfileSyncException exception) when (
            exception.Kind == expectedKind)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected profile-sync construction failure '{expectedKind}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class InMemoryProfileServer : IProfileSyncTransport
    {
        private readonly PortableProfileService _portableProfiles;
        private readonly ExtensionDiscoverySnapshot _extensions;
        private readonly Dictionary<string, StoredProfile> _profiles =
            new(StringComparer.OrdinalIgnoreCase);
        private long _serverRevision;

        public InMemoryProfileServer(
            PortableProfileService portableProfiles,
            ExtensionDiscoverySnapshot extensions)
        {
            _portableProfiles = portableProfiles;
            _extensions = extensions;
        }

        public int PutCount { get; private set; }
        public string? LastPutExpectedETag { get; private set; }

        public Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteProfileSummary>>(
                _profiles.Values
                    .Select(profile => profile.Summary)
                    .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        public Task<ProfileSyncFetchResult> FetchAsync(
            string profileId,
            CancellationToken cancellationToken = default)
        {
            if (!_profiles.TryGetValue(profileId, out var stored)
                || stored.Summary.IsDeleted)
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.NotFound,
                    "The in-memory profile was not found.");
            }

            return Task.FromResult(
                new ProfileSyncFetchResult(
                    profileId,
                    stored.Summary.ETag,
                    stored.Summary.ServerRevision,
                    stored.Payload));
        }

        public Task<ProfileSyncMutationResult> PutAsync(
            string profileId,
            ReadOnlyMemory<byte> profilePayload,
            string? expectedETag,
            CancellationToken cancellationToken = default)
        {
            PutCount++;
            LastPutExpectedETag = expectedETag;
            var exists = _profiles.TryGetValue(profileId, out var existing);
            if ((!exists && expectedETag is not null)
                || (exists && expectedETag is null)
                || (exists && !string.Equals(
                    existing!.Summary.ETag,
                    expectedETag,
                    StringComparison.Ordinal)))
            {
                return Task.FromResult(
                    new ProfileSyncMutationResult(
                        ProfileSyncMutationStatus.Conflict));
            }

            var summary = Store(profileId, profilePayload, isDeleted: false);
            return Task.FromResult(
                new ProfileSyncMutationResult(
                    exists
                        ? ProfileSyncMutationStatus.Updated
                        : ProfileSyncMutationStatus.Created,
                    summary));
        }

        public Task<ProfileSyncMutationResult> DeleteAsync(
            string profileId,
            string expectedETag,
            CancellationToken cancellationToken = default)
        {
            if (!_profiles.TryGetValue(profileId, out var existing)
                || !string.Equals(
                    existing.Summary.ETag,
                    expectedETag,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new ProfileSyncMutationResult(
                        ProfileSyncMutationStatus.Conflict));
            }

            var revision = ++_serverRevision;
            var tombstone = new RemoteProfileSummary
            {
                Id = existing.Summary.Id,
                DisplayName = existing.Summary.DisplayName,
                ServerRevision = revision,
                ProfileRevision = existing.Summary.ProfileRevision,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ProfileSha256 = existing.Summary.ProfileSha256,
                ETag = $"\"memory-{revision}\"",
                IsDeleted = true
            };
            _profiles[profileId] = existing with { Summary = tombstone };
            return Task.FromResult(
                new ProfileSyncMutationResult(
                    ProfileSyncMutationStatus.Deleted,
                    tombstone));
        }

        public void ForceReplace(
            string profileId,
            ReadOnlyMemory<byte> payload) =>
            _ = Store(profileId, payload, isDeleted: false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private RemoteProfileSummary Store(
            string profileId,
            ReadOnlyMemory<byte> payload,
            bool isDeleted)
        {
            var preview = _portableProfiles.InspectPayload(
                payload,
                $"memory://{profileId}",
                _extensions,
                []);
            if (!preview.CanImport
                || preview.Envelope is not { } envelope
                || !string.Equals(
                    envelope.Profile.Id,
                    profileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    "The in-memory server received an invalid profile.");
            }

            var revision = ++_serverRevision;
            var summary = new RemoteProfileSummary
            {
                Id = profileId,
                DisplayName = envelope.Profile.DisplayName,
                ServerRevision = revision,
                ProfileRevision = envelope.Profile.Revision,
                UpdatedAtUtc = envelope.Profile.UpdatedAtUtc,
                ProfileSha256 = envelope.ProfileSha256,
                ETag = $"\"memory-{revision}\"",
                IsDeleted = isDeleted
            };
            _profiles[profileId] = new StoredProfile(
                payload.ToArray(),
                summary);
            return summary;
        }

        private sealed record StoredProfile(
            byte[] Payload,
            RemoteProfileSummary Summary);
    }

    private sealed class ThrowingProfileSyncTransport : IProfileSyncTransport
    {
        private readonly ProfileSyncFailureKind _kind;

        public ThrowingProfileSyncTransport(ProfileSyncFailureKind kind)
        {
            _kind = kind;
        }

        public Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<RemoteProfileSummary>>(Failure());

        public Task<ProfileSyncFetchResult> FetchAsync(
            string profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProfileSyncFetchResult>(Failure());

        public Task<ProfileSyncMutationResult> PutAsync(
            string profileId,
            ReadOnlyMemory<byte> profilePayload,
            string? expectedETag,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProfileSyncMutationResult>(Failure());

        public Task<ProfileSyncMutationResult> DeleteAsync(
            string profileId,
            string expectedETag,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProfileSyncMutationResult>(Failure());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private ProfileSyncException Failure() =>
            new(_kind, $"Synthetic {_kind} failure.");
    }

    private sealed class ConflictFetchFailureTransport : IProfileSyncTransport
    {
        public Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteProfileSummary>>([]);

        public Task<ProfileSyncFetchResult> FetchAsync(
            string profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ProfileSyncFetchResult>(
                new ProfileSyncException(
                    ProfileSyncFailureKind.Network,
                    "Synthetic post-conflict fetch failure."));

        public Task<ProfileSyncMutationResult> PutAsync(
            string profileId,
            ReadOnlyMemory<byte> profilePayload,
            string? expectedETag,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProfileSyncMutationResult(
                    ProfileSyncMutationStatus.Conflict));

        public Task<ProfileSyncMutationResult> DeleteAsync(
            string profileId,
            string expectedETag,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProfileSyncMutationResult(
                    ProfileSyncMutationStatus.Conflict));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public RecordingHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request);
    }
}
