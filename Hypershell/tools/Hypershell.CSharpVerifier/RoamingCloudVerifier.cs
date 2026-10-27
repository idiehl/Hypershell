using Hypershell.ProfileServer;
using Hypershell.Profiles;
using Hypershell.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed record RoamingCloudVerificationState(
    string ProfileId,
    string ConversationId,
    string PackageSha256);

internal static class RoamingCloudVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(null, allowIntegerValues: false)
        }
    };

    public static async Task<RoamingCloudVerificationState> VerifyAsync(
        WebApplication app,
        string endpoint,
        string aliceToken,
        string bobToken,
        string verifierRoot)
    {
        await VerifyMigrationsAsync(app);
        var package = await VerifyPackageVaultAsync(
            endpoint,
            aliceToken,
            bobToken,
            verifierRoot);
        var profileId = await VerifyWorkspaceProfileAsync(
            endpoint,
            aliceToken,
            bobToken,
            package);
        var conversationId = await VerifyConversationAsync(
            endpoint,
            aliceToken,
            bobToken,
            profileId);
        await DesktopRoamingVerifier.VerifyAsync(
            endpoint,
            aliceToken,
            verifierRoot);
        return new RoamingCloudVerificationState(
            profileId,
            conversationId,
            package.ContentSha256);
    }

    public static async Task VerifyPersistenceAsync(
        string endpoint,
        string accessToken,
        RoamingCloudVerificationState state)
    {
        using var client = CreateClient(endpoint, accessToken);
        var profile = await client.GetFromJsonAsync<
            WorkspaceProfileRoamingResource>(
            HypershellCloudContract.RelativeWorkspaceProfilesPath
            + "/"
            + state.ProfileId,
            JsonOptions);
        var conversation = await client.GetFromJsonAsync<AiConversationPage>(
            HypershellCloudContract.RelativeConversationsPath
            + "/"
            + state.ConversationId
            + "?afterSequence=200&pageSize=100",
            JsonOptions);
        var packages = await client.GetFromJsonAsync<
            HyperlibVaultPackageList>(
            HypershellCloudContract.RelativePackagesPath,
            JsonOptions);
        Require(
            profile?.Document?.ProfileId == state.ProfileId
            && conversation is
            {
                Conversation.LastSequence: 235,
                Items.Count: 35
            }
            && packages?.Packages.Any(package =>
                package.ContentSha256 == state.PackageSha256) == true,
            "Roaming resources did not survive a server restart.");
    }

    private static async Task VerifyMigrationsAsync(WebApplication app)
    {
        var metadata = app.Services.GetRequiredService<CloudMetadataStore>();
        await using var connection = metadata.OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT version, name FROM schema_migrations ORDER BY version;";
        var migrations = new List<(long Version, string Name)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        Require(
            migrations.Count == 3
            && migrations[0].Version == 1
            && migrations[1]
               == (2L, "roaming-conversations-and-package-vault")
            && migrations[2]
               == (3L, "ssh-host-registry-and-envelope-vault"),
            "Cloud metadata migrations were not recorded deterministically.");
    }

    private static async Task<HyperlibVaultPackageSummary>
        VerifyPackageVaultAsync(
            string endpoint,
            string aliceToken,
            string bobToken,
            string verifierRoot)
    {
        var source = FindRepositoryPath(
            "samples",
            "extensions",
            "powershell-file-essentials");
        var work = Path.Combine(verifierRoot, "roaming-package");
        Directory.CreateDirectory(work);
        var archivePath = Path.Combine(work, "sample.hyperlib");
        var packed = new HyperlibPackageService(
                Path.Combine(work, "extensions"),
                Path.Combine(work, "staging"),
                Path.Combine(work, "recovery"))
            .Pack(source, archivePath);
        var archive = await File.ReadAllBytesAsync(archivePath);
        using var alice = CreateClient(endpoint, aliceToken);

        using (var wrongHash = await PutPackageAsync(
                   alice,
                   packed.ExtensionId,
                   packed.Version,
                   new string('0', 64),
                   archive))
        {
            Require(
                wrongHash.StatusCode == HttpStatusCode.BadRequest,
                "The package vault accepted a false content hash.");
        }

        HyperlibVaultPackageSummary stored;
        using (var created = await PutPackageAsync(
                   alice,
                   packed.ExtensionId,
                   packed.Version,
                   packed.PackageSha256,
                   archive))
        {
            var receipt = await created.Content.ReadFromJsonAsync<
                              HyperlibVaultUploadReceipt>(JsonOptions)
                          ?? throw new InvalidOperationException(
                              "The package vault returned no receipt.");
            stored = receipt.Package;
            Require(
                created.StatusCode == HttpStatusCode.Created
                && stored.ContentSha256 == packed.PackageSha256
                && stored.ByteLength == archive.LongLength
                && !stored.IsOfficial,
                "The package vault returned incorrect identity metadata.");
        }

        using (var repeated = await PutPackageAsync(
                   alice,
                   packed.ExtensionId,
                   packed.Version,
                   packed.PackageSha256,
                   archive))
        {
            Require(
                repeated.StatusCode == HttpStatusCode.OK,
                "An identical package upload was not idempotent.");
        }

        var inventory = await alice.GetFromJsonAsync<
            HyperlibVaultPackageList>(
            HypershellCloudContract.RelativePackagesPath,
            JsonOptions);
        var path = HypershellCloudContract.RelativePackagesPath
                   + "/"
                   + packed.PackageSha256;
        using var download = await alice.GetAsync(path);
        var downloaded = await download.Content.ReadAsByteArrayAsync();
        Require(
            inventory?.Packages.Single() == stored
            && download.IsSuccessStatusCode
            && download.Content.Headers.ContentType?.MediaType
               == HypershellCloudContract.HyperlibPackageMediaType
            && downloaded.AsSpan().SequenceEqual(archive),
            "The package vault did not list and return the exact inert archive.");

        using var bob = CreateClient(endpoint, bobToken);
        var bobInventory = await bob.GetFromJsonAsync<
            HyperlibVaultPackageList>(
            HypershellCloudContract.RelativePackagesPath,
            JsonOptions);
        using var bobDownload = await bob.GetAsync(path);
        Require(
            bobInventory?.Packages.Count == 0
            && bobDownload.StatusCode == HttpStatusCode.NotFound,
            "Personal package metadata or bytes crossed account boundaries.");

        using var traversal = await PutPackageAsync(
            alice,
            "community.traversal-test",
            "1.0.0",
            new string('1', 64),
            CreateTraversalArchive());
        Require(
            traversal.StatusCode == HttpStatusCode.BadRequest,
            "The package vault accepted an archive traversal entry.");
        return stored;
    }

    private static async Task<string> VerifyWorkspaceProfileAsync(
        string endpoint,
        string aliceToken,
        string bobToken,
        HyperlibVaultPackageSummary package)
    {
        const string profileId = "roaming-verifier";
        var document = CreateWorkspaceProfile(profileId, package);
        var path = HypershellCloudContract.RelativeWorkspaceProfilesPath
                   + "/"
                   + profileId;
        using var alice = CreateClient(endpoint, aliceToken);
        WorkspaceProfileRoamingMutationReceipt receipt;
        using (var response = await SendJsonAsync(
                   alice,
                   path,
                   document,
                   expectedETag: null))
        {
            receipt = await response.Content.ReadFromJsonAsync<
                          WorkspaceProfileRoamingMutationReceipt>(JsonOptions)
                      ?? throw new InvalidOperationException(
                          "The roaming-profile endpoint returned no receipt.");
            Require(
                response.StatusCode == HttpStatusCode.Created
                && receipt.Profile.ServerRevision == 1,
                "The roaming workspace profile was not conditionally created.");
        }

        var fetched = await alice.GetFromJsonAsync<
            WorkspaceProfileRoamingResource>(path, JsonOptions);
        Require(
            fetched?.Document?.Projects.Single().WorkspaceLocationUri
            == "ssh://host-verifier/srv/hypershell/project"
            && fetched.Document.Packages.Single().ContentSha256
               == package.ContentSha256,
            "Stable remote-project or package identity was lost while roaming.");

        using (var stale = await SendJsonAsync(
                   alice,
                   path,
                   document with
                   {
                       ClientRevision = 2,
                       ClientUpdatedAtUtc = DateTimeOffset.UtcNow
                   },
                   "\"stale\""))
        {
            Require(
                stale.StatusCode == HttpStatusCode.PreconditionFailed,
                "A stale roaming-profile ETag was accepted.");
        }

        using (var invalid = await SendJsonAsync(
                   alice,
                   path,
                   document with
                   {
                       ClientRevision = 2,
                       ClientUpdatedAtUtc = DateTimeOffset.UtcNow,
                       Projects =
                       [
                           new WorkspaceProjectReference
                           {
                               Id = "local-project",
                               DisplayName = "Leaked local path",
                               WorkspaceLocationUri =
                                   "local:///X:/workspace/project"
                           }
                       ]
                   },
                   receipt.Profile.ETag))
        {
            Require(
                invalid.StatusCode == HttpStatusCode.BadRequest,
                "A device-local Windows path entered roaming state.");
        }

        using var bob = CreateClient(endpoint, bobToken);
        var bobList = await bob.GetFromJsonAsync<WorkspaceProfileRoamingList>(
            HypershellCloudContract.RelativeWorkspaceProfilesPath,
            JsonOptions);
        Require(
            bobList?.Profiles.Count == 0,
            "Roaming workspace metadata crossed account boundaries.");
        return profileId;
    }

    private static async Task<string> VerifyConversationAsync(
        string endpoint,
        string aliceToken,
        string bobToken,
        string profileId)
    {
        const string conversationId = "conversation-verifier-2026";
        const string secret = "sk_1234567890abcdefghijklmnop";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var items = Enumerable.Range(1, 235)
            .Select(index => new AiConversationItemInput
            {
                Role = index % 2 == 0
                    ? AiConversationRole.Assistant
                    : AiConversationRole.User,
                Kind = AiConversationItemKind.Message,
                Text = index == 1
                    ? $"redact {secret}"
                    : $"Conversation item {index}",
                CreatedAtUtc = timestamp.AddSeconds(index)
            })
            .ToList();
        var path = HypershellCloudContract.RelativeConversationsPath
                   + "/"
                   + conversationId;
        using var alice = CreateClient(endpoint, aliceToken);
        string? eTag = null;
        for (var offset = 0; offset < items.Count; offset += 100)
        {
            using var response = await SendJsonAsync(
                alice,
                path,
                new AiConversationMutationRequest
                {
                    ProfileId = profileId,
                    Title = "Long roaming verifier conversation",
                    CompactedContext = offset == 0
                        ? "Useful compacted state."
                        : string.Empty,
                    Items = items.Skip(offset).Take(100).ToList()
                },
                eTag);
            var receipt = await response.Content.ReadFromJsonAsync<
                              AiConversationMutationReceipt>(JsonOptions)
                          ?? throw new InvalidOperationException(
                              "The conversation endpoint returned no receipt.");
            Require(
                response.StatusCode == (offset == 0
                    ? HttpStatusCode.Created
                    : HttpStatusCode.OK),
                "A bounded conversation append failed.");
            eTag = receipt.Conversation.ETag;
        }

        var collected = new List<AiConversationItem>();
        long after = 0;
        while (true)
        {
            var page = await alice.GetFromJsonAsync<AiConversationPage>(
                path + $"?afterSequence={after}&pageSize=80",
                JsonOptions)
                       ?? throw new InvalidOperationException(
                           "The conversation endpoint returned no page.");
            collected.AddRange(page.Items);
            if (page.NextAfterSequence is not long next)
            {
                break;
            }

            after = next;
        }

        Require(
            collected.Count == 235
            && collected.Select(item => item.Sequence).SequenceEqual(
                Enumerable.Range(1, 235).Select(value => (long)value))
            && !collected[0].Text.Contains(secret, StringComparison.Ordinal)
            && collected[0].Text.Contains(
                "[REDACTED_TOKEN]",
                StringComparison.Ordinal),
            "Pagination, ordering, or redaction failed beyond the former 96-item cap.");

        using (var screenshot = await SendJsonAsync(
                   alice,
                   path,
                   new AiConversationMutationRequest
                   {
                       ProfileId = profileId,
                       Title = "Long roaming verifier conversation",
                       CompactedContext =
                           "{\"image_url\":\"data:image/png;base64,AAAA\"}"
                   },
                   eTag))
        {
            Require(
                screenshot.StatusCode == HttpStatusCode.BadRequest,
                "Raw screenshot data entered conversation compaction.");
        }

        using (var bulkOutput = await SendJsonAsync(
                   alice,
                   path,
                   new AiConversationMutationRequest
                   {
                       ProfileId = profileId,
                       Title = "Long roaming verifier conversation",
                       Items =
                       [
                           new AiConversationItemInput
                           {
                               Role = AiConversationRole.Host,
                               Kind = AiConversationItemKind.ActionSummary,
                               Text = new string('x', 17 * 1024),
                               ActionName = "terminal",
                               CreatedAtUtc = DateTimeOffset.UtcNow
                           }
                       ]
                   },
                   eTag))
        {
            Require(
                bulkOutput.StatusCode == HttpStatusCode.BadRequest,
                "Bulk terminal output entered conversation history.");
        }

        using var bob = CreateClient(endpoint, bobToken);
        using var bobRead = await bob.GetAsync(path);
        using var bobCollision = await SendJsonAsync(
            bob,
            path,
            new AiConversationMutationRequest
            {
                ProfileId = profileId,
                Title = "Collision",
                Items =
                [
                    new AiConversationItemInput
                    {
                        Role = AiConversationRole.User,
                        Kind = AiConversationItemKind.Message,
                        Text = "isolated",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            },
            expectedETag: null);
        Require(
            bobRead.StatusCode == HttpStatusCode.NotFound
            && bobCollision.StatusCode == HttpStatusCode.PreconditionFailed,
            "Conversation data crossed accounts or a global ID collision mutated it.");
        return conversationId;
    }

    private static WorkspaceProfileRoamingDocument CreateWorkspaceProfile(
        string profileId,
        HyperlibVaultPackageSummary package) => new()
    {
        ProfileId = profileId,
        DisplayName = "Roaming verifier",
        ClientRevision = 1,
        ClientUpdatedAtUtc = DateTimeOffset.UtcNow,
        SelectedAiContextId = "review-context",
        AiContexts =
        [
            new RoamingAiContextDefinition
            {
                Id = "review-context",
                Name = "Review context",
                Model = "gpt-5.6-sol",
                ReasoningEffort = "high",
                TextVerbosity = "medium",
                Read = new RoamingAiPathPermission
                {
                    WorkspaceUris =
                    [
                        "ssh://host-verifier/srv/hypershell/project"
                    ]
                },
                AskBeforeChanges = true
            }
        ],
        Projects =
        [
            new WorkspaceProjectReference
            {
                Id = "remote-project",
                DisplayName = "Remote verifier project",
                WorkspaceLocationUri =
                    "ssh://host-verifier/srv/hypershell/project",
                GitOriginUri =
                    "ssh://host-verifier/srv/hypershell/git/project.git",
                DefaultBranch = "main"
            }
        ],
        Packages =
        [
            new HyperlibPackageSelection
            {
                PackageId = package.PackageId,
                Version = package.Version,
                ContentSha256 = package.ContentSha256,
                IsEnabled = true
            }
        ]
    };

    private static HttpClient CreateClient(string endpoint, string token)
    {
        var client = new HttpClient { BaseAddress = new Uri(endpoint) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        string path,
        T payload,
        string? expectedETag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation(
            expectedETag is null ? "If-None-Match" : "If-Match",
            expectedETag ?? "*");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutPackageAsync(
        HttpClient client,
        string packageId,
        string version,
        string hash,
        byte[] archive)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            HypershellCloudContract.RelativePackagesPath
            + "/"
            + packageId
            + "/"
            + version
            + "/"
            + hash)
        {
            Content = new ByteArrayContent(archive)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            HypershellCloudContract.HyperlibPackageMediaType);
        return await client.SendAsync(request);
    }

    private static byte[] CreateTraversalArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            var entry = archive.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("blocked");
        }

        return output.ToArray();
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = segments.Aggregate(
                directory.FullName,
                Path.Combine);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "The verifier could not locate its sample Hyperlib package.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
