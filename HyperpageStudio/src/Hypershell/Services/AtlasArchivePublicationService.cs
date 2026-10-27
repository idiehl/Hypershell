using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed class AtlasArchivePublicationPreparation
{
    public required string PreparationId { get; init; }
    public required string AtlasCheckoutRoot { get; init; }
    public required string StagingWorktreeRoot { get; init; }
    public required string BaseCommit { get; init; }
    public required string TargetBranch { get; init; }
    public required string ReviewHash { get; init; }
    public required string ReviewText { get; init; }
    public required string ValidationOutput { get; init; }
    public required AtlasArchivePageManifest Manifest { get; init; }
    public required IReadOnlyList<string> ChangedPaths { get; init; }
    public bool RequiresReviewedCode => Manifest.Classifications.Any(value =>
        value != VisualPageCodeClassification.Declarative);
}

public sealed class AtlasArchivePublicationService
{
    public const int MaximumPortableProjectFiles = 4096;
    public const long MaximumPortableProjectBytes = 64L * 1024 * 1024;
    public const int MaximumReviewCharacters = 2 * 1024 * 1024;

    private static readonly Regex SlugPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BranchPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._/-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CommitPattern = new(
        "^[a-f0-9]{40,64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PrivateMaterialPattern = new(
        "BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{20,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".razor", ".cs", ".css", ".js", ".json", ".xml",
        ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif",
        ".woff", ".woff2", ".ttf", ".ico", ".txt", ".md"
    };
    private static readonly HashSet<string> ProhibitedNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".env", "appsettings.Development.json", "appsettings.Production.json",
        "secrets.json", "launchSettings.json"
    };

    private readonly BoundedProcessRunner _processRunner;
    private readonly VisualPageProjectService _projectService;
    private readonly VisualPageGenerator _generator;
    private readonly VisualPageSchemaValidator _validator;
    private readonly string _stagingRoot;

    public AtlasArchivePublicationService(
        BoundedProcessRunner processRunner,
        VisualPageProjectService projectService,
        VisualPageGenerator generator,
        VisualPageSchemaValidator validator,
        string stagingRoot)
    {
        _processRunner = processRunner;
        _projectService = projectService;
        _generator = generator;
        _validator = validator;
        _stagingRoot = Path.GetFullPath(stagingRoot);
    }

    public async Task<AtlasArchivePublicationPreparation> PrepareAsync(
        VisualPageProjectSession session,
        string atlasCheckoutRoot,
        string slug,
        string? targetBranch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Project.Foundation.MarkupLanguage ==
            HyperpageMarkupLanguage.Html)
        {
            throw new AtlasArchivePublicationException(
                "Atlas Archive publication currently accepts Razor Pages projects. Export the generated static files under src/Host/wwwroot to deploy this HTML project to its selected web server.");
        }
        var safeSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugPattern.IsMatch(safeSlug))
        {
            throw new AtlasArchivePublicationException(
                "Archive slugs use lowercase letters, numbers, and single hyphens only.");
        }

        var atlasRoot = Path.GetFullPath(atlasCheckoutRoot);
        ValidateAtlasCheckout(atlasRoot);
        EnsureDirectoryIsOutside(_stagingRoot, atlasRoot);
        await ValidateSavedPortableProjectAsync(session, cancellationToken);

        var git = ResolveExecutable("git.exe");
        var dotnet = ResolveExecutable("dotnet.exe");
        var status = await RunRequiredAsync(
            git,
            ["status", "--porcelain=v1", "--untracked-files=all"],
            atlasRoot,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(status.StandardOutput))
        {
            throw new AtlasArchivePublicationException(
                "Atlas publication requires a clean source checkout. Existing work was left untouched.");
        }

        var baseCommit = (await RunRequiredAsync(
            git,
            ["rev-parse", "HEAD"],
            atlasRoot,
            TimeSpan.FromSeconds(20),
            cancellationToken)).StandardOutput.Trim();
        if (!CommitPattern.IsMatch(baseCommit))
        {
            throw new AtlasArchivePublicationException(
                "Atlas did not return a valid exact base commit.");
        }

        var branch = string.IsNullOrWhiteSpace(targetBranch)
            ? (await RunRequiredAsync(
                git,
                ["branch", "--show-current"],
                atlasRoot,
                TimeSpan.FromSeconds(20),
                cancellationToken)).StandardOutput.Trim()
            : targetBranch.Trim();
        ValidateBranch(branch);
        var origin = (await RunRequiredAsync(
            git,
            ["remote", "get-url", "origin"],
            atlasRoot,
            TimeSpan.FromSeconds(20),
            cancellationToken)).StandardOutput.Trim();
        GitRemoteCredentialPolicy.EnsureCredentialFree(origin);

        var preparationId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_stagingRoot);
        var stagingWorktree = ResolveChild(
            _stagingRoot,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{safeSlug}-{preparationId[..8]}");
        if (Directory.Exists(stagingWorktree) || File.Exists(stagingWorktree))
        {
            throw new AtlasArchivePublicationException(
                "The unique Atlas publication staging path already exists.");
        }

        await RunRequiredAsync(
            git,
            ["worktree", "add", "--detach", stagingWorktree, baseCommit],
            atlasRoot,
            TimeSpan.FromMinutes(2),
            cancellationToken);

        try
        {
            var classification = await CollectClassificationsAsync(
                session,
                cancellationToken);
            var projectFolder = $"vp-{Guid.Parse(session.Project.ProjectId):N}";
            var projectTargetRelative = $"src/Atlas.Archive/Projects/{projectFolder}";
            var manifestRelative = $"src/Atlas.Web/Archive/manifests/{safeSlug}.json";
            var projectTarget = ResolveChild(stagingWorktree, projectTargetRelative);
            ReplacePortableProject(session, projectTarget);

            var validationOutput = new StringBuilder();
            validationOutput.AppendLine((await RunRequiredAsync(
                dotnet,
                ["restore", "src/Atlas.Web/Atlas.Web.csproj", "--nologo"],
                stagingWorktree,
                TimeSpan.FromMinutes(5),
                cancellationToken)).CombinedOutput);
            validationOutput.AppendLine((await RunRequiredAsync(
                dotnet,
                ["format", $"{projectTargetRelative}/Pages.csproj", "--no-restore", "--verbosity", "minimal"],
                stagingWorktree,
                TimeSpan.FromMinutes(3),
                cancellationToken)).CombinedOutput);

            var artifacts = HashPortableProject(projectTarget, projectTargetRelative);
            var manifest = new AtlasArchivePageManifest
            {
                Slug = safeSlug,
                Route = $"/Archive/{safeSlug}",
                Title = string.IsNullOrWhiteSpace(session.Page.Title)
                    ? session.Page.Name
                    : session.Page.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(session.Page.Description)
                    ? $"{session.Page.Name}, published from Hypershell Hyperpage Studio."
                    : session.Page.Description.Trim(),
                Visibility = "Public",
                SourceProjectId = session.Project.ProjectId,
                SourcePageId = session.Page.PageId,
                SourceRevision = session.Page.Revision,
                ComponentType = $"{session.Project.RootNamespace}.Generated.{session.Page.ComponentName}",
                AssemblyName = session.Project.RootNamespace,
                Classifications = classification.OrderBy(value => value).ToList(),
                Artifacts = artifacts.ToList()
            };
            await WriteAtomicTextAsync(
                ResolveChild(stagingWorktree, manifestRelative),
                JsonSerializer.Serialize(manifest, VisualPageJson.Options),
                cancellationToken);

            validationOutput.AppendLine((await RunRequiredAsync(
                dotnet,
                ["format", "src/Atlas.Web/Atlas.Web.csproj", "--verify-no-changes", "--no-restore", "--verbosity", "minimal"],
                stagingWorktree,
                TimeSpan.FromMinutes(3),
                cancellationToken)).CombinedOutput);
            validationOutput.AppendLine((await RunRequiredAsync(
                dotnet,
                ["build", "src/Atlas.Web/Atlas.Web.csproj", "-c", "Release", "--no-restore", "--nologo"],
                stagingWorktree,
                TimeSpan.FromMinutes(5),
                cancellationToken)).CombinedOutput);
            validationOutput.AppendLine((await RunRequiredAsync(
                dotnet,
                ["test", "tests/Atlas.Web.Tests/Atlas.Web.Tests.csproj", "-c", "Release", "--nologo"],
                stagingWorktree,
                TimeSpan.FromMinutes(5),
                cancellationToken)).CombinedOutput);

            var allowedPrefix = projectTargetRelative.Replace('\\', '/') + "/";
            var workingStatus = await RunRequiredAsync(
                git,
                ["status", "--porcelain=v1", "--untracked-files=all"],
                stagingWorktree,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            EnsureOnlyPublicationPaths(
                workingStatus.StandardOutput,
                allowedPrefix,
                manifestRelative);

            await RunRequiredAsync(
                git,
                ["add", "-A", "--", projectTargetRelative, manifestRelative],
                stagingWorktree,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            await RunRequiredAsync(
                git,
                ["diff", "--cached", "--check"],
                stagingWorktree,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            var changedPaths = (await RunRequiredAsync(
                git,
                ["diff", "--cached", "--name-only"],
                stagingWorktree,
                TimeSpan.FromSeconds(30),
                cancellationToken)).StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (changedPaths.Length == 0)
            {
                throw new AtlasArchivePublicationException(
                    "The prepared Archive publication contains no source changes.");
            }
            if (changedPaths.Any(path => !IsAllowedPublicationPath(
                    path,
                    allowedPrefix,
                    manifestRelative)))
            {
                throw new AtlasArchivePublicationException(
                    "The prepared Git index contains a path outside the Archive publication boundary.");
            }

            var patch = await RunRequiredAsync(
                git,
                ["diff", "--cached", "--full-index", "--no-ext-diff"],
                stagingWorktree,
                TimeSpan.FromMinutes(1),
                cancellationToken,
                MaximumReviewCharacters);
            var stat = await RunRequiredAsync(
                git,
                ["diff", "--cached", "--stat"],
                stagingWorktree,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            var reviewHash = VisualPageHashing.Sha256Text(patch.StandardOutput);
            var reviewText = $$"""
                Base commit: {{baseCommit}}
                Target branch: {{branch}}
                Page revision: {{manifest.SourceRevision}}
                Review hash: {{reviewHash}}
                Classifications: {{string.Join(", ", manifest.Classifications)}}

                {{stat.StandardOutput.TrimEnd()}}

                {{patch.StandardOutput.TrimEnd()}}
                """;
            return new AtlasArchivePublicationPreparation
            {
                PreparationId = preparationId,
                AtlasCheckoutRoot = atlasRoot,
                StagingWorktreeRoot = stagingWorktree,
                BaseCommit = baseCommit,
                TargetBranch = branch,
                ReviewHash = reviewHash,
                ReviewText = reviewText,
                ValidationOutput = validationOutput.ToString().Trim(),
                Manifest = manifest,
                ChangedPaths = changedPaths
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw exception is AtlasArchivePublicationException
                ? new AtlasArchivePublicationException(
                    $"{exception.Message} The isolated worktree was retained for inspection at '{stagingWorktree}'.",
                    exception)
                : new AtlasArchivePublicationException(
                    $"Atlas publication preparation failed: {exception.Message} " +
                    $"The isolated worktree was retained for inspection at '{stagingWorktree}'.",
                    exception);
        }
    }

    public async Task<AtlasArchivePublishReceipt> PublishExactCommitAsync(
        VisualPageProjectSession session,
        AtlasArchivePublicationPreparation preparation,
        bool approvedReviewedCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(preparation);
        if (!string.Equals(
                session.Page.Revision,
                preparation.Manifest.SourceRevision,
                StringComparison.Ordinal))
        {
            throw new AtlasArchivePublicationException(
                "The Hyperpage document changed after review. Prepare and review a fresh publication.");
        }
        if (preparation.RequiresReviewedCode && !approvedReviewedCode)
        {
            throw new AtlasArchivePublicationException(
                "This page contains reviewed-code classifications. Explicit source-diff approval is required.");
        }
        if (!Directory.Exists(preparation.StagingWorktreeRoot))
        {
            throw new AtlasArchivePublicationException(
                "The reviewed publication worktree no longer exists.");
        }

        var git = ResolveExecutable("git.exe");
        var currentPatch = await RunRequiredAsync(
            git,
            ["diff", "--cached", "--full-index", "--no-ext-diff"],
            preparation.StagingWorktreeRoot,
            TimeSpan.FromMinutes(1),
            cancellationToken,
            MaximumReviewCharacters);
        var currentReviewHash = VisualPageHashing.Sha256Text(currentPatch.StandardOutput);
        if (!string.Equals(
                currentReviewHash,
                preparation.ReviewHash,
                StringComparison.Ordinal))
        {
            throw new AtlasArchivePublicationException(
                "The staged Atlas source changed after review. No commit was created.");
        }

        await RunRequiredAsync(
            git,
            ["diff", "--cached", "--check"],
            preparation.StagingWorktreeRoot,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        var subject = $"Publish Archive/{preparation.Manifest.Slug} from Hyperpage Studio";
        await RunRequiredAsync(
            git,
            [
                "commit",
                "-m", subject,
                "-m", $"Visual-Page-Project: {preparation.Manifest.SourceProjectId}",
                "-m", $"Visual-Page-Page: {preparation.Manifest.SourcePageId}",
                "-m", $"Visual-Page-Revision: {preparation.Manifest.SourceRevision}",
                "-m", $"Visual-Page-Review: {preparation.ReviewHash}"
            ],
            preparation.StagingWorktreeRoot,
            TimeSpan.FromMinutes(1),
            cancellationToken);
        var commit = (await RunRequiredAsync(
            git,
            ["rev-parse", "HEAD"],
            preparation.StagingWorktreeRoot,
            TimeSpan.FromSeconds(20),
            cancellationToken)).StandardOutput.Trim();
        if (!CommitPattern.IsMatch(commit))
        {
            throw new AtlasArchivePublicationException(
                "Git did not return the exact publication commit.");
        }

        await RunRequiredAsync(
            git,
            ["push", "origin", $"{commit}:refs/heads/{preparation.TargetBranch}"],
            preparation.StagingWorktreeRoot,
            TimeSpan.FromMinutes(3),
            cancellationToken);

        var receipt = new AtlasArchivePublishReceipt
        {
            Slug = preparation.Manifest.Slug,
            SourceProjectId = preparation.Manifest.SourceProjectId,
            SourcePageId = preparation.Manifest.SourcePageId,
            SourceRevision = preparation.Manifest.SourceRevision,
            SourceCommit = commit,
            TargetBranch = preparation.TargetBranch,
            ReviewHash = preparation.ReviewHash,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Artifacts = preparation.Manifest.Artifacts
                .Select(artifact => VisualPageJson.Clone(artifact))
                .ToList()
        };
        var receiptPath = ResolveChild(
            session.RootPath,
            $".visual-page/publish-receipts/{receipt.ReceiptId}.json");
        try
        {
            await WriteAtomicTextAsync(
                receiptPath,
                JsonSerializer.Serialize(receipt, VisualPageJson.Options),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
            // The exact commit is already on the target ref. Preserve truthful success
            // semantics even if the optional local receipt cache is unavailable.
        }

        try
        {
            await RunRequiredAsync(
                git,
                ["worktree", "remove", preparation.StagingWorktreeRoot],
                preparation.AtlasCheckoutRoot,
                TimeSpan.FromMinutes(1),
                cancellationToken);
        }
        catch (AtlasArchivePublicationException)
        {
            // Publication already succeeded. A retained clean worktree is recoverable and
            // must not turn an exact pushed commit into a false failure.
        }
        return receipt;
    }

    private async Task ValidateSavedPortableProjectAsync(
        VisualPageProjectSession session,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(session.Project.ProjectId, out _) ||
            !Guid.TryParse(session.Page.PageId, out _))
        {
            throw new AtlasArchivePublicationException(
                "The Hyperpage project or page identity is invalid.");
        }

        var diskSession = await _projectService.OpenAsync(
            session.RootPath,
            session.Page.PageId,
            cancellationToken);
        if (!string.Equals(diskSession.Project.Revision, session.Project.Revision, StringComparison.Ordinal) ||
            !string.Equals(diskSession.Page.Revision, session.Page.Revision, StringComparison.Ordinal))
        {
            throw new AtlasArchivePublicationException(
                "Save the current project revision before preparing publication.");
        }

        foreach (var projectPage in session.Project.Pages)
        {
            var pageSession = await _projectService.OpenAsync(
                session.RootPath,
                projectPage.PageId,
                cancellationToken);
            var diagnostics = _validator.ValidatePage(pageSession.Page)
                .Where(value => value.Severity is
                    VisualPageDiagnosticSeverity.Warning or VisualPageDiagnosticSeverity.Error)
                .ToList();
            if (diagnostics.Count > 0)
            {
                throw new AtlasArchivePublicationException(
                    $"Accessibility/schema validation stopped publication: {diagnostics[0].Code} {diagnostics[0].Message}");
            }
            var drift = _projectService.InspectGeneratedDrift(pageSession);
            if (drift.Count > 0)
            {
                throw new AtlasArchivePublicationException(
                    $"Generated source drift must be reviewed before publication ({drift[0].RelativePath}).");
            }
        }

        foreach (var asset in session.Project.Assets)
        {
            var source = ResolveChild(session.RootPath, asset.RelativePath);
            if (!File.Exists(source) || string.IsNullOrWhiteSpace(asset.Sha256) ||
                !string.Equals(
                    VisualPageHashing.Sha256File(source),
                    asset.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AtlasArchivePublicationException(
                    $"Asset '{asset.RelativePath}' is missing or does not match its registered hash.");
            }
        }
    }

    private async Task<HashSet<VisualPageCodeClassification>> CollectClassificationsAsync(
        VisualPageProjectSession session,
        CancellationToken cancellationToken)
    {
        var values = new HashSet<VisualPageCodeClassification>
        {
            VisualPageCodeClassification.Declarative
        };
        foreach (var projectPage in session.Project.Pages)
        {
            var pageSession = await _projectService.OpenAsync(
                session.RootPath,
                projectPage.PageId,
                cancellationToken);
            values.UnionWith(_generator.Generate(
                pageSession.Project,
                pageSession.Page).Classifications);
            AddEditableSourceClassifications(values, pageSession);
        }

        var projectPath = ResolveChild(session.RootPath, session.Project.RazorProjectPath);
        using var reader = XmlReader.Create(projectPath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Descendants().Any(element =>
                element.Name.LocalName == "PackageReference"))
        {
            values.Add(VisualPageCodeClassification.PackageDependency);
        }
        if (document.Descendants().Any(element =>
                element.Name.LocalName == "ProjectReference"))
        {
            values.Add(VisualPageCodeClassification.CustomComponent);
        }
        return values;
    }

    private static void AddEditableSourceClassifications(
        ISet<VisualPageCodeClassification> values,
        VisualPageProjectSession session)
    {
        var basePath = $"src/Pages/Generated/{session.Page.ComponentName}";
        AddIfDifferent(
            values,
            VisualPageCodeClassification.CustomCSharp,
            ResolveChild(session.RootPath, $"{basePath}.razor.cs"),
            VisualPageProjectService.EditablePartial(session.Project, session.Page));
        AddIfDifferent(
            values,
            VisualPageCodeClassification.CustomCss,
            ResolveChild(session.RootPath, $"src/Pages/wwwroot/{session.Page.ComponentName}.custom.css"),
            VisualPageProjectService.EditableCss(session.Page));
        AddIfDifferent(
            values,
            VisualPageCodeClassification.CustomJavaScript,
            ResolveChild(session.RootPath, $"{basePath}.razor.js"),
            VisualPageProjectService.EditableJavaScript(session.Page));
    }

    private static void AddIfDifferent(
        ISet<VisualPageCodeClassification> values,
        VisualPageCodeClassification classification,
        string path,
        string baseline)
    {
        if (File.Exists(path) && !string.Equals(
                NormalizeText(File.ReadAllText(path)),
                NormalizeText(baseline),
                StringComparison.Ordinal))
        {
            values.Add(classification);
        }
    }

    private static void ReplacePortableProject(
        VisualPageProjectSession session,
        string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }
        Directory.CreateDirectory(targetRoot);
        var sourceRoot = ResolveChild(session.RootPath, "src/Pages");
        var fileCount = 0;
        long totalBytes = 0;
        foreach (var source in EnumeratePortableSourceFiles(sourceRoot))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (IsExcludedSourcePath(relative))
            {
                continue;
            }
            ValidatePortableSourceFile(source, relative);
            fileCount++;
            totalBytes += new FileInfo(source).Length;
            if (fileCount > MaximumPortableProjectFiles || totalBytes > MaximumPortableProjectBytes)
            {
                throw new AtlasArchivePublicationException(
                    "The portable Razor project exceeds the bounded publication size.");
            }
            var target = ResolveChild(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        foreach (var asset in session.Project.Assets)
        {
            var source = ResolveChild(session.RootPath, asset.RelativePath);
            var target = ResolveChild(
                targetRoot,
                $"wwwroot/visual-page-assets/{Guid.Parse(asset.AssetId):N}/{Path.GetFileName(source)}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static IReadOnlyList<VisualPageGeneratedFileHash> HashPortableProject(
        string targetRoot,
        string targetRelative)
    {
        return Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsExcludedSourcePath(Path.GetRelativePath(targetRoot, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new VisualPageGeneratedFileHash
            {
                RelativePath = $"{targetRelative}/{Path.GetRelativePath(targetRoot, path).Replace('\\', '/')}",
                Sha256 = VisualPageHashing.Sha256File(path)
            })
            .ToList();
    }

    private static IEnumerable<string> EnumeratePortableSourceFiles(string sourceRoot)
    {
        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            VisualPageProjectService.RejectReparsePoint(directory);
            foreach (var child in Directory.EnumerateDirectories(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(sourceRoot, child);
                if (IsExcludedSourcePath(relative))
                {
                    continue;
                }
                VisualPageProjectService.RejectReparsePoint(child);
                pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static void ValidatePortableSourceFile(string path, string relativePath)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            ProhibitedNames.Contains(Path.GetFileName(path)) ||
            !AllowedExtensions.Contains(Path.GetExtension(path)))
        {
            throw new AtlasArchivePublicationException(
                $"Portable source '{relativePath}' is not allowed in an Archive publication.");
        }

        if (new FileInfo(path).Length <= 2 * 1024 * 1024 &&
            IsTextExtension(Path.GetExtension(path)))
        {
            var content = File.ReadAllText(path);
            if (PrivateMaterialPattern.IsMatch(content))
            {
                throw new AtlasArchivePublicationException(
                    $"Potential private credential material was detected in '{relativePath}'.");
            }
        }
    }

    private static bool IsExcludedSourcePath(string relativePath)
    {
        var parts = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals("publish", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals(".visual-page", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTextExtension(string extension) => extension.ToLowerInvariant() is
        ".csproj" or ".razor" or ".cs" or ".css" or ".js" or ".json" or
        ".xml" or ".svg" or ".txt" or ".md";

    private static void ValidateAtlasCheckout(string root)
    {
        if (!Directory.Exists(root) ||
            !Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git")) ||
            !File.Exists(Path.Combine(root, "src", "Atlas.Archive", "Atlas.Archive.csproj")) ||
            !File.Exists(Path.Combine(root, "src", "Atlas.Web", "Atlas.Web.csproj")) ||
            !File.Exists(Path.Combine(root, "tests", "Atlas.Web.Tests", "Atlas.Web.Tests.csproj")))
        {
            throw new AtlasArchivePublicationException(
                "The configured path is not a complete Atlas Archive source checkout.");
        }
    }

    private static void ValidateBranch(string branch)
    {
        if (!BranchPattern.IsMatch(branch) || branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal) || branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            branch.EndsWith("/", StringComparison.Ordinal) || branch.Contains("//", StringComparison.Ordinal))
        {
            throw new AtlasArchivePublicationException(
                "The target Git branch name is invalid.");
        }
    }

    private static void EnsureOnlyPublicationPaths(
        string porcelainStatus,
        string allowedPrefix,
        string manifestRelative)
    {
        foreach (var line in porcelainStatus.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var path = line.Length > 3 ? line[3..].Trim().Trim('"') : string.Empty;
            if (path.Contains(" -> ", StringComparison.Ordinal))
            {
                path = path[(path.LastIndexOf(" -> ", StringComparison.Ordinal) + 4)..];
            }
            if (!IsAllowedPublicationPath(path.Replace('\\', '/'), allowedPrefix, manifestRelative))
            {
                throw new AtlasArchivePublicationException(
                    $"Publication validation changed an unrelated Atlas path: '{path}'.");
            }
        }
    }

    private static bool IsAllowedPublicationPath(
        string path,
        string allowedPrefix,
        string manifestRelative)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith(allowedPrefix, StringComparison.Ordinal) ||
               string.Equals(normalized, manifestRelative, StringComparison.Ordinal);
    }

    private static void EnsureDirectoryIsOutside(string candidate, string protectedRoot)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedProtected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedRoot));
        if (normalizedCandidate.Equals(normalizedProtected, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedProtected + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AtlasArchivePublicationException(
                "The publication staging cache must be outside the Atlas checkout.");
        }
    }

    private static string ResolveChild(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new AtlasArchivePublicationException(
                "A publication path must be relative to its bounded root.");
        }
        if (relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."
                            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new AtlasArchivePublicationException(
                "Publication paths cannot contain traversal segments, invalid names, or alternate data streams.");
        }
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(
            relativePath.Replace('/', Path.DirectorySeparatorChar),
            fullRoot);
        if (!candidate.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AtlasArchivePublicationException(
                "A publication path escaped its bounded root.");
        }
        VisualPageProjectService.EnsureNoReparseTraversal(fullRoot, candidate);
        return candidate;
    }

    private static string ResolveExecutable(string name)
    {
        var candidates = new List<string>();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory.Trim('"'), name));
        }
        if (name.Equals("git.exe", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git", "cmd", name));
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Git", "cmd", name));
        }
        else if (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", name));
        }

        return candidates.Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return string.Empty; }
            })
            .FirstOrDefault(File.Exists)
            ?? throw new AtlasArchivePublicationException(
                $"Required publication tool '{name}' was not found.");
    }

    private async Task<PublicationProcessResult> RunRequiredAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int maximumOutputCharacters = 4 * 1024 * 1024)
    {
        var effectiveArguments = Path.GetFileName(executable)
            .Equals("git.exe", StringComparison.OrdinalIgnoreCase)
            ? new[] { "-c", "core.longpaths=true" }.Concat(arguments).ToArray()
            : arguments;
        var result = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = executable,
                Arguments = effectiveArguments,
                WorkingDirectory = workingDirectory,
                Timeout = timeout,
                MaximumStandardOutputCharacters = maximumOutputCharacters,
                MaximumStandardErrorCharacters = Math.Min(maximumOutputCharacters, 512 * 1024),
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_NOLOGO"] = "1"
                }
            },
            cancellationToken);
        if (!result.Started || result.TimedOut || result.OutputLimitExceeded || result.ExitCode != 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                new[] { result.FailureMessage, result.StandardError, result.StandardOutput }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
            throw new AtlasArchivePublicationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Publication command '{Path.GetFileName(executable)}' failed."
                    : detail);
        }
        return new PublicationProcessResult(
            result.StandardOutput,
            result.StandardError);
    }

    private static async Task WriteAtomicTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                content.Replace("\r\n", "\n", StringComparison.Ordinal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string NormalizeText(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd();

    private sealed record PublicationProcessResult(
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
    }
}

public sealed class AtlasArchivePublicationException : Exception
{
    public AtlasArchivePublicationException(string message)
        : base(message)
    {
    }

    public AtlasArchivePublicationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
