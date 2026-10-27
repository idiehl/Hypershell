using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed record HyperlibSemanticSearchResult(
    string EntryId,
    string LibraryId,
    string LibraryName,
    string Name,
    string Description,
    string Template,
    string Language,
    string ExecutionTarget,
    string CoverageTier,
    string LibraryOrigin,
    string Provenance,
    string ContentOrigin,
    string ReviewState,
    double Score);

public interface IHyperlibSemanticSearchService
{
    Task<IReadOnlyList<HyperlibSemanticSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class HyperlibSemanticSearchService
    : IHyperlibSemanticSearchService
{
    private const int MaximumSelectedLibraries = 4;
    private const int MaximumCandidatesPerLibrary = 24;
    private const string QueryInstruction =
        "Instruct: Given a user question, retrieve relevant Hypershell Hyperlib commands and programming references\nQuery: ";

    private static readonly HashSet<string> StopWords = new(
        [
            "a", "an", "and", "are", "as", "at", "be", "by", "for",
            "from", "how", "i", "in", "is", "it", "of", "on", "or",
            "that", "the", "this", "to", "use", "using", "with"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly CommandCatalogService _catalog;
    private readonly IWorkspaceAiEmbeddingService _embeddings;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private IReadOnlyList<LibraryIndex>? _libraryIndex;

    public HyperlibSemanticSearchService(
        CommandCatalogService catalog,
        IWorkspaceAiEmbeddingService embeddings)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _embeddings = embeddings
                      ?? throw new ArgumentNullException(nameof(embeddings));
    }

    public void InvalidateIndex() => _libraryIndex = null;

    public async Task<IReadOnlyList<HyperlibSemanticSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)
            || query.Length > 1000
            || query.IndexOf('\0') >= 0
            || limit is < 1 or > 20)
        {
            throw new ArgumentException(
                "Hyperlib search requires a bounded query and a result limit from 1 through 20.",
                nameof(query));
        }

        var normalizedQuery = query.Trim();
        var libraries = await GetLibraryIndexAsync(cancellationToken);
        if (libraries.Count == 0)
        {
            return [];
        }

