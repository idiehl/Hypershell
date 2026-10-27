using System.IO;
using System.Text;
using System.Text.Json;
using Hypershell.Models;

namespace Hypershell.Services;

public sealed class VisualPageProjectSession
{
    public required string RootPath { get; init; }
    public required VisualPageProjectManifest Project { get; set; }
    public required VisualPageDocument Page { get; set; }
    public string ProjectManifestPath => Path.Combine(
        RootPath,
        VisualPageProjectService.ProjectManifestFileName);
}

public sealed class HyperpagePageCreationDefinition
{
    public string Name { get; init; } = "Page";
    public string Route { get; init; } = "/";
    public VisualPageTemplateDocument? Template { get; init; }
}

public sealed record HyperpagePageCreationRequest(
    string Name,
    string Route,
    HyperpagePageFileType FileType,
    VisualPageTemplateDocument? Template,
    string Description);

public sealed class HyperpageProjectCreationOptions
{
    public HyperpageProjectFoundation Foundation { get; init; } = new();
    public IReadOnlyList<HyperpagePageCreationDefinition> Pages { get; init; } =
        [new() { Name = "Home", Route = "/" }];
}

public sealed class VisualPageProjectService
{
    public static string DefaultProjectsDirectory =>
        ApplicationUpdateService.DetectSuiteComponents(AppContext.BaseDirectory)
            ? Path.Combine(new SettingsService(Environment.GetEnvironmentVariable(
                StandaloneStudioHost.BaseDataOverrideVariable)).ApplicationDataDirectory, "Projects")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Hyperpage Studio", "Projects");
    public const string ProjectManifestFileName = "visual-page.project.json";
    public const int MaximumProjectDocumentBytes = 16 * 1024 * 1024;
    public const long MaximumAssetBytes = 32L * 1024 * 1024;
    public const int MaximumRecoveryRevisions = 20;
    private readonly VisualPageSchemaValidator _validator;
    private readonly VisualPageGenerator _generator;
    private readonly VisualPageComponentCatalog _catalog;

    public VisualPageProjectService(
        VisualPageSchemaValidator validator,
        VisualPageGenerator generator,
        VisualPageComponentCatalog catalog)
    {
        _validator = validator;
        _generator = generator;
        _catalog = catalog;
    }

    public static string EnsureDefaultProjectsDirectory()
    {
        var directory = Path.GetFullPath(DefaultProjectsDirectory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public Task<VisualPageProjectSession> CreateInDirectoryAsync(
        string parentDirectory,
        string name,
        string? rootNamespace = null,
        CancellationToken cancellationToken = default)
    {
        var folderName = name.Trim();
        if (string.IsNullOrWhiteSpace(folderName)
            || folderName is "." or ".."
            || folderName.EndsWith('.')
            || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new VisualPageValidationException(
                "Choose a project name that is valid as a folder name, without path separators or a trailing period.");
        }

        var parent = Path.GetFullPath(parentDirectory);
        var root = Path.Combine(parent, folderName);
        EnsureNoReparseTraversal(parent, root);
        if (Path.Exists(root))
        {
            throw new VisualPageValidationException(
                $"A file or folder named '{folderName}' already exists in '{parent}'. "
                + "Choose a different project name, or use Open Hyperpage project to reopen it.");
        }

        return CreateAsync(root, folderName, rootNamespace, cancellationToken);
    }

    public Task<VisualPageProjectSession> CreateInDirectoryAsync(
        string parentDirectory,
        string name,
        HyperpageProjectCreationOptions options,
        string? rootNamespace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var folderName = ValidateNewProjectFolderName(name);
        var parent = Path.GetFullPath(parentDirectory);
        var root = Path.Combine(parent, folderName);
        EnsureNoReparseTraversal(parent, root);
        if (Path.Exists(root))
        {
            throw new VisualPageValidationException(
                $"A file or folder named '{folderName}' already exists in '{parent}'. "
                + "Choose a different project name, or use Open Hyperpage project to reopen it.");
        }

        return CreateConfiguredAsync(
            root,
            folderName,
            options,
            rootNamespace,
            cancellationToken);
    }

    public async Task<VisualPageProjectSession> CreateAsync(
        string rootPath,
        string name,
        string? rootNamespace = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(root))
        {
            throw new VisualPageValidationException(
                "A Hyperpage project root must be a directory.");
        }

        if (Directory.Exists(root)
            && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new VisualPageValidationException(
                "New Hyperpage projects require an empty folder.");
        }

        Directory.CreateDirectory(root);
        var safeName = MakeIdentifier(name, "VisualPageProject");
        var project = new VisualPageProjectManifest
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? "Hyperpage Project"
                : name.Trim(),
            RootNamespace = string.IsNullOrWhiteSpace(rootNamespace)
                ? safeName
                : rootNamespace.Trim()
        };
        var page = CreateStarterPage(project.ProjectId);
        page.Revision = VisualPageHashing.ComputePageRevision(
            page,
            string.Empty);
        var pageEntry = new VisualPageProjectPage
        {
            PageId = page.PageId,
            Name = page.Name,
            Route = page.Route,
            Slug = page.Slug,
            DocumentPath = $"Pages/{page.Slug}.visual-page.json",
            ComponentPath = GeneratedMarkupPath(project, page),
            Revision = page.Revision
        };
        project.Pages.Add(pageEntry);
        project.Theme.Tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["color.background"] = "#0d1117",
            ["color.surface"] = "#161b22",
            ["color.text"] = "#f0f6fc",
            ["color.muted"] = "#8b949e",
            ["color.accent"] = "#58a6ff",
            ["radius.card"] = "18px",
            ["space.section"] = "clamp(3rem, 8vw, 7rem)"
        };
        project.Revision = VisualPageHashing.ComputeProjectRevision(project);

