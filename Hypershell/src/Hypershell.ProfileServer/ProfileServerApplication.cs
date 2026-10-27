using Hypershell.Profiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace Hypershell.ProfileServer;

public static class ProfileServerApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    public static WebApplication Build(
        ProfileServerOptions options,
        string[]? hostArguments = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = hostArguments ?? [],
                ApplicationName = typeof(ProfileServerApplication)
                    .Assembly
                    .GetName()
                    .Name,
                ContentRootPath = ResolveContentRoot()
            });
        builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = 1;
            // Kestrel is bound to host loopback in production, so only the
            // colocated reverse proxy can reach it.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        });
        builder.Services.AddSingleton(normalized);
        builder.Services.AddSingleton<ServerPortableProfileValidator>();
        builder.Services.AddSingleton<ProfileAccountStore>();
        builder.Services.AddSingleton<FileProfileStore>();
        builder.Services.AddHostedService<ProfileStoreMigrationHostedService>();
        builder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        builder.Services.AddSingleton<CloudMetadataStore>();
        builder.Services.AddSingleton<StorageCapacityService>();
        builder.Services.AddSingleton<BackupExportStore>();
        builder.Services.AddSingleton<ReleaseStore>();
        builder.Services.AddSingleton<WorkspaceProfileRoamingStore>();
        builder.Services.AddSingleton<ConversationStore>();
        builder.Services.AddSingleton<HyperlibVaultStore>();
        builder.Services.AddSingleton<SshVaultMasterKeyStore>();
        builder.Services.AddSingleton<SshSecretEnvelopeService>();
        builder.Services.AddSingleton<SshCredentialVaultStore>();
        builder.Services.AddSingleton<RemoteHostStore>();
        builder.Services.AddSingleton<CredentialReleaseService>();
        var protectionKeys = Path.Combine(
            normalized.DataRoot,
            "data-protection-keys");
        Directory.CreateDirectory(protectionKeys);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                protectionKeys,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
        var dataProtection = builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(protectionKeys))
            .SetApplicationName("Hypershell.Cloud");
        if (normalized.DataProtectionCertificatePath is not null)
        {
            dataProtection.ProtectKeysWithCertificate(
                LoadDataProtectionCertificate(normalized));
        }
        builder.Services.AddSingleton<AccountSecurityStore>();
        builder.Services
            .AddAuthentication("HypershellAdmin")
            .AddCookie(
                "HypershellAdmin",
                cookie =>
                {
                    cookie.Cookie.Name = "__Host-HypershellAdmin";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.SecurePolicy =
                        normalized.RequireHttpsPortalCookies
                            ? CookieSecurePolicy.Always
                            : CookieSecurePolicy.SameAsRequest;
                    cookie.LoginPath = "/Account/Login";
                    cookie.AccessDeniedPath = "/Account/Login";
                    cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                    cookie.SlidingExpiration = true;
                });
        builder.Services.AddAuthorization();
        builder.Services.AddRazorPages(
            pages =>
            {
                pages.Conventions.AuthorizeFolder("/");
                pages.Conventions.AllowAnonymousToPage("/Account/Login");
                pages.Conventions.AllowAnonymousToPage("/PublicDownloads");
            });
        builder.Services.AddAntiforgery(
            antiforgery =>
            {
                antiforgery.Cookie.Name = "__Host-HypershellAntiforgery";
                antiforgery.Cookie.HttpOnly = true;
                antiforgery.Cookie.SameSite = SameSiteMode.Strict;
                antiforgery.Cookie.SecurePolicy =
                    normalized.RequireHttpsPortalCookies
                        ? CookieSecurePolicy.Always
                        : CookieSecurePolicy.SameAsRequest;
            });
        builder.Services.AddSingleton(
            new ProfileSessionTokenService(
                normalized,
                timeProvider ?? TimeProvider.System));
        builder.Services.AddSingleton<DeviceAuthorizationService>();
        builder.Services.AddRateLimiter(rateLimits =>
        {
            rateLimits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimits.AddPolicy(
                "profile-sign-in",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRemotePartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            rateLimits.AddPolicy(
                "device-authorization",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRemotePartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            rateLimits.AddPolicy(
                "device-token",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRemotePartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            rateLimits.AddPolicy(
                "profile-api",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    GetRemotePartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 240,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        var app = builder.Build();
        app.Services
            .GetRequiredService<CloudMetadataStore>()
            .InitializeAsync()
            .GetAwaiter()
            .GetResult();
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features
                    .Get<IExceptionHandlerFeature>()
                    ?.Error;
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ProfileServer")
                    .LogError(exception, "Unhandled profile-server request failure.");
                context.Response.StatusCode =
                    StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "server_error",
                        message = "The profile server could not complete the request."
                    },
                    JsonOptions);
            });
        });
        app.UseForwardedHeaders();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api")
                && !context.Request.Path.StartsWithSegments("/healthz"))
            {
                context.Response.Headers.CacheControl = "no-cache, no-store";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers["X-Content-Type-Options"] =
                    "nosniff";
            }

            await next(context);
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet(
            "/healthz",
            (StorageCapacityService storage, CloudMetadataStore cloud) =>
            {
                var capacity = storage.Inspect();
                var metadata = cloud.InspectAsync().GetAwaiter().GetResult();
                return Results.Json(
                    new
                    {
                        status = capacity.CanAcceptWrites
                            ? "ready"
                            : "storage-reserve",
                        protocolVersion = ProfileSyncContract.CurrentProtocolVersion,
                        cloudProtocolVersion =
                            HypershellCloudContract.CurrentProtocolVersion,
                        metadataSchemaVersion = metadata.SchemaVersion,
                        storageCanAcceptWrites = capacity.CanAcceptWrites
                    },
                    JsonOptions);
            });

        app.MapGet(
                "/" + HypershellCloudContract.RelativeStoragePath,
                GetStorageStatusAsync)
            .RequireRateLimiting("profile-api");
        app.MapPost(
                "/" + HypershellCloudContract.RelativeDeviceAuthorizationPath,
                StartDeviceAuthorizationAsync)
            .AllowAnonymous()
            .RequireRateLimiting("device-authorization");
        app.MapPost(
                "/" + HypershellCloudContract.RelativeDeviceTokenPath,
                PollDeviceAuthorizationAsync)
            .AllowAnonymous()
            .RequireRateLimiting("device-token");
        app.MapPost(
                "/" + HypershellCloudContract.RelativeRefreshTokenPath,
                RefreshDeviceSessionAsync)
            .AllowAnonymous()
            .RequireRateLimiting("device-token");
        var clientDevicesPath =
            "/" + HypershellCloudContract.RelativeClientDevicesPath;
        app.MapGet(clientDevicesPath, ListClientDevicesAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(
                clientDevicesPath + "/{deviceId}",
                RevokeClientDeviceAsync)
            .RequireRateLimiting("profile-api");

        var remoteHostsPath =
            "/" + HypershellCloudContract.RelativeRemoteHostsPath;
        app.MapGet(remoteHostsPath, ListRemoteHostsAsync)
            .RequireRateLimiting("profile-api");
        app.MapPost(remoteHostsPath, CreateRemoteHostAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(remoteHostsPath + "/{hostId}", UpdateRemoteHostAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(
                remoteHostsPath + "/{hostId}/host-key",
                PinRemoteHostKeyAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(remoteHostsPath + "/{hostId}", DisableRemoteHostAsync)
            .RequireRateLimiting("profile-api");

        var sshCredentialsPath =
            "/" + HypershellCloudContract.RelativeSshCredentialsPath;
        app.MapGet(sshCredentialsPath, ListSshCredentialsAsync)
            .RequireRateLimiting("profile-api");
        app.MapPost(sshCredentialsPath, CreateSshCredentialAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(
                sshCredentialsPath + "/{credentialId}",
                RotateSshCredentialAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(
                sshCredentialsPath + "/{credentialId}",
                RevokeSshCredentialAsync)
            .RequireRateLimiting("profile-api");
        app.MapPost(
                sshCredentialsPath + "/{credentialId}/release",
                StartCredentialReleaseAsync)
            .RequireRateLimiting("profile-api");
        app.MapPost(
                "/" + HypershellCloudContract.RelativeCredentialReleasePath
                + "/token",
                PollCredentialReleaseAsync)
            .RequireRateLimiting("device-token");

        var workspaceProfilesPath =
            "/" + HypershellCloudContract.RelativeWorkspaceProfilesPath;
        app.MapGet(workspaceProfilesPath, ListWorkspaceProfilesAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(
                workspaceProfilesPath + "/{profileId}",
                GetWorkspaceProfileAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(
                workspaceProfilesPath + "/{profileId}",
                PutWorkspaceProfileAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(
                workspaceProfilesPath + "/{profileId}",
                DeleteWorkspaceProfileAsync)
            .RequireRateLimiting("profile-api");

        var conversationsPath =
            "/" + HypershellCloudContract.RelativeConversationsPath;
        app.MapGet(conversationsPath, ListConversationsAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(
                conversationsPath + "/{conversationId}",
                GetConversationAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(
                conversationsPath + "/{conversationId}",
                PutConversationAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(
                conversationsPath + "/{conversationId}",
                DeleteConversationAsync)
            .RequireRateLimiting("profile-api");

        var packagesPath =
            "/" + HypershellCloudContract.RelativePackagesPath;
        app.MapGet(packagesPath, ListHyperlibPackagesAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(
                packagesPath + "/{contentSha256}",
                DownloadHyperlibPackageAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(
                packagesPath + "/{packageId}/{version}/{contentSha256}",
                PutHyperlibPackageAsync)
            .RequireRateLimiting("profile-api");

        var releasesPath =
            "/" + HypershellCloudContract.RelativeReleasesPath;
        app.MapPublicDownloads();
        app.MapGet(releasesPath, ListReleasesAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(releasesPath + "/latest", GetLatestReleaseAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(
                releasesPath + "/{version}/manifest",
                GetReleaseManifestAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(
                releasesPath + "/{version}/artifacts/{kind}",
                DownloadReleaseArtifactAsync)
            .RequireRateLimiting("profile-api");

        app.MapPost(
                "/" + ProfileSessionContract.RelativeSessionPath,
                SignInAsync)
            .RequireRateLimiting("profile-sign-in");
        app.MapDelete(
                "/" + ProfileSessionContract.RelativeSessionPath,
                SignOutAsync)
            .RequireRateLimiting("profile-api");

        var profilesPath = "/" + ProfileSyncContract.RelativeProfilesPath;
        app.MapGet(profilesPath, ListProfilesAsync)
            .RequireRateLimiting("profile-api");
        app.MapGet(profilesPath + "/{profileId}", FetchProfileAsync)
            .RequireRateLimiting("profile-api");
        app.MapPut(profilesPath + "/{profileId}", PutProfileAsync)
            .RequireRateLimiting("profile-api");
        app.MapDelete(profilesPath + "/{profileId}", DeleteProfileAsync)
            .RequireRateLimiting("profile-api");
        app.MapRazorPages();
        return app;
    }

    private static string ResolveContentRoot()
    {
        var current = Directory.GetCurrentDirectory();
        var sourceProject = Path.Combine(
            current,
            "src",
            "Hypershell.ProfileServer");
        return Directory.Exists(Path.Combine(sourceProject, "Pages"))
            ? sourceProject
            : current;
    }

    internal static X509Certificate2 LoadDataProtectionCertificate(
        ProfileServerOptions options)
    {
        var certificatePath = options.DataProtectionCertificatePath
                              ?? throw new InvalidOperationException(
                                  "The data-protection certificate path is missing.");
        var passwordPath = options.DataProtectionCertificatePasswordPath
                           ?? throw new InvalidOperationException(
                               "The data-protection certificate password path is missing.");
        if (!File.Exists(certificatePath) || !File.Exists(passwordPath))
        {
            throw new InvalidOperationException(
                "The configured data-protection certificate or password file is unavailable.");
        }

        var password = File.ReadAllText(passwordPath).TrimEnd('\r', '\n');
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The data-protection certificate has no private key.");
        }

        return certificate;
    }

    private static async Task<IResult> SignInAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "sign_in_media_type",
                "Use application/json for profile sign-in.");
        }

        ProfileSessionRequest request;
        try
        {
            request = await ReadJsonAsync<ProfileSessionRequest>(
                context.Request,
                ProfileSessionContract.MaximumSignInRequestBytes,
                cancellationToken);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_sign_in",
                exception.Message);
        }

        if (request.ProtocolVersion
            != ProfileSessionContract.CurrentProtocolVersion
            || string.IsNullOrWhiteSpace(request.AccessKey)
            || request.AccessKey.Length > 256
            || request.AccessKey.Any(char.IsControl))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_sign_in",
                "The sign-in request is malformed or uses an unsupported protocol version.");
        }

        var account = await accounts.VerifyAccessKeyAsync(
            request.AccountId,
            request.AccessKey,
            cancellationToken);
        if (account is null)
        {
            return Error(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "The account ID or access key is invalid.");
        }

        var issued = sessions.Issue(account);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        return Results.Json(
            new ProfileSessionResponse
            {
                AccountId = issued.Principal.AccountId,
                AccountDisplayName = issued.Principal.AccountDisplayName,
                AccessToken = issued.AccessToken,
                ExpiresAtUtc = issued.Principal.ExpiresAtUtc
            },
            JsonOptions);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        sessions.Revoke(authentication.AccessToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static async Task<IResult> ListProfilesAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        FileProfileStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var document = new ProfileSyncListDocument
        {
            Profiles = await store.ListAsync(
                authentication.Principal!.AccountId,
                cancellationToken)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.LongLength > ProfileSyncContract.MaximumListResponseBytes)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "metadata_limit",
                "The account's profile metadata exceeds protocol v1 limits.");
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private static async Task<IResult> FetchProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        FileProfileStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var fetched = await store.FetchAsync(
                authentication.Principal!.AccountId,
                profileId,
                cancellationToken);
            if (fetched is null)
            {
                return Error(
                    StatusCodes.Status404NotFound,
                    "profile_not_found",
                    "The requested profile does not exist.");
            }

            if (fetched.Summary.IsDeleted)
            {
                return Error(
                    StatusCodes.Status410Gone,
                    "profile_deleted",
                    "The requested profile is a tombstone.");
            }

            ApplyRevisionHeaders(context.Response, fetched.Summary);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(
                fetched.Payload.ToArray(),
                ProfileSyncContract.ProfileMediaType + "; charset=utf-8");
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_profile_id",
                exception.Message);
        }
    }

    private static async Task<IResult> PutProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        FileProfileStore store,
        ServerPortableProfileValidator validator,
        StorageCapacityService storage,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsProfileMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "profile_media_type",
                $"Use {ProfileSyncContract.ProfileMediaType} for profile uploads.");
        }

        if (!TryResolvePutPrecondition(
                context.Request,
                out var expectedETag,
                out var preconditionError))
        {
            return preconditionError!;
        }

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = await ReadBytesAsync(
                context.Request,
                ProfileContract.MaximumBundleBytes,
                cancellationToken);
        }

        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "profile_payload",
                exception.Message);
        }

        if (!storage.CanAccept(payload.Length))
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "storage_reserve",
                "The server is preserving its configured free-storage reserve.");
        }

        ValidatedServerProfile validated;
        try
        {
            validated = validator.Validate(payload, profileId);
        }
        catch (Exception exception) when (exception is
                                          ProfilePayloadValidationException
                                          or ArgumentException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_profile",
                exception.Message);
        }

        var mutation = await store.PutAsync(
            authentication.Principal!.AccountId,
            validated,
            expectedETag,
            cancellationToken);
        return mutation.Kind switch
        {
            ProfileStoreMutationKind.Conflict => Error(
                StatusCodes.Status412PreconditionFailed,
                "profile_conflict",
                "The profile ETag precondition did not match."),
            ProfileStoreMutationKind.StorageLimit => Error(
                StatusCodes.Status507InsufficientStorage,
                "storage_limit",
                "The account has reached its profile count or storage limit."),
            ProfileStoreMutationKind.Created => MutationReceipt(
                context.Response,
                StatusCodes.Status201Created,
                mutation.Profile!),
            _ => MutationReceipt(
                context.Response,
                StatusCodes.Status200OK,
                mutation.Profile!)
        };
    }

    private static async Task<IResult> GetStorageStatusAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        StorageCapacityService storage,
        CloudMetadataStore cloud,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var capacity = storage.Inspect();
        var metadata = await cloud.InspectAsync(cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(
            new CloudStorageStatus
            {
                TotalBytes = capacity.TotalBytes,
                AvailableBytes = capacity.AvailableBytes,
                ReservedBytes = capacity.ReservedBytes,
                CanAcceptWrites = capacity.CanAcceptWrites,
                MetadataSchemaVersion = metadata.SchemaVersion,
                CheckedAtUtc = capacity.CheckedAtUtc
            },
            JsonOptions);
    }

    private static async Task<IResult> StartDeviceAuthorizationAsync(
        HttpContext context,
        DeviceAuthorizationService authorizations,
        StorageCapacityService storage,
        ProfileServerOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "device_authorization_media_type",
                "Use application/json to authorize a client device.");
        }

        DeviceAuthorizationRequest request;
        try
        {
            request = await ReadJsonAsync<DeviceAuthorizationRequest>(
                context.Request,
                HypershellCloudContract
                    .MaximumDeviceAuthorizationRequestBytes,
                cancellationToken);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_device_authorization",
                exception.Message);
        }

        if (!storage.CanAccept(16 * 1024))
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "storage_reserve",
                "The server is preserving its configured free-storage reserve.");
        }

        try
        {
            var authorization = await authorizations.StartAsync(
                request,
                options.PublicPortalBaseUri,
                cancellationToken);
            ApplyNoStore(context.Response);
            return Results.Json(
                authorization,
                JsonOptions,
                statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_device_authorization",
                exception.Message);
        }
    }

    private static async Task<IResult> PollDeviceAuthorizationAsync(
        HttpContext context,
        DeviceAuthorizationService authorizations,
        CancellationToken cancellationToken)
    {
        var request = await ReadDeviceTokenRequestAsync(
            context,
            cancellationToken);
        if (request.Error is not null)
        {
            return request.Error;
        }

        var result = await authorizations.PollAsync(
            request.Request!.DeviceCode,
            cancellationToken);
        ApplyNoStore(context.Response);
        return result.Kind switch
        {
            DeviceAuthorizationPollKind.Approved => Results.Json(
                result.Session,
                JsonOptions),
            DeviceAuthorizationPollKind.Pending => Error(
                StatusCodes.Status202Accepted,
                "authorization_pending",
                "The client device is waiting for portal approval."),
            DeviceAuthorizationPollKind.Expired => Error(
                StatusCodes.Status410Gone,
                "device_code_expired",
                "The device authorization request has expired."),
            DeviceAuthorizationPollKind.Denied => Error(
                StatusCodes.Status403Forbidden,
                "device_authorization_denied",
                "The device authorization request was denied."),
            DeviceAuthorizationPollKind.Consumed => Error(
                StatusCodes.Status409Conflict,
                "device_code_consumed",
                "The device authorization request has already been used."),
            _ => Error(
                StatusCodes.Status400BadRequest,
                "invalid_device_code",
                "The device authorization request is invalid.")
        };
    }

    private static async Task<IResult> RefreshDeviceSessionAsync(
        HttpContext context,
        DeviceAuthorizationService authorizations,
        CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "refresh_media_type",
                "Use application/json to refresh a client session.");
        }

        RefreshTokenRequest request;
        try
        {
            request = await ReadJsonAsync<RefreshTokenRequest>(
                context.Request,
                HypershellCloudContract.MaximumDeviceTokenRequestBytes,
                cancellationToken);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_refresh",
                exception.Message);
        }

        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_refresh",
                "The refresh request uses an unsupported protocol version.");
        }

        var session = await authorizations.RefreshAsync(
            request.RefreshToken,
            cancellationToken);
        ApplyNoStore(context.Response);
        return session is null
            ? Error(
                StatusCodes.Status401Unauthorized,
                "invalid_refresh_token",
                "The refresh credential is invalid, expired, or revoked.")
            : Results.Json(session, JsonOptions);
    }

    private static async Task<IResult> ListClientDevicesAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        DeviceAuthorizationService authorizations,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var devices = await authorizations.ListDevicesAsync(
            authentication.Principal!.AccountId,
            cancellationToken);
        ApplyNoStore(context.Response);
        return Results.Json(devices, JsonOptions);
    }

    private static async Task<IResult> RevokeClientDeviceAsync(
        string deviceId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        DeviceAuthorizationService authorizations,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsDeviceId(deviceId))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_device_id",
                "The client-device ID is malformed.");
        }

        var revoked = await authorizations.RevokeDeviceAsync(
            authentication.Principal!.AccountId,
            deviceId,
            cancellationToken);
        ApplyNoStore(context.Response);
        return revoked
            ? Results.NoContent()
            : Error(
                StatusCodes.Status404NotFound,
                "device_not_found",
                "The active client device was not found.");
    }

    private static async Task<IResult> ListRemoteHostsAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        RemoteHostStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        return BoundedPrivateJson(
            context.Response,
            new RemoteHostList
            {
                Hosts = await store.ListAsync(
                    authentication.Principal!.AccountId,
                    cancellationToken)
            },
            HypershellCloudContract.MaximumRemoteHostListBytes,
            "remote_host_metadata_limit",
            "The account's remote-host metadata exceeds the protocol limit.");
    }

    private static async Task<IResult> CreateRemoteHostAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        RemoteHostStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var request = await TryReadRemoteHostRequestAsync(
            context,
            cancellationToken);
        if (request.Error is not null)
        {
            return request.Error;
        }

        try
        {
            var mutation = await store.CreateAsync(
                authentication.Principal!.AccountId,
                request.Request!,
                cancellationToken);
            return RemoteHostMutationResponse(
                context.Response,
                mutation,
                StatusCodes.Status201Created);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_remote_host",
                exception.Message);
        }
    }

    private static async Task<IResult> UpdateRemoteHostAsync(
        string hostId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        RemoteHostStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var request = await TryReadRemoteHostRequestAsync(
            context,
            cancellationToken);
        if (request.Error is not null)
        {
            return request.Error;
        }

        try
        {
            var mutation = await store.UpdateAsync(
                authentication.Principal!.AccountId,
                hostId,
                request.Request!,
                cancellationToken);
            return RemoteHostMutationResponse(
                context.Response,
                mutation,
                StatusCodes.Status200OK);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_remote_host",
                exception.Message);
        }
    }

    private static async Task<IResult> PinRemoteHostKeyAsync(
        string hostId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        RemoteHostStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "remote_host_key_media_type",
                "Use application/json to pin an SSH host key.");
        }

        try
        {
            var request = await ReadJsonAsync<RemoteHostKeyPinRequest>(
                context.Request,
                HypershellCloudContract.MaximumRemoteHostMutationBytes,
                cancellationToken);
            var mutation = await store.PinHostKeyAsync(
                authentication.Principal!.AccountId,
                hostId,
                request,
                cancellationToken);
            return RemoteHostMutationResponse(
                context.Response,
                mutation,
                StatusCodes.Status200OK);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_remote_host_key",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_remote_host_key",
                exception.Message);
        }
    }

    private static async Task<IResult> DisableRemoteHostAsync(
        string hostId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        RemoteHostStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var disabled = await store.DisableAsync(
                authentication.Principal!.AccountId,
                hostId,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return disabled
                ? Results.NoContent()
                : Error(
                    StatusCodes.Status404NotFound,
                    "remote_host_not_found",
                    "The active remote host was not found.");
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_remote_host_id",
                exception.Message);
        }
    }

    private static async Task<IResult> ListSshCredentialsAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        SshCredentialVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        return BoundedPrivateJson(
            context.Response,
            new SshCredentialList
            {
                Credentials = await store.ListAsync(
                    authentication.Principal!.AccountId,
                    cancellationToken)
            },
            HypershellCloudContract.MaximumSshCredentialListBytes,
            "ssh_credential_metadata_limit",
            "The account's SSH credential metadata exceeds the protocol limit.");
    }

    private static async Task<IResult> CreateSshCredentialAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        SshCredentialVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var request = await TryReadSshCredentialRequestAsync(
            context,
            cancellationToken);
        if (request.Error is not null)
        {
            return request.Error;
        }

        try
        {
            var mutation = await store.CreateAsync(
                authentication.Principal!.AccountId,
                request.Request!,
                cancellationToken);
            return SshCredentialMutationResponse(
                context.Response,
                mutation,
                StatusCodes.Status201Created);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_ssh_credential",
                exception.Message);
        }
        finally
        {
            ClearCredentialRequest(request.Request);
        }
    }

    private static async Task<IResult> RotateSshCredentialAsync(
        string credentialId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        SshCredentialVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var request = await TryReadSshCredentialRequestAsync(
            context,
            cancellationToken);
        if (request.Error is not null)
        {
            return request.Error;
        }

        try
        {
            var mutation = await store.RotateAsync(
                authentication.Principal!.AccountId,
                credentialId,
                request.Request!,
                cancellationToken);
            return SshCredentialMutationResponse(
                context.Response,
                mutation,
                StatusCodes.Status200OK);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_ssh_credential",
                exception.Message);
        }
        finally
        {
            ClearCredentialRequest(request.Request);
        }
    }

    private static async Task<IResult> RevokeSshCredentialAsync(
        string credentialId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        SshCredentialVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var revoked = await store.RevokeAsync(
                authentication.Principal!.AccountId,
                credentialId,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return revoked
                ? Results.NoContent()
                : Error(
                    StatusCodes.Status404NotFound,
                    "ssh_credential_not_found",
                    "The active SSH credential was not found.");
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_ssh_credential_id",
                exception.Message);
        }
    }

    private static async Task<IResult> StartCredentialReleaseAsync(
        string credentialId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        CredentialReleaseService releases,
        ProfileServerOptions options,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var authorization = await releases.StartAsync(
                authentication.Principal!,
                credentialId,
                options.PublicPortalBaseUri,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return Results.Json(
                authorization,
                JsonOptions,
                statusCode: StatusCodes.Status201Created);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(
                StatusCodes.Status403Forbidden,
                "browser_authorized_device_required",
                exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return Error(
                StatusCodes.Status404NotFound,
                "ssh_credential_not_found",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_ssh_credential_id",
                exception.Message);
        }
    }

    private static async Task<IResult> PollCredentialReleaseAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        CredentialReleaseService releases,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "credential_release_media_type",
                "Use application/json to poll a credential release.");
        }

        SshCredentialReleasePollRequest request;
        try
        {
            request = await ReadJsonAsync<SshCredentialReleasePollRequest>(
                context.Request,
                HypershellCloudContract.MaximumCredentialReleaseRequestBytes,
                cancellationToken);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_credential_release",
                exception.Message);
        }

        if (request.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_credential_release",
                "The credential-release request uses an unsupported protocol version.");
        }

        var result = await releases.PollAsync(
            authentication.Principal!,
            request.ReleaseToken,
            cancellationToken);
        ApplyPrivateNoStore(context.Response);
        if (result.Kind == CredentialReleasePollKind.Approved
            && result.Credential is { } credential)
        {
            try
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    credential,
                    JsonOptions);
                if (payload.Length
                    > HypershellCloudContract
                        .MaximumCredentialReleaseResponseBytes)
                {
                    return Error(
                        StatusCodes.Status507InsufficientStorage,
                        "credential_release_limit",
                        "The SSH credential exceeds the release protocol limit.");
                }

                return Results.Bytes(
                    payload,
                    "application/json; charset=utf-8");
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations
                    .ZeroMemory(credential.PrivateKeyUtf8);
                System.Security.Cryptography.CryptographicOperations
                    .ZeroMemory(credential.PassphraseUtf8);
            }
        }

        return result.Kind switch
        {
            CredentialReleasePollKind.Pending => Error(
                StatusCodes.Status202Accepted,
                "credential_release_pending",
                "Approve the credential release in the browser."),
            CredentialReleasePollKind.Expired => Error(
                StatusCodes.Status410Gone,
                "credential_release_expired",
                "The credential-release request expired."),
            CredentialReleasePollKind.Denied => Error(
                StatusCodes.Status403Forbidden,
                "credential_release_denied",
                "The credential-release request was denied or revoked."),
            CredentialReleasePollKind.Consumed => Error(
                StatusCodes.Status409Conflict,
                "credential_release_consumed",
                "The credential-release request has already been used."),
            CredentialReleasePollKind.CredentialChanged => Error(
                StatusCodes.Status409Conflict,
                "credential_release_changed",
                "The SSH credential changed after this release was requested."),
            _ => Error(
                StatusCodes.Status404NotFound,
                "credential_release_not_found",
                "The credential-release request is invalid.")
        };
    }

    private static async Task<RemoteHostReadOutcome>
        TryReadRemoteHostRequestAsync(
            HttpContext context,
            CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return new RemoteHostReadOutcome(
                null,
                Error(
                    StatusCodes.Status415UnsupportedMediaType,
                    "remote_host_media_type",
                    "Use application/json for remote-host changes."));
        }

        try
        {
            return new RemoteHostReadOutcome(
                await ReadJsonAsync<RemoteHostMutationRequest>(
                    context.Request,
                    HypershellCloudContract.MaximumRemoteHostMutationBytes,
                    cancellationToken),
                null);
        }
        catch (RequestPayloadException exception)
        {
            return new RemoteHostReadOutcome(
                null,
                Error(
                    exception.StatusCode,
                    "invalid_remote_host",
                    exception.Message));
        }
    }

    private static async Task<SshCredentialReadOutcome>
        TryReadSshCredentialRequestAsync(
            HttpContext context,
            CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return new SshCredentialReadOutcome(
                null,
                Error(
                    StatusCodes.Status415UnsupportedMediaType,
                    "ssh_credential_media_type",
                    "Use application/json for SSH credential changes."));
        }

        try
        {
            return new SshCredentialReadOutcome(
                await ReadJsonAsync<SshCredentialMutationRequest>(
                    context.Request,
                    HypershellCloudContract.MaximumSshCredentialMutationBytes,
                    cancellationToken),
                null);
        }
        catch (RequestPayloadException exception)
        {
            return new SshCredentialReadOutcome(
                null,
                Error(
                    exception.StatusCode,
                    "invalid_ssh_credential",
                    exception.Message));
        }
    }

    private static void ClearCredentialRequest(
        SshCredentialMutationRequest? request)
    {
        if (request is null)
        {
            return;
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(
            request.PrivateKeyUtf8);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(
            request.PassphraseUtf8);
    }

    private static IResult RemoteHostMutationResponse(
        HttpResponse response,
        RemoteHostMutationResult mutation,
        int successStatus)
    {
        ApplyPrivateNoStore(response);
        return mutation.Kind switch
        {
            SshVaultMutationKind.Conflict => Error(
                StatusCodes.Status409Conflict,
                "remote_host_conflict",
                "The remote host changed or its host-key pin did not match the expected value."),
            SshVaultMutationKind.NotFound => Error(
                StatusCodes.Status404NotFound,
                "remote_host_not_found",
                "The remote host was not found."),
            _ => Results.Json(
                new RemoteHostMutationReceipt
                {
                    Host = mutation.Host!
                },
                JsonOptions,
                statusCode: successStatus)
        };
    }

    private static IResult SshCredentialMutationResponse(
        HttpResponse response,
        SshCredentialMutationResult mutation,
        int successStatus)
    {
        ApplyPrivateNoStore(response);
        return mutation.Kind switch
        {
            SshVaultMutationKind.Conflict => Error(
                StatusCodes.Status409Conflict,
                "ssh_credential_conflict",
                "The SSH credential changed or the key-version precondition did not match."),
            SshVaultMutationKind.NotFound => Error(
                StatusCodes.Status404NotFound,
                "ssh_credential_not_found",
                "The SSH credential was not found."),
            _ => Results.Json(
                new SshCredentialMutationReceipt
                {
                    Credential = mutation.Credential!
                },
                JsonOptions,
                statusCode: successStatus)
        };
    }

    private static async Task<IResult> ListWorkspaceProfilesAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        WorkspaceProfileRoamingStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var document = new WorkspaceProfileRoamingList
        {
            Profiles = await store.ListAsync(
                authentication.Principal!.AccountId,
                cancellationToken)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.Length
            > HypershellCloudContract.MaximumWorkspaceProfileListBytes)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "workspace_profile_metadata_limit",
                "The account's workspace-profile metadata exceeds the protocol limit.");
        }

        ApplyPrivateNoStore(context.Response);
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private static async Task<IResult> GetWorkspaceProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        WorkspaceProfileRoamingStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var resource = await store.GetAsync(
                authentication.Principal!.AccountId,
                profileId,
                cancellationToken);
            if (resource is null)
            {
                return Error(
                    StatusCodes.Status404NotFound,
                    "workspace_profile_not_found",
                    "The roaming workspace profile does not exist.");
            }

            ApplyWorkspaceProfileHeaders(context.Response, resource.Profile);
            ApplyPrivateNoStore(context.Response);
            return resource.Profile.IsDeleted
                ? Error(
                    StatusCodes.Status410Gone,
                    "workspace_profile_deleted",
                    "The roaming workspace profile is a tombstone.")
                : Results.Json(resource, JsonOptions);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_workspace_profile",
                exception.Message);
        }
    }

    private static async Task<IResult> PutWorkspaceProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        WorkspaceProfileRoamingStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "workspace_profile_media_type",
                "Use application/json for roaming workspace profiles.");
        }

        if (!TryResolvePutPrecondition(
                context.Request,
                out var expectedETag,
                out var preconditionError))
        {
            return preconditionError!;
        }

        WorkspaceProfileRoamingDocument document;
        try
        {
            document = await ReadJsonAsync<WorkspaceProfileRoamingDocument>(
                context.Request,
                HypershellCloudContract.MaximumWorkspaceProfileBytes,
                cancellationToken);
            var mutation = await store.PutAsync(
                authentication.Principal!.AccountId,
                profileId,
                document,
                expectedETag,
                cancellationToken);
            if (mutation.Kind
                == WorkspaceProfileRoamingMutationKind.Conflict)
            {
                return Error(
                    StatusCodes.Status412PreconditionFailed,
                    "workspace_profile_conflict",
                    "The workspace-profile ETag precondition did not match.");
            }

            return WorkspaceProfileMutationReceipt(
                context.Response,
                mutation.Kind == WorkspaceProfileRoamingMutationKind.Created
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK,
                mutation.Profile!);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_workspace_profile",
                exception.Message);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_workspace_profile",
                exception.Message);
        }
    }

    private static async Task<IResult> DeleteWorkspaceProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        WorkspaceProfileRoamingStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!TryReadStrongETag(
                context.Request.Headers.IfMatch,
                out var expectedETag))
        {
            return Error(
                StatusCodes.Status428PreconditionRequired,
                "etag_required",
                "A single strong If-Match ETag is required.");
        }

        try
        {
            var mutation = await store.DeleteAsync(
                authentication.Principal!.AccountId,
                profileId,
                expectedETag,
                cancellationToken);
            return mutation.Kind
                   == WorkspaceProfileRoamingMutationKind.Conflict
                ? Error(
                    StatusCodes.Status412PreconditionFailed,
                    "workspace_profile_conflict",
                    "The workspace-profile ETag precondition did not match.")
                : WorkspaceProfileMutationReceipt(
                    context.Response,
                    StatusCodes.Status200OK,
                    mutation.Profile!);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_workspace_profile",
                exception.Message);
        }
    }

    private static async Task<IResult> ListConversationsAsync(
        string? profileId,
        string? cursor,
        int? pageSize,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ConversationStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var page = await store.ListAsync(
                authentication.Principal!.AccountId,
                profileId,
                cursor,
                pageSize ?? 50,
                cancellationToken);
            return BoundedConversationJson(context.Response, page);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException
                                          or FormatException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_conversation_query",
                exception.Message);
        }
    }

    private static async Task<IResult> GetConversationAsync(
        string conversationId,
        long? afterSequence,
        int? pageSize,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ConversationStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var page = await store.GetPageAsync(
                authentication.Principal!.AccountId,
                conversationId,
                afterSequence ?? 0,
                pageSize ?? 100,
                cancellationToken);
            if (page is null)
            {
                return Error(
                    StatusCodes.Status404NotFound,
                    "conversation_not_found",
                    "The AI conversation does not exist.");
            }

            ApplyConversationHeaders(context.Response, page.Conversation);
            ApplyPrivateNoStore(context.Response);
            return page.Conversation.IsDeleted
                ? Error(
                    StatusCodes.Status410Gone,
                    "conversation_deleted",
                    "The AI conversation is a tombstone.")
                : BoundedConversationJson(context.Response, page);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or ArgumentOutOfRangeException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_conversation_query",
                exception.Message);
        }
    }

    private static async Task<IResult> PutConversationAsync(
        string conversationId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ConversationStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "conversation_media_type",
                "Use application/json for AI conversation mutations.");
        }

        if (!TryResolvePutPrecondition(
                context.Request,
                out var expectedETag,
                out var preconditionError))
        {
            return preconditionError!;
        }

        try
        {
            var request = await ReadJsonAsync<AiConversationMutationRequest>(
                context.Request,
                HypershellCloudContract.MaximumConversationMutationBytes,
                cancellationToken);
            var mutation = await store.PutAsync(
                authentication.Principal!.AccountId,
                conversationId,
                request,
                expectedETag,
                cancellationToken);
            if (mutation.Kind == ConversationMutationKind.Conflict)
            {
                return Error(
                    StatusCodes.Status412PreconditionFailed,
                    "conversation_conflict",
                    "The conversation ETag precondition did not match.");
            }

            return ConversationMutationReceipt(
                context.Response,
                mutation.Kind == ConversationMutationKind.Created
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK,
                mutation.Conversation!);
        }
        catch (RequestPayloadException exception)
        {
            return Error(
                exception.StatusCode,
                "invalid_conversation",
                exception.Message);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_conversation",
                exception.Message);
        }
    }

    private static async Task<IResult> DeleteConversationAsync(
        string conversationId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ConversationStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!TryReadStrongETag(
                context.Request.Headers.IfMatch,
                out var expectedETag))
        {
            return Error(
                StatusCodes.Status428PreconditionRequired,
                "etag_required",
                "A single strong If-Match ETag is required.");
        }

        try
        {
            var mutation = await store.DeleteAsync(
                authentication.Principal!.AccountId,
                conversationId,
                expectedETag,
                cancellationToken);
            return mutation.Kind == ConversationMutationKind.Conflict
                ? Error(
                    StatusCodes.Status412PreconditionFailed,
                    "conversation_conflict",
                    "The conversation ETag precondition did not match.")
                : ConversationMutationReceipt(
                    context.Response,
                    StatusCodes.Status200OK,
                    mutation.Conversation!);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_conversation",
                exception.Message);
        }
    }

    private static async Task<IResult> ListHyperlibPackagesAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        HyperlibVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        var document = new HyperlibVaultPackageList
        {
            Packages = await store.ListAsync(
                authentication.Principal!.AccountId,
                cancellationToken)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.Length > HypershellCloudContract.MaximumPackageListBytes)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "package_metadata_limit",
                "The account's personal-package inventory exceeds the protocol limit.");
        }

        ApplyPrivateNoStore(context.Response);
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private static async Task<IResult> DownloadHyperlibPackageAsync(
        string contentSha256,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        HyperlibVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var package = await store.GetAsync(
                authentication.Principal!.AccountId,
                contentSha256,
                cancellationToken);
            if (package is null)
            {
                return Error(
                    StatusCodes.Status404NotFound,
                    "package_not_found",
                    "The personal Hyperlib package does not exist for this account.");
            }

            ApplyPrivateNoStore(context.Response);
            return Results.File(
                package.FullPath,
                HypershellCloudContract.HyperlibPackageMediaType,
                package.DownloadFileName,
                enableRangeProcessing: true);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_package_hash",
                exception.Message);
        }
    }

    private static async Task<IResult> PutHyperlibPackageAsync(
        string packageId,
        string version,
        string contentSha256,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        HyperlibVaultStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!IsMediaType(
                context.Request.ContentType,
                HypershellCloudContract.HyperlibPackageMediaType))
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                "package_media_type",
                $"Use {HypershellCloudContract.HyperlibPackageMediaType} for personal Hyperlib uploads.");
        }

        try
        {
            var result = await store.ImportAsync(
                authentication.Principal!.AccountId,
                packageId,
                version,
                contentSha256,
                context.Request.Body,
                context.Request.ContentLength,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return Results.Json(
                new HyperlibVaultUploadReceipt
                {
                    Package = result.Package
                },
                JsonOptions,
                statusCode: result.Disposition
                    == HyperlibVaultImportDisposition.Created
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK);
        }
        catch (IOException exception)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "package_storage_reserve",
                exception.Message);
        }
        catch (Exception exception) when (exception is
                                          ArgumentException
                                          or InvalidDataException
                                          or NotSupportedException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_package",
                exception.Message);
        }
    }

    private static async Task<IResult> ListReleasesAsync(
        string? channel,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ReleaseStore releases,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var inventory = await releases.ListAsync(
                channel,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return Results.Json(inventory, JsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_release_channel",
                exception.Message);
        }
    }

    private static async Task<IResult> GetLatestReleaseAsync(
        string? channel,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ReleaseStore releases,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var release = await releases.GetLatestAsync(
                string.IsNullOrWhiteSpace(channel)
                    ? HypershellReleaseContract.StableChannel
                    : channel,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return release is null
                ? Error(
                    StatusCodes.Status404NotFound,
                    "release_not_found",
                    "No compatible release has been published on that channel.")
                : Results.Json(release.SignedManifest, JsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_release_channel",
                exception.Message);
        }
    }

    private static async Task<IResult> GetReleaseManifestAsync(
        string version,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ReleaseStore releases,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        try
        {
            var release = await releases.GetAsync(
                version,
                cancellationToken);
            ApplyPrivateNoStore(context.Response);
            return release is null
                ? Error(
                    StatusCodes.Status404NotFound,
                    "release_not_found",
                    "That Hypershell release does not exist.")
                : Results.Json(release.SignedManifest, JsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_release_version",
                exception.Message);
        }
    }

    private static async Task<IResult> DownloadReleaseArtifactAsync(
        string version,
        string kind,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        ReleaseStore releases,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!Enum.TryParse<HypershellReleaseArtifactKind>(
                kind,
                ignoreCase: true,
                out var artifactKind))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_release_artifact",
                "The requested release artifact kind is invalid.");
        }

        try
        {
            var release = await releases.GetAsync(
                version,
                cancellationToken);
            var artifact = release?.Artifacts.SingleOrDefault(
                candidate => candidate.Artifact.Kind == artifactKind);
            if (artifact is null)
            {
                return Error(
                    StatusCodes.Status404NotFound,
                    "release_artifact_not_found",
                    "That release artifact does not exist.");
            }

            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(
                artifact.FullPath,
                "application/octet-stream",
                artifact.Artifact.FileName,
                enableRangeProcessing: true);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_release_version",
                exception.Message);
        }
    }

    private static async Task<DeviceTokenReadOutcome>
        ReadDeviceTokenRequestAsync(
            HttpContext context,
            CancellationToken cancellationToken)
    {
        if (!IsJsonMediaType(context.Request.ContentType))
        {
            return new DeviceTokenReadOutcome(
                null,
                Error(
                    StatusCodes.Status415UnsupportedMediaType,
                    "device_token_media_type",
                    "Use application/json to poll a device authorization."));
        }

        try
        {
            var request = await ReadJsonAsync<DeviceTokenRequest>(
                context.Request,
                HypershellCloudContract.MaximumDeviceTokenRequestBytes,
                cancellationToken);
            return request.ProtocolVersion
                   == HypershellCloudContract.CurrentProtocolVersion
                ? new DeviceTokenReadOutcome(request, null)
                : new DeviceTokenReadOutcome(
                    null,
                    Error(
                        StatusCodes.Status400BadRequest,
                        "invalid_device_token_request",
                        "The device-token request uses an unsupported protocol version."));
        }
        catch (RequestPayloadException exception)
        {
            return new DeviceTokenReadOutcome(
                null,
                Error(
                    exception.StatusCode,
                    "invalid_device_token_request",
                    exception.Message));
        }
    }

    private static bool IsDeviceId(string value) =>
        value.Length == 32
        && value.All(character => char.IsAsciiHexDigit(character));

    private static void ApplyNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }

    private static void ApplyPrivateNoStore(HttpResponse response)
    {
        ApplyNoStore(response);
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static async Task<IResult> DeleteProfileAsync(
        string profileId,
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        FileProfileStore store,
        CancellationToken cancellationToken)
    {
        var authentication = await AuthenticateAsync(
            context,
            accounts,
            sessions,
            cancellationToken);
        if (authentication.Error is not null)
        {
            return authentication.Error;
        }

        if (!TryReadStrongETag(
                context.Request.Headers.IfMatch,
                out var expectedETag))
        {
            return Error(
                StatusCodes.Status428PreconditionRequired,
                "etag_required",
                "A single strong If-Match ETag is required.");
        }

        try
        {
            var mutation = await store.DeleteAsync(
                authentication.Principal!.AccountId,
                profileId,
                expectedETag,
                cancellationToken);
            return mutation.Kind == ProfileStoreMutationKind.Conflict
                ? Error(
                    StatusCodes.Status412PreconditionFailed,
                    "profile_conflict",
                    "The profile ETag precondition did not match.")
                : MutationReceipt(
                    context.Response,
                    StatusCodes.Status200OK,
                    mutation.Profile!);
        }
        catch (ArgumentException exception)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "invalid_profile_id",
                exception.Message);
        }
    }

    private static async Task<AuthenticationOutcome> AuthenticateAsync(
        HttpContext context,
        ProfileAccountStore accounts,
        ProfileSessionTokenService sessions,
        CancellationToken cancellationToken)
    {
        var rawAuthorization = context.Request.Headers.Authorization;
        if (rawAuthorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(
                rawAuthorization[0],
                out var authorization)
            || !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter)
            || !sessions.TryAuthenticate(
                authorization.Parameter,
                out var principal)
            || principal is null)
        {
            return new AuthenticationOutcome(
                null,
                string.Empty,
                Error(
                    StatusCodes.Status401Unauthorized,
                    "session_required",
                    "A valid unexpired profile session is required."));
        }

        var account = await accounts.FindAsync(
            principal.AccountId,
            cancellationToken);
        if (account is null
            || account.IsDisabled
            || account.CredentialVersion != principal.CredentialVersion)
        {
            sessions.Revoke(authorization.Parameter);
            return new AuthenticationOutcome(
                null,
                string.Empty,
                Error(
                    account?.IsDisabled == true
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status401Unauthorized,
                    "session_revoked",
                    "The profile session is no longer valid."));
        }

        return new AuthenticationOutcome(
            principal,
            authorization.Parameter,
            null);
    }

    private static bool TryResolvePutPrecondition(
        HttpRequest request,
        out string? expectedETag,
        out IResult? error)
    {
        expectedETag = null;
        error = null;
        var ifNoneMatch = request.Headers.IfNoneMatch;
        var ifMatch = request.Headers.IfMatch;
        if (ifNoneMatch.Count == 1
            && string.Equals(
                ifNoneMatch[0]?.Trim(),
                "*",
                StringComparison.Ordinal)
            && ifMatch.Count == 0)
        {
            return true;
        }

        if (ifNoneMatch.Count == 0
            && TryReadStrongETag(ifMatch, out var strongETag))
        {
            expectedETag = strongETag;
            return true;
        }

        error = Error(
            StatusCodes.Status428PreconditionRequired,
            "etag_required",
            "Use either If-None-Match: * or one strong If-Match ETag.");
        return false;
    }

    private static bool TryReadStrongETag(
        Microsoft.Extensions.Primitives.StringValues values,
        out string eTag)
    {
        eTag = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return eTag.Length is >= 3 and <= 258
               && eTag[0] == '"'
               && eTag[^1] == '"'
               && !eTag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
               && !eTag.Contains(',')
               && !eTag.Any(char.IsControl);
    }

    private static IResult MutationReceipt(
        HttpResponse response,
        int statusCode,
        RemoteProfileSummary profile)
    {
        ApplyRevisionHeaders(response, profile);
        response.Headers.CacheControl = "no-store";
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ProfileSyncMutationReceipt
            {
                Profile = profile
            },
            JsonOptions);
        if (payload.LongLength > ProfileSyncContract.MaximumReceiptBytes)
        {
            throw new InvalidOperationException(
                "The mutation receipt exceeds the protocol size limit.");
        }

        return Results.Json(
            new ProfileSyncMutationReceipt
            {
                Profile = profile
            },
            JsonOptions,
            statusCode: statusCode);
    }

    private static IResult WorkspaceProfileMutationReceipt(
        HttpResponse response,
        int statusCode,
        WorkspaceProfileRoamingSummary profile)
    {
        ApplyWorkspaceProfileHeaders(response, profile);
        ApplyPrivateNoStore(response);
        return Results.Json(
            new WorkspaceProfileRoamingMutationReceipt
            {
                Profile = profile
            },
            JsonOptions,
            statusCode: statusCode);
    }

    private static IResult ConversationMutationReceipt(
        HttpResponse response,
        int statusCode,
        AiConversationSummary conversation)
    {
        ApplyConversationHeaders(response, conversation);
        ApplyPrivateNoStore(response);
        return Results.Json(
            new AiConversationMutationReceipt
            {
                Conversation = conversation
            },
            JsonOptions,
            statusCode: statusCode);
    }

    private static IResult BoundedConversationJson<T>(
        HttpResponse response,
        T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            value,
            JsonOptions);
        if (payload.Length
            > HypershellCloudContract.MaximumConversationPageBytes)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                "conversation_page_limit",
                "The requested conversation page exceeds the protocol limit. Request fewer items.");
        }

        ApplyPrivateNoStore(response);
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private static IResult BoundedPrivateJson<T>(
        HttpResponse response,
        T value,
        int maximumBytes,
        string errorCode,
        string errorMessage)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            value,
            JsonOptions);
        if (payload.Length > maximumBytes)
        {
            return Error(
                StatusCodes.Status507InsufficientStorage,
                errorCode,
                errorMessage);
        }

        ApplyPrivateNoStore(response);
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private static void ApplyWorkspaceProfileHeaders(
        HttpResponse response,
        WorkspaceProfileRoamingSummary profile)
    {
        response.Headers.ETag = profile.ETag;
        response.Headers[ProfileSyncContract.ServerRevisionHeader] =
            profile.ServerRevision.ToString(CultureInfo.InvariantCulture);
    }

    private static void ApplyConversationHeaders(
        HttpResponse response,
        AiConversationSummary conversation)
    {
        response.Headers.ETag = conversation.ETag;
        response.Headers[ProfileSyncContract.ServerRevisionHeader] =
            conversation.Revision.ToString(CultureInfo.InvariantCulture);
    }

    private static void ApplyRevisionHeaders(
        HttpResponse response,
        RemoteProfileSummary profile)
    {
        response.Headers.ETag = profile.ETag;
        response.Headers[ProfileSyncContract.ServerRevisionHeader] =
            profile.ServerRevision.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsProfileMediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(
            parsed.MediaType,
            ProfileSyncContract.ProfileMediaType,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonMediaType(string? contentType) =>
        IsMediaType(contentType, "application/json");

    private static bool IsMediaType(
        string? contentType,
        string expectedMediaType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(
            parsed.MediaType,
            expectedMediaType,
            StringComparison.OrdinalIgnoreCase);

    private static async Task<T> ReadJsonAsync<T>(
        HttpRequest request,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var payload = await ReadBytesAsync(
            request,
            maximumBytes,
            cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(payload.Span, JsonOptions)
                   ?? throw new JsonException("The request body was empty.");
        }
        catch (JsonException exception)
        {
            throw new RequestPayloadException(
                StatusCodes.Status400BadRequest,
                $"The JSON request is invalid: {exception.Message}");
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBytesAsync(
        HttpRequest request,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > 0
            && request.ContentLength > maximumBytes)
        {
            throw new RequestPayloadException(
                StatusCodes.Status413PayloadTooLarge,
                $"The request exceeds the {maximumBytes:N0}-byte limit.");
        }

        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(
                buffer,
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new RequestPayloadException(
                    StatusCodes.Status413PayloadTooLarge,
                    $"The request exceeds the {maximumBytes:N0}-byte limit.");
            }

            destination.Write(buffer, 0, read);
        }

        if (destination.Length == 0)
        {
            throw new RequestPayloadException(
                StatusCodes.Status400BadRequest,
                "The request body is empty.");
        }

        return destination.ToArray();
    }

    private static IResult Error(
        int statusCode,
        string code,
        string message) =>
        Results.Json(
            new
            {
                error = code,
                message
            },
            JsonOptions,
            statusCode: statusCode);

    private static string GetRemotePartition(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private sealed record AuthenticationOutcome(
        ProfileSessionPrincipal? Principal,
        string AccessToken,
        IResult? Error);

    private sealed record DeviceTokenReadOutcome(
        DeviceTokenRequest? Request,
        IResult? Error);

    private sealed record RemoteHostReadOutcome(
        RemoteHostMutationRequest? Request,
        IResult? Error);

    private sealed record SshCredentialReadOutcome(
        SshCredentialMutationRequest? Request,
        IResult? Error);

    private sealed class RequestPayloadException : Exception
    {
        public RequestPayloadException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }
}