        var queryVector = (await _embeddings.EmbedAsync(
            [$"{QueryInstruction}{normalizedQuery}"],
            cancellationToken))[0];
        var queryTokens = Tokenize(normalizedQuery);
        var selectedLibraries = libraries
            .Select(index => new ScoredLibrary(
                index,
                Cosine(queryVector, index.Vector)
                + (0.08 * LexicalScore(
                    index.SearchText,
                    normalizedQuery,
                    queryTokens))))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index.Library.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSelectedLibraries)
            .ToArray();

        var candidates = new List<EntryCandidate>(
            MaximumSelectedLibraries * MaximumCandidatesPerLibrary);
        foreach (var selected in selectedLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await _catalog.LoadLibraryEntriesAsync(
                selected.Index.Library);
            var queue = new PriorityQueue<EntryCandidate, double>();
            foreach (var entry in entries)
            {
                var lexical = LexicalScore(
                    entry.SearchText,
                    normalizedQuery,
                    queryTokens);
                queue.Enqueue(
                    new EntryCandidate(
                        selected.Index.Library,
                        entry,
                        selected.Score,
                        lexical),
                    lexical);
                if (queue.Count > MaximumCandidatesPerLibrary)
                {
                    queue.Dequeue();
                }
            }

            candidates.AddRange(queue.UnorderedItems.Select(item =>
                item.Element));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateVectors = await EmbedBatchesAsync(
            candidates.Select(BuildEntryDocument).ToArray(),
            cancellationToken);
        return candidates
            .Select((candidate, index) => new
            {
                Candidate = candidate,
                Score = Cosine(queryVector, candidateVectors[index])
                        + (0.05 * candidate.LibraryScore)
                        + (0.04 * candidate.LexicalScore)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Entry.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(item => BuildResult(item.Candidate, item.Score))
            .ToArray();
    }

    private async Task<IReadOnlyList<LibraryIndex>> GetLibraryIndexAsync(
        CancellationToken cancellationToken)
    {
        if (_libraryIndex is not null)
        {
            return _libraryIndex;
        }

        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_libraryIndex is not null)
            {
                return _libraryIndex;
            }

            var libraries = (await _catalog.LoadAsync())
                .Where(library => library.EffectiveEntryCount > 0)
                .OrderBy(library => library.CatalogPriority)
                .ThenBy(library => library.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var documents = libraries.Select(BuildLibraryDocument).ToArray();
            var vectors = await EmbedBatchesAsync(
                documents,
                cancellationToken);
            _libraryIndex = libraries
                .Select((library, index) => new LibraryIndex(
                    library,
                    vectors[index],
                    documents[index].ToLowerInvariant()))
                .ToArray();
            return _libraryIndex;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchesAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>(texts.Count);
        for (var offset = 0;
             offset < texts.Count;
             offset += WorkspaceAiEmbeddingService.MaximumBatchSize)
        {
            var count = Math.Min(
                WorkspaceAiEmbeddingService.MaximumBatchSize,
                texts.Count - offset);
            var batch = texts.Skip(offset).Take(count).ToArray();
            vectors.AddRange(await _embeddings.EmbedAsync(
                batch,
                cancellationToken));
        }

        return vectors;
    }

    private static string BuildLibraryDocument(CommandLibrary library) =>
        Bound(
            $"{library.DisplayName}\n"
            + $"Language: {library.Language}\n"
            + $"Category: {library.Category}\n"
            + $"Kind: {library.Kind}\n"
            + $"Ecosystem: {library.Ecosystem}\n"
            + $"Description: {library.Description}\n"
            + $"Keywords: {string.Join(' ', library.Keywords)}",
            WorkspaceAiEmbeddingService.MaximumTextCharacters);

    private static string BuildEntryDocument(EntryCandidate candidate)
    {
        var entry = candidate.Entry;
        var library = candidate.Library;
        return Bound(
            $"{entry.DisplayName} {entry.Name}\n"
            + $"Library: {library.DisplayName}\n"
            + $"Language: {library.Language}\n"
            + $"Kind: {entry.SymbolKind}\n"
            + $"Description: {entry.Description}\n"
            + $"Definition: {entry.GuideDefinition}\n"
            + $"Template: {entry.Template}\n"
            + $"Keywords: {string.Join(' ', entry.Keywords)}",
            WorkspaceAiEmbeddingService.MaximumTextCharacters);
    }

    private static HyperlibSemanticSearchResult BuildResult(
        EntryCandidate candidate,
        double score)
    {
        var library = candidate.Library;
        var entry = candidate.Entry;
        return new HyperlibSemanticSearchResult(
            entry.Id,
            library.Id,
            library.DisplayName,
            string.IsNullOrWhiteSpace(entry.DisplayName)
                ? entry.Name
                : entry.DisplayName,
            Bound(entry.Description, 1200),
            Bound(entry.Template, 2000),
            library.Language,
            entry.ExecutionTarget.ToString(),
            entry.CoverageTier.ToString(),
            library.Origin.ToString(),
            string.IsNullOrWhiteSpace(library.ContentProvenance)
                ? library.SourceUrl
                : library.ContentProvenance,
            library.ContentOrigin.ToString(),
            library.ContentReviewState.ToString(),
            Math.Round(score, 6));
    }

    private static double LexicalScore(
        string source,
        string query,
        IReadOnlySet<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return 0;
        }

        var normalized = source.ToLowerInvariant();
        var score = normalized.Contains(
            query.ToLowerInvariant(),
            StringComparison.Ordinal)
            ? 4d
            : 0d;
        foreach (var token in queryTokens)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                score += 1d + Math.Min(token.Length, 12) / 12d;
            }
        }

        return score / Math.Max(1, queryTokens.Count);
    }

    private static IReadOnlySet<string> Tokenize(string value) => value
        .ToLowerInvariant()
        .Split(
            value.Where(character => !char.IsLetterOrDigit(character)
                                     && character is not '#' and not '+')
                .Distinct()
                .ToArray(),
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries)
        .Where(token => token.Length > 1 && !StopWords.Contains(token))
        .Take(32)
        .SelectMany(ExpandToken)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ExpandToken(string token)
    {
        yield return token;
        if (token.Length > 4
            && token.EndsWith("ies", StringComparison.Ordinal))
        {
            yield return $"{token[..^3]}y";
        }
        else if (token.Length > 3
                 && token.EndsWith('s')
                 && !token.EndsWith("ss", StringComparison.Ordinal))
        {
            yield return token[..^1];
        }

        if (token.Length > 5
            && token.EndsWith("ing", StringComparison.Ordinal))
        {
            yield return token[..^3];
        }
        else if (token.Length > 4
                 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            yield return token[..^2];
        }
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            throw new InvalidDataException(
                "The Librarian returned embeddings with inconsistent dimensions.");
        }

        var dot = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
        }

        return dot;
    }

    private static string Bound(string? value, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private sealed record LibraryIndex(
        CommandLibrary Library,
        float[] Vector,
        string SearchText);

    private sealed record ScoredLibrary(
        LibraryIndex Index,
        double Score);

    private sealed record EntryCandidate(
        CommandLibrary Library,
        CommandDefinition Entry,
        double LibraryScore,
        double LexicalScore);
}
