using Hypershell.Extensibility;
using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed class DocumentProviderRegistry
{
    private readonly TextDocumentProvider _textProvider = new();
    private readonly MarkdownDocumentProvider _markdownProvider = new();
    private readonly JsonDocumentProvider _jsonProvider = new();
    private readonly IReadOnlyDictionary<string, IDocumentProvider> _providers;
    private IReadOnlyList<DocumentProviderRegistration> _registrations = [];

    public DocumentProviderRegistry(ExtensionDiscoverySnapshot? extensionCatalog = null)
    {
        _providers = new Dictionary<string, IDocumentProvider>(StringComparer.OrdinalIgnoreCase)
        {
            [_textProvider.Definition.Id] = _textProvider,
            [_markdownProvider.Definition.Id] = _markdownProvider,
            [_jsonProvider.Definition.Id] = _jsonProvider
        };
        Rebuild(extensionCatalog);
    }

    public DocumentProviderCatalogSnapshot Catalog { get; private set; } = new();

    public DocumentProviderCatalogSnapshot Rebuild(
        ExtensionDiscoverySnapshot? extensionCatalog)
    {
        var builtIns = CreateBuiltInRegistrations();
        var builtInExtensions = builtIns
            .SelectMany(registration => registration.FileExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extensionSources = (extensionCatalog?.Packages ?? [])
            .Where(package => package.Status == ExtensionPackageStatus.Validated
                              && package.Manifest?.Runtime == ExtensionRuntimeKind.ContentOnly)
            .SelectMany(package =>
                (package.Manifest?.Contributions.DocumentFormats ?? [])
                    .Where(contribution => contribution.Kind == DocumentContributionKind.TextEditor)
                    .Select(contribution => new ExtensionRegistrationSource(
                        package,
                        contribution)))
            .ToList();
        var extensionUseCounts = extensionSources
            .SelectMany(source => source.Contribution.FileExtensions)
            .GroupBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var extensions = extensionSources
            .Select(source => CreateExtensionRegistration(
                source,
                builtInExtensions,
                extensionUseCounts))
            .ToList();

        _registrations = builtIns
            .Concat(extensions)
            .OrderByDescending(registration => registration.IsBuiltIn)
            .ThenBy(registration => registration.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Catalog = new DocumentProviderCatalogSnapshot
        {
            RefreshedAtUtc = DateTimeOffset.UtcNow,
            Registrations = _registrations
        };
        return Catalog;
    }

    public ResolvedDocumentProvider Resolve(
        string? path,
        string? languageId = null)
    {
        var extension = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetExtension(path);
        var registration = !string.IsNullOrWhiteSpace(extension)
            ? _registrations.FirstOrDefault(candidate =>
                candidate.IsActive
                && candidate.FileExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            : null;
        registration ??= !string.IsNullOrWhiteSpace(languageId)
            ? _registrations.FirstOrDefault(candidate =>
                candidate.IsActive
                && candidate.IsBuiltIn
                && string.Equals(
                    candidate.LanguageId,
                    languageId,
                    StringComparison.OrdinalIgnoreCase))
            : null;
        var effectiveLanguageId = registration?.LanguageId;
        if (string.IsNullOrWhiteSpace(effectiveLanguageId))
        {
            effectiveLanguageId = !string.IsNullOrWhiteSpace(languageId)
                ? languageId
                : EditorLanguageCatalog.ResolveLanguageId(path);
        }

        registration ??= CreateFallbackRegistration(effectiveLanguageId);
        var provider = _providers.GetValueOrDefault(
                           registration.ProviderId,
                           _textProvider)
                       ?? _textProvider;
        return new ResolvedDocumentProvider
        {
            Provider = provider,
            Registration = registration,
            LanguageId = effectiveLanguageId
        };
    }

    public ResolvedDocumentProvider ResolveByLanguage(string languageId)
    {
        var effectiveLanguageId = string.IsNullOrWhiteSpace(languageId)
            ? "plaintext"
            : languageId.Trim();
        var registration = _registrations.FirstOrDefault(candidate =>
            candidate.IsActive
            && candidate.IsBuiltIn
            && string.Equals(
                candidate.LanguageId,
                effectiveLanguageId,
                StringComparison.OrdinalIgnoreCase))
            ?? CreateFallbackRegistration(effectiveLanguageId);
        var provider = _providers.GetValueOrDefault(
                           registration.ProviderId,
                           _textProvider)
                       ?? _textProvider;
        return new ResolvedDocumentProvider
        {
            Provider = provider,
            Registration = registration,
            LanguageId = effectiveLanguageId
        };
    }

    public string CreateOpenDialogFilter()
    {
        var extensions = _registrations
            .Where(registration => registration.IsActive)
            .SelectMany(registration => registration.FileExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}")
            .ToList();
        var textPatterns = extensions.Count == 0
            ? "*.*"
            : string.Join(';', extensions);
        var supportedPatterns = extensions
            .Append("*.pdf")
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"All supported documents|{string.Join(';', supportedPatterns)}"
               + "|PDF documents|*.pdf"
               + $"|Code, Markdown, and text documents|{textPatterns}"
               + "|All files|*.*";
    }

    private IReadOnlyList<DocumentProviderRegistration> CreateBuiltInRegistrations()
    {
        var registrations = new List<DocumentProviderRegistration>();
        foreach (var language in EditorLanguageCatalog.Languages)
        {
            IDocumentProvider provider = string.Equals(language.Id, "markdown", StringComparison.OrdinalIgnoreCase)
                ? _markdownProvider
                : string.Equals(language.Id, "json", StringComparison.OrdinalIgnoreCase)
                    ? _jsonProvider
                    : _textProvider;
            registrations.Add(new DocumentProviderRegistration
            {
                Id = $"core.document.registration.{language.Id}",
                DisplayName = provider.Definition.DisplayName,
                ProviderId = provider.Definition.Id,
                LanguageId = language.Id,
                FileExtensions = language.FileExtensions,
                IsBuiltIn = true,
                IsActive = true,
                StatusMessage = "Built-in host provider."
            });
        }

        return registrations;
    }

    private DocumentProviderRegistration CreateExtensionRegistration(
        ExtensionRegistrationSource source,
        IReadOnlySet<string> builtInExtensions,
        IReadOnlyDictionary<string, int> extensionUseCounts)
    {
        var builtInConflicts = source.Contribution.FileExtensions
            .Where(builtInExtensions.Contains)
            .ToList();
        var packageConflicts = source.Contribution.FileExtensions
            .Where(extension => extensionUseCounts.GetValueOrDefault(extension) > 1)
            .ToList();
        var isActive = builtInConflicts.Count == 0 && packageConflicts.Count == 0;
        IDocumentProvider provider = string.Equals(
            source.Contribution.LanguageId,
            "markdown",
            StringComparison.OrdinalIgnoreCase)
            ? _markdownProvider
            : string.Equals(
                source.Contribution.LanguageId,
                "json",
                StringComparison.OrdinalIgnoreCase)
                ? _jsonProvider
                : _textProvider;
        var status = builtInConflicts.Count > 0
            ? $"Dormant because the core owns {string.Join(", ", builtInConflicts)}."
            : packageConflicts.Count > 0
                ? $"Dormant because multiple packages claim {string.Join(", ", packageConflicts)}."
                : "Active static TextEditor mapping; no extension code is loaded.";
        return new DocumentProviderRegistration
        {
            Id = $"{source.Package.Manifest!.Id}:{source.Contribution.Id}",
            DisplayName = source.Contribution.DisplayName,
            ProviderId = provider.Definition.Id,
            LanguageId = source.Contribution.LanguageId,
            FileExtensions = source.Contribution.FileExtensions,
            IsBuiltIn = false,
            IsActive = isActive,
            OwnerId = source.Package.Manifest.Id,
            StatusMessage = status
        };
    }

    private DocumentProviderRegistration CreateFallbackRegistration(
        string languageId) =>
        new()
        {
            Id = "core.document.registration.fallback",
            DisplayName = _textProvider.Definition.DisplayName,
            ProviderId = _textProvider.Definition.Id,
            LanguageId = languageId,
            IsBuiltIn = true,
            IsActive = true,
            StatusMessage = "Built-in fallback provider."
        };

    private sealed record ExtensionRegistrationSource(
        DiscoveredExtensionPackage Package,
        DocumentFormatContribution Contribution);
}