        await WriteInitialScaffoldAsync(
            root,
            project,
            page,
            cancellationToken);
        return await OpenAsync(root, page.PageId, cancellationToken);
    }

    public async Task<VisualPageProjectSession> CreateConfiguredAsync(
        string rootPath,
        string name,
        HyperpageProjectCreationOptions options,
        string? rootNamespace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateCreationOptions(options);
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(root))
        {
            throw new VisualPageValidationException(
                "A Hyperpage project root must be a directory.");
        }
        if (Directory.Exists(root)
            && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new VisualPageValidationException(
                "New Hyperpage projects require an empty folder.");
        }

        var safeName = MakeIdentifier(name, "HyperpageProject");
        var project = new VisualPageProjectManifest
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? "Hyperpage Project"
                : name.Trim(),
            RootNamespace = string.IsNullOrWhiteSpace(rootNamespace)
                ? safeName
                : rootNamespace.Trim(),
            Foundation = VisualPageJson.Clone(options.Foundation)
        };
        if (project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html)
        {
            project.RazorProjectPath = project.LocalHostProjectPath;
        }

        var templateService = new VisualPageTemplateService(_catalog);
        var documents = new List<VisualPageDocument>();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedTheme = false;
        for (var index = 0; index < options.Pages.Count; index++)
        {
            var definition = options.Pages[index];
            var template = project.Foundation.UsesTemplates
                ? definition.Template
                : null;
            var pageName = FirstNonEmpty(
                definition.Name,
                template?.SuggestedPageName,
                index == 0 ? "Home" : $"Page {index + 1}");
            var slugBase = MakeSlug(pageName);
            var slug = MakeUnique(slugBase, slugs);
            var route = NormalizeCreationRoute(
                FirstNonEmpty(
                    definition.Route,
                    template?.SuggestedPageRoute,
                    index == 0 ? "/" : $"/{slug}"),
                slug,
                routes,
                project.Foundation.MarkupLanguage ==
                    HyperpageMarkupLanguage.Html);
            var componentBase = MakeIdentifier(pageName, "Page") + "Page";
            var componentName = MakeUnique(componentBase, componentNames);
            var page = new VisualPageDocument
            {
                ProjectId = project.ProjectId,
                Name = pageName,
                ComponentName = componentName,
                Route = route,
                Slug = slug,
                Title = FirstNonEmpty(
                    template?.SuggestedPageTitle,
                    pageName),
                Description = template?.SuggestedPageDescription?.Trim()
                              ?? string.Empty,
                Root = template is null
                    ? CreateBlankPageRoot(pageName)
                    : templateService.Instantiate(template)
            };
            page.Revision = VisualPageHashing.ComputePageRevision(
                page,
                string.Empty);
            var entry = new VisualPageProjectPage
            {
                PageId = page.PageId,
                Name = page.Name,
                Route = page.Route,
                Slug = page.Slug,
                DocumentPath = $"Pages/{page.Slug}.visual-page.json",
                ComponentPath = GeneratedMarkupPath(project, page),
                StarterTemplateId = template?.TemplateId ?? string.Empty,
                StarterTemplateName = template?.Name ?? string.Empty,
                Revision = page.Revision
            };
            project.Pages.Add(entry);
            documents.Add(page);

            if (!appliedTheme && template is { ThemeTokens.Count: > 0 })
            {
                project.Theme.Tokens = new Dictionary<string, string>(
                    template.ThemeTokens,
                    StringComparer.Ordinal);
                appliedTheme = true;
            }
        }

        if (!appliedTheme)
        {
            project.Theme.Tokens = CreateDefaultThemeTokens();
        }
        project.Revision = VisualPageHashing.ComputeProjectRevision(project);
        ThrowIfProjectInvalid(project);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _validator.ThrowIfInvalid(document);
            _ = _generator.Generate(project, document);
        }
        await WriteInitialScaffoldAsync(
            root,
            project,
            documents,
            cancellationToken);
        return await OpenAsync(root, documents[0].PageId, cancellationToken);
    }

    public async Task<VisualPageProjectSession> OpenAsync(
        string rootOrManifestPath,
        string? pageId = null,
        CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(rootOrManifestPath);
        var manifestPath = Directory.Exists(full)
            ? Path.Combine(full, ProjectManifestFileName)
            : full;
        if (!File.Exists(manifestPath)
            || !string.Equals(
                Path.GetFileName(manifestPath),
                ProjectManifestFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "The selected folder does not contain visual-page.project.json.",
                manifestPath);
        }

        var root = Path.GetDirectoryName(manifestPath)
                   ?? throw new InvalidOperationException(
                       "The project manifest has no parent directory.");
        EnsureNoReparseTraversal(root, manifestPath);
        var project = await ReadJsonAsync<VisualPageProjectManifest>(
            manifestPath,
            cancellationToken);
        ThrowIfProjectInvalid(project);
        var pageEntry = string.IsNullOrWhiteSpace(pageId)
            ? project.Pages.FirstOrDefault()
            : project.Pages.FirstOrDefault(page => string.Equals(
                page.PageId,
                pageId,
                StringComparison.Ordinal));
        if (pageEntry is null)
        {
            throw new VisualPageValidationException(
                string.IsNullOrWhiteSpace(pageId)
                    ? "The Hyperpage project has no pages."
                    : $"Page '{pageId}' is not part of this project.");
        }

        var documentPath = ResolveProjectPath(root, pageEntry.DocumentPath);
        var pageDocument = await ReadJsonAsync<VisualPageDocument>(
            documentPath,
            cancellationToken);
        _validator.ThrowIfInvalid(pageDocument);
        if (!string.Equals(
                pageDocument.ProjectId,
                project.ProjectId,
                StringComparison.Ordinal)
            || !string.Equals(
                pageDocument.PageId,
                pageEntry.PageId,
                StringComparison.Ordinal))
        {
            throw new VisualPageValidationException(
                "The selected page document does not belong to this project manifest.");
        }
        if (!string.Equals(
                pageDocument.Revision,
                pageEntry.Revision,
                StringComparison.Ordinal))
        {
            throw new VisualPageValidationException(
                "The page document and project manifest revisions disagree. " +
                "Use the retained Hyperpage Studio recovery snapshot; no file was overwritten.");
        }

        return new VisualPageProjectSession
        {
            RootPath = root,
            Project = project,
            Page = pageDocument
        };
    }

    public async Task SaveAsync(
        VisualPageProjectSession session,
        string expectedProjectRevision,
        string expectedPageRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var root = Path.GetFullPath(session.RootPath);
        var lockDirectory = ResolveProjectPath(root, ".visual-page");
        Directory.CreateDirectory(lockDirectory);
        var lockPath = ResolveProjectPath(root, ".visual-page/write.lock");
        await using var writeLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);

        var diskProject = await ReadJsonAsync<VisualPageProjectManifest>(
            session.ProjectManifestPath,
            cancellationToken);
        if (!string.Equals(
                diskProject.Revision,
                expectedProjectRevision,
                StringComparison.Ordinal))
        {
            throw new VisualPageStaleRevisionException(
                expectedProjectRevision,
                diskProject.Revision);
        }

        var pageEntry = session.Project.Pages.FirstOrDefault(page =>
            string.Equals(page.PageId, session.Page.PageId, StringComparison.Ordinal))
            ?? throw new VisualPageValidationException(
                "The active page is missing from the project manifest.");
        var pagePath = ResolveProjectPath(root, pageEntry.DocumentPath);
        var diskPage = await ReadJsonAsync<VisualPageDocument>(
            pagePath,
            cancellationToken);
        if (!string.Equals(
                diskPage.Revision,
                expectedPageRevision,
                StringComparison.Ordinal))
        {
            throw new VisualPageStaleRevisionException(
                expectedPageRevision,
                diskPage.Revision);
        }

        ThrowIfProjectInvalid(session.Project);
        _validator.ThrowIfInvalid(session.Page);
        var generation = _generator.Generate(session.Project, session.Page);
        DetectGeneratedDrift(root, diskPage.GeneratedFiles, generation.Artifacts);

        var pageToSave = VisualPageJson.Clone(session.Page);
        pageToSave.SourceMappings = generation.SourceMappings
            .Select(mapping =>
            {
                var copy = VisualPageJson.Clone(mapping);
                copy.RelativePath = generation.Artifacts[0].RelativePath;
                return copy;
            })
            .ToList();
        pageToSave.Diagnostics = _validator.ValidatePage(pageToSave).ToList();
        pageToSave.GeneratedFiles = generation.Artifacts
            .Select(artifact => new VisualPageGeneratedFileHash
            {
                RelativePath = artifact.RelativePath,
                Sha256 = artifact.Sha256
            })
            .ToList();
        pageToSave.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var projectToSave = VisualPageJson.Clone(session.Project);
        var savedEntry = projectToSave.Pages.First(page => string.Equals(
            page.PageId,
            pageToSave.PageId,
            StringComparison.Ordinal));
        savedEntry.Name = pageToSave.Name;
        savedEntry.Route = pageToSave.Route;
        savedEntry.Slug = pageToSave.Slug;
        savedEntry.FileType = pageToSave.FileType;
        savedEntry.ComponentPath = GeneratedMarkupPath(
            projectToSave,
            pageToSave);
        savedEntry.Revision = pageToSave.Revision;
        projectToSave.UpdatedAtUtc = DateTimeOffset.UtcNow;
        projectToSave.Revision = VisualPageHashing.ComputeProjectRevision(
            projectToSave);

        await WriteRecoveryAsync(root, diskPage, cancellationToken);
        var transactionPath = ResolveProjectPath(
            root,
            $".visual-page/transactions/{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionPath);
        try
        {
            var stagedFiles = new List<(string Staged, string Destination)>();
            foreach (var artifact in generation.Artifacts)
            {
                stagedFiles.Add(await StageTextAsync(
                    root,
                    transactionPath,
                    artifact.RelativePath,
                    artifact.Content,
                    cancellationToken));
            }
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                pageEntry.DocumentPath,
                JsonSerializer.Serialize(pageToSave, VisualPageJson.Options),
                cancellationToken));
            if (StudioWebProjectNavigationIndex.IsEnabled(projectToSave))
                stagedFiles.Add(await StageTextAsync(root, transactionPath, StudioWebProjectNavigationIndex.RelativePath,
                    StudioWebProjectNavigationIndex.Serialize(projectToSave), cancellationToken));
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                ProjectManifestFileName,
                JsonSerializer.Serialize(projectToSave, VisualPageJson.Options),
                cancellationToken));

            foreach (var file in stagedFiles
                         .Where(file => !string.Equals(
                             file.Destination,
                             session.ProjectManifestPath,
                             StringComparison.OrdinalIgnoreCase)))
            {
                CommitStagedFile(file.Staged, file.Destination);
            }
            var manifest = stagedFiles.Single(file => string.Equals(
                file.Destination,
                session.ProjectManifestPath,
                StringComparison.OrdinalIgnoreCase));
            CommitStagedFile(manifest.Staged, manifest.Destination);
        }
        finally
        {
            if (Directory.Exists(transactionPath))
            {
                Directory.Delete(transactionPath, recursive: true);
            }
        }

        session.Project = projectToSave;
        session.Page = pageToSave;
        PruneRecovery(root);
    }

    public async Task<VisualPageProjectSession> AddPageAsync(
        VisualPageProjectSession session,
        string name,
        string route,
        VisualPageElement? starterRoot = null,
        IReadOnlyDictionary<string, string>? themeTokens = null,
        string? title = null,
        string? description = null,
        HyperpagePageFileType fileType = HyperpagePageFileType.ProjectDefault,
        string? starterTemplateId = null,
        string? starterTemplateName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var root = Path.GetFullPath(session.RootPath);
        var lockDirectory = ResolveProjectPath(root, ".visual-page");
        Directory.CreateDirectory(lockDirectory);
        await using var writeLock = new FileStream(
            ResolveProjectPath(root, ".visual-page/write.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous);
        var diskProject = await ReadJsonAsync<VisualPageProjectManifest>(
            session.ProjectManifestPath,
            cancellationToken);
        if (!string.Equals(
                diskProject.Revision,
                session.Project.Revision,
                StringComparison.Ordinal))
        {
            throw new VisualPageStaleRevisionException(
                session.Project.Revision,
                diskProject.Revision);
        }

        var slug = MakeSlug(name);
        var uniqueSlug = slug;
        var number = 2;
        while (session.Project.Pages.Any(page => string.Equals(
                   page.Slug,
                   uniqueSlug,
                   StringComparison.OrdinalIgnoreCase)))
        {
            uniqueSlug = $"{slug}-{number++}";
        }

        var componentBase = MakeIdentifier(name, "Page") + "Page";
        var componentName = componentBase;
        number = 2;
        while (session.Project.Pages.Any(existing =>
                   Path.GetFileNameWithoutExtension(existing.ComponentPath)
                       .Equals(componentName, StringComparison.OrdinalIgnoreCase)))
        {
            componentName = $"{componentBase}{number++}";
        }

        var resolvedFileType = ResolvePageFileType(session.Project, fileType);
        if (session.Project.Foundation.MarkupLanguage ==
            HyperpageMarkupLanguage.Html
            && resolvedFileType != HyperpagePageFileType.Html)
        {
            throw new VisualPageValidationException(
                "Static HTML projects can add .html pages only. Choose a Razor Pages project for .cshtml or .razor pages.");
        }

        var page = new VisualPageDocument
        {
            ProjectId = session.Project.ProjectId,
            Name = string.IsNullOrWhiteSpace(name) ? "Page" : name.Trim(),
            ComponentName = componentName,
            Route = resolvedFileType == HyperpagePageFileType.Html
                ? NormalizeHtmlRoute(route, uniqueSlug)
                : NormalizeRoute(route, uniqueSlug),
            Slug = uniqueSlug,
            FileType = fileType,
            Title = string.IsNullOrWhiteSpace(title)
                ? string.IsNullOrWhiteSpace(name) ? "Page" : name.Trim()
                : title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Root = starterRoot is null
                ? new VisualPageElement
                {
                    DescriptorId = "core.page",
                    Name = "Page"
                }
                : VisualPageJson.Clone(starterRoot)
        };
        page.Revision = VisualPageHashing.ComputePageRevision(page, string.Empty);
        var entry = new VisualPageProjectPage
        {
            PageId = page.PageId,
            Name = page.Name,
            Route = page.Route,
            Slug = page.Slug,
            FileType = page.FileType,
            DocumentPath = $"Pages/{page.Slug}.visual-page.json",
            ComponentPath = GeneratedMarkupPath(session.Project, page),
            StarterTemplateId = starterTemplateId?.Trim() ?? string.Empty,
            StarterTemplateName = starterTemplateName?.Trim() ?? string.Empty,
            Revision = page.Revision
        };
        var projectToSave = VisualPageJson.Clone(session.Project);
        if (themeTokens is not null)
        {
            foreach (var token in themeTokens)
            {
                projectToSave.Theme.Tokens[token.Key] = token.Value;
            }
        }
        projectToSave.Pages.Add(entry);
        ThrowIfProjectInvalid(projectToSave);
        _validator.ThrowIfInvalid(page);
        DetectGeneratedDrift(
            root,
            session.Page.GeneratedFiles,
            _generator.Generate(session.Project, session.Page).Artifacts);
        var generation = _generator.Generate(projectToSave, page);
        page.SourceMappings = generation.SourceMappings.Select(mapping =>
        {
            var copy = VisualPageJson.Clone(mapping);
            copy.RelativePath = generation.Artifacts[0].RelativePath;
            return copy;
        }).ToList();
        page.GeneratedFiles = generation.Artifacts.Select(artifact =>
            new VisualPageGeneratedFileHash
            {
                RelativePath = artifact.RelativePath,
                Sha256 = artifact.Sha256
            }).ToList();
        page.Diagnostics = _validator.ValidatePage(page).ToList();
        projectToSave.UpdatedAtUtc = DateTimeOffset.UtcNow;
        projectToSave.Revision = VisualPageHashing.ComputeProjectRevision(
            projectToSave);

        var transactionPath = ResolveProjectPath(
            root,
            $".visual-page/transactions/{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionPath);
        try
        {
            var stagedFiles = new List<(string Staged, string Destination)>();
            foreach (var artifact in generation.Artifacts)
            {
                stagedFiles.Add(await StageTextAsync(
                    root,
                    transactionPath,
                    artifact.RelativePath,
                    artifact.Content,
                    cancellationToken));
            }
            if (ResolvePageFileType(projectToSave, page) ==
                HyperpagePageFileType.RazorPage)
            {
                stagedFiles.Add(await StageTextAsync(
                    root,
                    transactionPath,
                    UserCSharpPath(projectToSave, page),
                    EditablePartial(projectToSave, page),
                    cancellationToken));
            }
            if (StudioWebProjectNavigationIndex.IsEnabled(projectToSave))
                stagedFiles.Add(await StageTextAsync(root, transactionPath, StudioWebProjectNavigationIndex.RelativePath,
                    StudioWebProjectNavigationIndex.Serialize(projectToSave), cancellationToken));
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                UserJavaScriptPath(projectToSave, page),
                EditableJavaScript(projectToSave, page),
                cancellationToken));
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                UserCssPath(projectToSave, page),
                EditableCss(page),
                cancellationToken));
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                entry.DocumentPath,
                JsonSerializer.Serialize(page, VisualPageJson.Options),
                cancellationToken));
            stagedFiles.Add(await StageTextAsync(
                root,
                transactionPath,
                ProjectManifestFileName,
                JsonSerializer.Serialize(projectToSave, VisualPageJson.Options),
                cancellationToken));
            foreach (var file in stagedFiles.Where(file => !string.Equals(
                         file.Destination,
                         session.ProjectManifestPath,
                         StringComparison.OrdinalIgnoreCase)))
            {
                CommitStagedFile(file.Staged, file.Destination);
            }
            var manifest = stagedFiles.Single(file => string.Equals(
                file.Destination,
                session.ProjectManifestPath,
                StringComparison.OrdinalIgnoreCase));
            CommitStagedFile(manifest.Staged, manifest.Destination);
        }
        finally
        {
            if (Directory.Exists(transactionPath))
            {
                Directory.Delete(transactionPath, recursive: true);
            }
        }

        session.Project = projectToSave;
        session.Page = page;
        return session;
    }

    public async Task<VisualPageAsset> ImportAssetAsync(
        VisualPageProjectSession session,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        return await ImportAssetAsync(
            session,
            sourcePath,
            sourceName,
            string.Empty,
            cancellationToken);
    }

    public async Task<VisualPageAsset> ImportAssetAsync(
        VisualPageProjectSession session,
        string sourcePath,
        string name,
        string altText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalizedName = name.Trim();
        var normalizedAltText = altText.Trim();
        if (normalizedName.Length is 0 or > 120)
        {
            throw new VisualPageValidationException(
                "Asset names must contain 1 to 120 characters.");
        }
        if (normalizedAltText.Length > 500)
        {
            throw new VisualPageValidationException(
                "Asset alt text cannot exceed 500 characters.");
        }
        var source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists || source.Length is <= 0 or > MaximumAssetBytes)
        {
            throw new VisualPageValidationException(
                $"Assets must be non-empty files no larger than {MaximumAssetBytes / 1024 / 1024} MB.");
        }
        RejectReparsePoint(source.FullName);
        var mediaType = AssetMediaType(source.Extension)
                        ?? throw new VisualPageValidationException(
                            "Hyperpage assets support PNG, JPEG, GIF, WebP, AVIF, SVG, ICO, WOFF, WOFF2, and TTF files.");
        var asset = new VisualPageAsset
        {
            Name = normalizedName,
            RelativePath = $"Assets/{Guid.NewGuid():N}-{source.Name}",
            MediaType = mediaType,
            AltText = normalizedAltText
        };
        var destination = ResolveProjectPath(
            session.RootPath,
            asset.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = source.OpenRead())
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination);
            asset.Sha256 = VisualPageHashing.Sha256File(destination);
            session.Project.Assets.Add(asset);
            session.Project.Revision = VisualPageHashing.ComputeProjectRevision(
                session.Project);
            return asset;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public IReadOnlyList<VisualPageGeneratedDrift> InspectGeneratedDrift(
        VisualPageProjectSession session)
    {
        var generation = _generator.Generate(session.Project, session.Page);
        var expected = session.Page.GeneratedFiles.ToDictionary(
            file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var drift = new List<VisualPageGeneratedDrift>();
        foreach (var artifact in generation.Artifacts)
        {
            var fullPath = ResolveProjectPath(
                session.RootPath,
                artifact.RelativePath);
            if (!File.Exists(fullPath))
            {
                drift.Add(new VisualPageGeneratedDrift(
                    artifact.RelativePath,
                    VisualPageGeneratedDriftKind.Missing,
                    expected.GetValueOrDefault(artifact.RelativePath)?.Sha256,
                    null));
                continue;
            }

            var actual = VisualPageHashing.Sha256File(fullPath);
            if (!expected.TryGetValue(artifact.RelativePath, out var stored))
            {
                drift.Add(new VisualPageGeneratedDrift(
                    artifact.RelativePath,
                    VisualPageGeneratedDriftKind.Untracked,
                    null,
                    actual));
            }
            else if (!string.Equals(stored.Sha256, actual, StringComparison.Ordinal))
            {
                drift.Add(new VisualPageGeneratedDrift(
                    artifact.RelativePath,
                    VisualPageGeneratedDriftKind.Modified,
                    stored.Sha256,
                    actual));
            }
        }
        return drift;
    }

    private async Task WriteInitialScaffoldAsync(
        string root,
        VisualPageProjectManifest project,
        VisualPageDocument page,
        CancellationToken cancellationToken) =>
        await WriteInitialScaffoldAsync(
            root,
            project,
            [page],
            cancellationToken);

    private async Task WriteInitialScaffoldAsync(
        string root,
        VisualPageProjectManifest project,
        IReadOnlyList<VisualPageDocument> pages,
        CancellationToken cancellationToken)
    {
        if (pages.Count == 0)
        {
            throw new VisualPageValidationException(
                "A new Hyperpage project requires at least one initial page.");
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectManifestFileName] = JsonSerializer.Serialize(
                project,
                VisualPageJson.Options),
            ["src/Host/Host.csproj"] = HostProject(project),
            ["src/Host/Program.cs"] = HostProgram(project, pages[0]),
            ["src/Host/wwwroot/app.css"] = HostCss(),
            ["Shared/theme.css"] = VisualPageGenerator.GenerateThemeCss(project),
            ["README.md"] = ProjectReadme(project),
            [".gitignore"] = ProjectGitIgnore()
        };
        if (project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.RazorPages)
        {
            files["src/Pages/Pages.csproj"] = PagesProject(project);
            files["src/Pages/_Imports.razor"] = PagesImports(project);
            files["src/Pages/Shared/VisualPageComponentBase.cs"] = ComponentBase(project);
            files["src/Host/Components/App.razor"] = HostApp();
            files["src/Host/Components/Routes.razor"] = HostRoutes(project, pages[0]);
            files["src/Host/Components/Layout/MainLayout.razor"] = HostLayout();
            files["src/Host/Components/_Imports.razor"] = HostImports();
        }

        foreach (var page in pages)
        {
            var generation = _generator.Generate(project, page);
            page.SourceMappings = generation.SourceMappings
                .Select(mapping =>
                {
                    mapping.RelativePath = generation.Artifacts[0].RelativePath;
                    return mapping;
                })
                .ToList();
            page.GeneratedFiles = generation.Artifacts.Select(artifact =>
                new VisualPageGeneratedFileHash
                {
                    RelativePath = artifact.RelativePath,
                    Sha256 = artifact.Sha256
                }).ToList();
            var pageEntry = project.Pages.Single(entry => string.Equals(
                entry.PageId,
                page.PageId,
                StringComparison.Ordinal));
            files[pageEntry.DocumentPath] = JsonSerializer.Serialize(
                page,
                VisualPageJson.Options);
            foreach (var artifact in generation.Artifacts)
            {
                files[artifact.RelativePath] = artifact.Content;
            }

            if (ResolvePageFileType(project, page) ==
                HyperpagePageFileType.RazorPage)
            {
                files[UserCSharpPath(project, page)] = EditablePartial(project, page);
            }
            files[UserJavaScriptPath(project, page)] = EditableJavaScript(project, page);
            files[UserCssPath(project, page)] = EditableCss(page);
        }

        project.Revision = VisualPageHashing.ComputeProjectRevision(project);
        files[ProjectManifestFileName] = JsonSerializer.Serialize(
            project,
            VisualPageJson.Options);
        if (StudioWebProjectNavigationIndex.IsEnabled(project))
            files[StudioWebProjectNavigationIndex.RelativePath] = StudioWebProjectNavigationIndex.Serialize(project);

        foreach (var file in files)
        {
            await WriteAtomicTextAsync(
                ResolveProjectPath(root, file.Key),
                file.Value,
                cancellationToken);
        }
        Directory.CreateDirectory(ResolveProjectPath(root, "Assets"));
        Directory.CreateDirectory(ResolveProjectPath(root, "Templates"));
        Directory.CreateDirectory(ResolveProjectPath(root, ".visual-page/recovery"));
    }

    private static VisualPageDocument CreateStarterPage(string projectId)
    {
        var heading = new VisualPageElement
        {
            DescriptorId = "core.heading1",
            Name = "Hero heading",
            Text = "Build something worth exploring"
        };
        var summary = new VisualPageElement
        {
            DescriptorId = "core.paragraph",
            Name = "Hero summary",
            Text = "This responsive Blazor page is backed by ordinary source files and a portable visual document."
        };
        var button = new VisualPageElement
        {
            DescriptorId = "core.button",
            Name = "Primary action",
            Text = "Begin",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["type"] = "button"
            }
        };
        var copy = new VisualPageElement
        {
            DescriptorId = "core.container",
            Name = "Hero copy",
            Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
            {
                [VisualPageBreakpoint.Base] = new()
                {
                    Display = "flex",
                    FlexDirection = "column",
                    Gap = "1rem",
                    JustifyContent = "center"
                }
            },
            Children = [heading, summary, button]
        };
        var art = new VisualPageElement
        {
            DescriptorId = "core.container",
            Name = "Hero art",
            Text = "Hyperpage Studio",
            Styles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["min-height"] = "18rem",
                ["border"] = "1px solid color-mix(in srgb, var(--vp-accent) 35%, transparent)",
                ["border-radius"] = "var(--vp-radius-card)",
                ["background"] = "radial-gradient(circle at 20% 20%, color-mix(in srgb, var(--vp-accent) 35%, transparent), transparent 60%)",
                ["place-content"] = "center",
                ["text-align"] = "center"
            },
            Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
            {
                [VisualPageBreakpoint.Base] = new() { Display = "grid" }
            }
        };
        var hero = new VisualPageElement
        {
            DescriptorId = "atlas.hero",
            Name = "Atlas hero",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aria-label"] = "Page introduction",
                ["class"] = "atlas-hero"
            },
            Layout = new Dictionary<VisualPageBreakpoint, VisualPageLayoutSpec>
            {
                [VisualPageBreakpoint.Base] = new()
                {
                    Display = "flex",
                    FlexDirection = "column",
                    Gap = "2rem",
                    Padding = "var(--vp-space-section) clamp(1.25rem, 4vw, 5rem)"
                },
                [VisualPageBreakpoint.Large] = new()
                {
                    Display = "grid",
                    GridTemplateColumns = "minmax(0, 7fr) minmax(16rem, 5fr)",
                    Gap = "clamp(2rem, 5vw, 5rem)"
                }
            },
            Children = [copy, art]
        };
        return new VisualPageDocument
        {
            ProjectId = projectId,
            Name = "Home",
            ComponentName = "HomePage",
            Route = "/",
            Slug = "home",
            Title = "Home",
            Description = "A page created with Hypershell Hyperpage Studio.",
            Root = new VisualPageElement
            {
                DescriptorId = "core.page",
                Name = "Home page",
                Children = [hero]
            }
        };
    }

    internal static StudioLayoutSnapshot CreateDefaultStudioLayout() => new()
    {
        Name = "Canvas",
        Root = StudioLayoutNode.CreateDefault(),
        IsProjectOverride = false,
        ToolPlacements = VisualPageStudioToolCatalog.Definitions
            .Select((tool, index) => new StudioToolPlacement
            {
                ToolId = tool.Id,
                Host = tool.Id is "elements" or "project" or "language-library"
                    or "page-tree" or "templates" or "assets"
                    ? StudioToolHostKind.StudioLeft
                    : StudioToolHostKind.StudioRight,
                Order = index,
                IsVisible = true
            })
            .ToList()
    };

    private static void DetectGeneratedDrift(
        string root,
        IReadOnlyList<VisualPageGeneratedFileHash> stored,
        IReadOnlyList<VisualPageGeneratedArtifact> generated)
    {
        var expected = stored.ToDictionary(
            value => value.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var drift = new List<VisualPageGeneratedDrift>();
        foreach (var artifact in generated)
        {
            var path = ResolveProjectPath(root, artifact.RelativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            var actual = VisualPageHashing.Sha256File(path);
            if (!expected.TryGetValue(artifact.RelativePath, out var old))
            {
                drift.Add(new VisualPageGeneratedDrift(
                    artifact.RelativePath,
                    VisualPageGeneratedDriftKind.Untracked,
                    null,
                    actual));
            }
            else if (!string.Equals(old.Sha256, actual, StringComparison.Ordinal))
            {
                drift.Add(new VisualPageGeneratedDrift(
                    artifact.RelativePath,
                    VisualPageGeneratedDriftKind.Modified,
                    old.Sha256,
                    actual));
            }
        }

        if (drift.Count > 0)
        {
            throw new VisualPageGeneratedDriftException(drift);
        }
    }

    private async Task WriteRecoveryAsync(
        string root,
        VisualPageDocument page,
        CancellationToken cancellationToken)
    {
        var relativePath = $".visual-page/recovery/{page.PageId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{page.Revision[..Math.Min(12, page.Revision.Length)]}.json";
        await WriteAtomicTextAsync(
            ResolveProjectPath(root, relativePath),
            JsonSerializer.Serialize(page, VisualPageJson.Options),
            cancellationToken);
    }

    private static void PruneRecovery(string root)
    {
        var directory = ResolveProjectPath(root, ".visual-page/recovery");
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaximumRecoveryRevisions))
        {
            File.Delete(file);
        }
    }

    private static async Task<(string Staged, string Destination)> StageTextAsync(
        string root,
        string transactionPath,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var destination = ResolveProjectPath(root, relativePath);
        var staged = ResolveProjectPath(
            transactionPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        await File.WriteAllTextAsync(
            staged,
            content,
            new UTF8Encoding(false),
            cancellationToken);
        await using (var stream = new FileStream(
                         staged,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         4096,
                         FileOptions.Asynchronous))
        {
            await stream.FlushAsync(cancellationToken);
        }
        return (staged, destination);
    }

    private static void CommitStagedFile(string staged, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(staged, destination, overwrite: true);
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
                content,
                new UTF8Encoding(false),
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

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Hyperpage project file not found.", path);
        }
        if (info.Length > MaximumProjectDocumentBytes)
        {
            throw new VisualPageValidationException(
                $"'{info.Name}' exceeds the {MaximumProjectDocumentBytes / 1024 / 1024} MB project document limit.");
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   VisualPageJson.Options,
                   cancellationToken)
               ?? throw new VisualPageValidationException(
                   $"'{info.Name}' is empty or invalid.");
    }

    private void ThrowIfProjectInvalid(VisualPageProjectManifest project)
    {
        var errors = _validator.ValidateProject(project)
            .Where(value => value.Severity == VisualPageDiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new VisualPageValidationException(string.Join(
                Environment.NewLine,
                errors.Select(value => $"{value.Code}: {value.Message}")));
        }
    }

    internal static string ResolveProjectPath(string root, string relativePath)
    {
        var rootWithoutSeparator = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = rootWithoutSeparator + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relativePath))
        {
            throw new VisualPageValidationException(
                "Project file paths must be relative.");
        }
        if (relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."
                            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new VisualPageValidationException(
                "Project file paths cannot contain traversal segments, invalid names, or alternate data streams.");
        }
        var candidate = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new VisualPageValidationException(
                "A project file path escaped the Hyperpage project root.");
        }
        EnsureNoReparseTraversal(rootWithoutSeparator, candidate);
        return candidate;
    }

    internal static void EnsureNoReparseTraversal(
        string rootPath,
        string fullPath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootPath));
        if (Directory.Exists(normalizedRoot) || File.Exists(normalizedRoot))
        {
            RejectReparsePoint(normalizedRoot);
        }

        var relative = Path.GetRelativePath(normalizedRoot, fullPath);
        if (relative == ".")
        {
            return;
        }

        var current = normalizedRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }
            RejectReparsePoint(current);
        }
    }

    internal static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "Hyperpage Studio rejects symbolic links, junctions, and other reparse points inside project and cache boundaries.");
        }
    }

    internal static string MakeIdentifier(string? value, string fallback)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
        }
        if (builder.Length == 0)
        {
            builder.Append(fallback);
        }
        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }
        return builder.ToString();
    }

    internal static string MakeSlug(string? value)
    {
        var builder = new StringBuilder();
        var previousDash = false;
        foreach (var character in (value ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }
        return builder.ToString().Trim('-') is { Length: > 0 } slug
            ? slug
            : "page";
    }

    internal static string GeneratedMarkupPath(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page) switch
        {
            HyperpagePageFileType.Html => page.Route == "/"
                ? "src/Host/wwwroot/index.html"
                : $"src/Host/wwwroot/{page.Route.Trim('/')}/index.html",
            HyperpagePageFileType.Cshtml =>
                $"src/Host/Pages/{page.ComponentName}.cshtml",
            _ => $"src/Pages/Generated/{page.ComponentName}.razor"
        };

    internal static string UserCSharpPath(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page) ==
                                    HyperpagePageFileType.RazorPage
            ? $"src/Pages/Generated/{page.ComponentName}.razor.cs"
            : string.Empty;

    internal static string UserCssPath(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page) ==
                                    HyperpagePageFileType.RazorPage
            ? $"src/Pages/wwwroot/{page.ComponentName}.custom.css"
            : $"src/Host/wwwroot/assets/{page.Slug}.custom.css";

    internal static string UserJavaScriptPath(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page) ==
                                    HyperpagePageFileType.RazorPage
            ? $"src/Pages/Generated/{page.ComponentName}.razor.js"
            : $"src/Host/wwwroot/assets/{page.Slug}.custom.js";

    internal static HyperpagePageFileType ResolvePageFileType(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page.FileType);

    internal static HyperpagePageFileType ResolvePageFileType(
        VisualPageProjectManifest project,
        HyperpagePageFileType fileType) =>
        fileType != HyperpagePageFileType.ProjectDefault
            ? fileType
            : project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html
                ? HyperpagePageFileType.Html
                : string.Equals(
                    project.RazorProjectPath.Replace('\\', '/'),
                    project.LocalHostProjectPath.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)
                    ? HyperpagePageFileType.Cshtml
                    : HyperpagePageFileType.RazorPage;

    private static string ValidateNewProjectFolderName(string name)
    {
        var folderName = name.Trim();
        if (string.IsNullOrWhiteSpace(folderName)
            || folderName is "." or ".."
            || folderName.EndsWith('.')
            || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new VisualPageValidationException(
                "Choose a project name that is valid as a folder name, without path separators or a trailing period.");
        }
        return folderName;
    }

    private static void ValidateCreationOptions(
        HyperpageProjectCreationOptions options)
    {
        if (options.Foundation is null)
        {
            throw new VisualPageValidationException(
                "The Hyperpage project foundation is missing.");
        }
        if (options.Pages is null
            || options.Pages.Count is < 1 or > 20)
        {
            throw new VisualPageValidationException(
                "Choose between 1 and 20 initial pages.");
        }
        if (options.Foundation.SiteStructure == HyperpageSiteStructure.FixedPages
            && options.Foundation.PlannedPageCount != options.Pages.Count)
        {
            throw new VisualPageValidationException(
                "The fixed page count does not match the page setup list.");
        }
        if (options.Foundation.SiteStructure == HyperpageSiteStructure.DynamicSite
            && options.Pages.Count != 1)
        {
            throw new VisualPageValidationException(
                "A dynamic site starts with one foundation page and creates later routes from its data model.");
        }
        if (options.Foundation.CodingMode == HyperpageCodingMode.AiAssisted
            && options.Foundation.AiProvider == HyperpageAiProvider.None)
        {
            throw new VisualPageValidationException(
                "Choose OpenAI or a local model for AI-assisted coding.");
        }
        if (options.Foundation.CodingMode == HyperpageCodingMode.Manual
            && options.Foundation.AiProvider != HyperpageAiProvider.None)
        {
            throw new VisualPageValidationException(
                "Manual projects cannot retain an automatic AI provider selection.");
        }
        foreach (var page in options.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Name)
                || page.Name.Trim().Length > 200)
            {
                throw new VisualPageValidationException(
                    "Every initial page needs a name no longer than 200 characters.");
            }
            if (options.Foundation.UsesTemplates
                && page.Template is not { Scope: "page" })
            {
                throw new VisualPageValidationException(
                    "Choose one page template for every initial page, or turn templates off.");
            }
        }
    }

    private static VisualPageElement CreateBlankPageRoot(string pageName) =>
        new()
        {
            DescriptorId = "core.page",
            Name = $"{pageName} page"
        };

    private static Dictionary<string, string> CreateDefaultThemeTokens() =>
        new(StringComparer.Ordinal)
        {
            ["color.background"] = "#0d1117",
            ["color.surface"] = "#161b22",
            ["color.text"] = "#f0f6fc",
            ["color.muted"] = "#8b949e",
            ["color.accent"] = "#58a6ff",
            ["radius.card"] = "18px",
            ["space.section"] = "clamp(3rem, 8vw, 7rem)"
        };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string MakeUnique(string value, ISet<string> used)
    {
        var candidate = value;
        var number = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{value}{number++}";
        }
        return candidate;
    }

    private static string NormalizeCreationRoute(
        string? route,
        string slug,
        ISet<string> used,
        bool staticHtml)
    {
        var normalized = staticHtml
            ? NormalizeHtmlRoute(route, slug)
            : NormalizeRoute(route, slug);
        var candidate = normalized;
        var number = 2;
        while (!used.Add(candidate))
        {
            candidate = normalized == "/"
                ? $"/{slug}-{number++}"
                : $"{normalized.TrimEnd('/')}-{number++}";
        }
        return candidate;
    }

    internal static string NormalizeHtmlRoute(string? route, string slug)
    {
        var normalized = NormalizeRoute(route, slug);
        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(MakeSlug)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();
        return segments.Count == 0
            ? "/"
            : "/" + string.Join('/', segments);
    }

    private static string NormalizeRoute(string? route, string slug) =>
        string.IsNullOrWhiteSpace(route)
            ? $"/{slug}"
            : "/" + route.Trim().Trim('/');

    private static string? AssetMediaType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            _ => null
        };

    private static string PagesProject(VisualPageProjectManifest project) => $$"""
        <Project Sdk="Microsoft.NET.Sdk.Razor">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>{{project.RootNamespace}}</RootNamespace>
            <AssemblyName>{{project.RootNamespace}}</AssemblyName>
          </PropertyGroup>
          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>
        </Project>
        """;

    private static string PagesImports(VisualPageProjectManifest project) => $$"""
        @using Microsoft.AspNetCore.Components
        @using Microsoft.AspNetCore.Components.Forms
        @using Microsoft.AspNetCore.Components.Web
        @using {{project.RootNamespace}}
        @using {{project.RootNamespace}}.Generated
        """;

    private static string ComponentBase(VisualPageProjectManifest project) => $$"""
        using Microsoft.AspNetCore.Components;

        namespace {{project.RootNamespace}}.Shared;

        public abstract class VisualPageComponentBase : ComponentBase
        {
        }
        """;

    private static string HostProject(VisualPageProjectManifest project) =>
        project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html
            ? """
              <Project Sdk="Microsoft.NET.Sdk.Web">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                  <Nullable>enable</Nullable>
                  <ImplicitUsings>enable</ImplicitUsings>
                </PropertyGroup>
              </Project>
              """
            : """
              <Project Sdk="Microsoft.NET.Sdk.Web">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                  <Nullable>enable</Nullable>
                  <ImplicitUsings>enable</ImplicitUsings>
                </PropertyGroup>
                <ItemGroup>
                  <ProjectReference Include="..\Pages\Pages.csproj" />
                </ItemGroup>
              </Project>
              """;

    private static string HostProgram(
        VisualPageProjectManifest project,
        VisualPageDocument page) =>
        project.Foundation.MarkupLanguage == HyperpageMarkupLanguage.Html
            ? """
              var builder = WebApplication.CreateBuilder(args);
              var app = builder.Build();

              app.MapGet("/healthz", () => Results.Json(new
              {
                  status = "healthy",
                  app = "hyperpage-html-host"
              }));
              app.UseDefaultFiles();
              app.UseStaticFiles();
              app.MapFallbackToFile("index.html");
              app.Run();
              """
            : $$"""
              using Host.Components;
              using {{project.RootNamespace}}.Generated;

              var builder = WebApplication.CreateBuilder(args);
              builder.Services.AddRazorPages();
              builder.Services.AddRazorComponents()
                  .AddInteractiveServerComponents();

              var app = builder.Build();
              app.UseExceptionHandler("/Error", createScopeForErrors: true);
              app.UseAntiforgery();
              app.UseDefaultFiles();
              app.UseStaticFiles();
              app.MapStaticAssets();
              app.MapGet("/healthz", () => Results.Json(new
              {
                  status = "healthy",
                  app = "hyperpage-razor-host"
              }));
              app.MapRazorPages();
              app.MapRazorComponents<App>()
                  .AddInteractiveServerRenderMode()
                  .AddAdditionalAssemblies(typeof({{page.ComponentName}}).Assembly);
              app.Run();
              """;

    private static string HostApp() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <base href="/" />
            <link rel="stylesheet" href="@Assets["app.css"]" />
            <link rel="stylesheet" href="@Assets["Host.styles.css"]" />
            <HeadOutlet />
        </head>
        <body>
            <Routes />
            <script src="_framework/blazor.web.js"></script>
        </body>
        </html>
        """;

    private static string HostRoutes(
        VisualPageProjectManifest project,
        VisualPageDocument page) => $$"""
        <Router AppAssembly="typeof(Program).Assembly"
                AdditionalAssemblies="new[] { typeof({{project.RootNamespace}}.Generated.{{page.ComponentName}}).Assembly }">
            <Found Context="routeData">
                <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
                <FocusOnNavigate RouteData="routeData" Selector="h1" />
            </Found>
            <NotFound>
                <PageTitle>Not found</PageTitle>
                <LayoutView Layout="typeof(Layout.MainLayout)">
                    <p role="alert">This page does not exist.</p>
                </LayoutView>
            </NotFound>
        </Router>
        """;

    private static string HostLayout() => """
        @inherits LayoutComponentBase

        <div class="visual-page-host">
            @Body
        </div>

        <div id="blazor-error-ui" data-nosnippet>
            An unhandled error has occurred.
            <a href="." class="reload">Reload</a>
            <span class="dismiss">🗙</span>
        </div>
        """;

    private static string HostImports() => """
        @using System.Net.Http
        @using Microsoft.AspNetCore.Components
        @using Microsoft.AspNetCore.Components.Forms
        @using Microsoft.AspNetCore.Components.Routing
        @using Microsoft.AspNetCore.Components.Web
        @using Microsoft.AspNetCore.Components.Web.Virtualization
        @using Microsoft.JSInterop
        @using Host
        @using Host.Components
        """;

    private static string HostCss() => """
        :root {
          color-scheme: dark;
          --vp-background: #0d1117;
          --vp-surface: #161b22;
          --vp-text: #f0f6fc;
          --vp-muted: #8b949e;
          --vp-accent: #58a6ff;
          --vp-radius-card: 18px;
          --vp-space-section: clamp(3rem, 8vw, 7rem);
          font-family: Inter, ui-sans-serif, system-ui, sans-serif;
        }

        * { box-sizing: border-box; }
        body { margin: 0; background: var(--vp-background); color: var(--vp-text); }
        button, input, select, textarea { font: inherit; }
        button { cursor: pointer; }
        .visual-page-host { min-height: 100vh; }
        #blazor-error-ui { display: none; }
        """;

    private static string ProjectReadme(VisualPageProjectManifest project)
    {
        var markup = project.Foundation.MarkupLanguage ==
                     HyperpageMarkupLanguage.Html
            ? "static HTML"
            : "Razor components";
        var hosting = project.Foundation.HostingTarget ==
                      HyperpageHostingTarget.Local
            ? "local hosting"
            : "deployment to a web server";
        var structure = project.Foundation.SiteStructure ==
                        HyperpageSiteStructure.DynamicSite
            ? "dynamic site"
            : $"{project.Foundation.PlannedPageCount}-page site";
        var output = project.Foundation.MarkupLanguage ==
                     HyperpageMarkupLanguage.Html
            ? "Deployable static files are generated under `src/Host/wwwroot`."
            : "The projects under `src/Pages` and `src/Host` are ordinary .NET 10 Razor/Blazor projects.";
        return $$"""
            # {{project.Name}}

            This portable Hypershell Hyperpage Studio project is configured for
            {{markup}}, {{hosting}}, and a {{structure}}.

            {{output}} The generated site does not depend on Hypershell at runtime.
            Generated files are deterministic. Keep custom work in the explicitly
            user-owned CSS, JavaScript, and supported code files.

            Run the safe local preview host:

            ```powershell
            dotnet run --project .\src\Host\Host.csproj --urls http://127.0.0.1:0
            ```
            """;
    }

    private static string ProjectGitIgnore() => """
        bin/
        obj/
        .visual-page/cache/
        .visual-page/runtime/
        .visual-page/transactions/
        .visual-page/write.lock
        *.user
        .env
        """;

    internal static string EditablePartial(
        VisualPageProjectManifest project,
        VisualPageDocument page) => $$"""
        // User-owned partial class. Hyperpage Studio preserves this file.
        namespace {{project.RootNamespace}}.Generated;

        public partial class {{page.ComponentName}}
        {
        }
        """;

    internal static string EditableJavaScript(VisualPageDocument page) => $$"""
        // User-owned collocated JavaScript module for {{page.ComponentName}}.
        // This code runs only in the isolated compiled preview or a published host.
        export function initialize() {
        }
        """;

    internal static string EditableJavaScript(
        VisualPageProjectManifest project,
        VisualPageDocument page) => ResolvePageFileType(project, page) == HyperpagePageFileType.RazorPage
        ? EditableJavaScript(page)
        : $$"""
          // User-owned browser JavaScript for {{page.Name}}.
          // This file is loaded with a classic deferred script element, not as an ES module.
          (() => {
            'use strict';
            // Add browser event handlers here. Canvas saves preserve this file.
          })();
          """;

    internal static string EditableCss(VisualPageDocument page) => $$"""
        /* User-owned CSS for {{page.ComponentName}}. Hyperpage Studio preserves this file. */
        """;
}

public enum VisualPageGeneratedDriftKind
{
    Missing,
    Modified,
    Untracked
}

public sealed record VisualPageGeneratedDrift(
    string RelativePath,
    VisualPageGeneratedDriftKind Kind,
    string? ExpectedSha256,
    string? ActualSha256);

public sealed class VisualPageGeneratedDriftException : Exception
{
    public VisualPageGeneratedDriftException(
        IReadOnlyList<VisualPageGeneratedDrift> drift)
        : base("Generated source changed outside Hyperpage Studio. Review the drift before regenerating.")
    {
        Drift = drift;
    }

    public IReadOnlyList<VisualPageGeneratedDrift> Drift { get; }
}

public sealed record VisualPageStudioToolDefinition(
    string Id,
    string DisplayName,
    string Glyph,
    string Description);

public static class VisualPageStudioToolCatalog
{
    public static IReadOnlyList<VisualPageStudioToolDefinition> Definitions { get; } =
    [
        new("elements", "Elements", "＋", "Create elements"),
        new("project", "Project", "▤", "Browse files"),
        new("language-library", "Library", "⌘", "Browse language"),
        new("page-tree", "Structure", "⌘", "Arrange page"),
        new("templates", "Templates", "▦", "Insert templates"),
        new("assets", "Assets", "▧", "Manage assets"),
        new("language-builder", "Builder", "＋", "Build statements"),
        new("inspector", "Properties", "◫", "Edit properties"),
        new("layout", "Layout", "⊞", "Adjust layout"),
        new("styles", "Appearance", "◐", "Style elements"),
        new("state-bindings", "Data", "⇄", "Bind data"),
        new("events", "Actions", "⚡", "Connect events"),
        new("code", "Code", "</>", "Edit code"),
        new("ai", "Assistant", "AI", "Ask AI"),
        new("build", "Preview", "▶", "Build preview"),
        new("host", "Host", "◎", "Run locally"),
        new("publish", "Publish", "↑", "Prepare release")
    ];
}
