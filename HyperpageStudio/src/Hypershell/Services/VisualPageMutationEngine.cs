using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hypershell.Models;

namespace Hypershell.Services;

public static class VisualPageJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)
        ?? throw new InvalidOperationException(
            $"Unable to clone {typeof(T).Name}.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = VisualPageContract.MaximumTreeDepth + 16
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public static class VisualPageHashing
{
    public static string Sha256Text(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ComputePageRevision(
        VisualPageDocument page,
        string previousRevision,
        IReadOnlyList<VisualPageMutationOperation>? operations = null)
    {
        var clone = VisualPageJson.Clone(page);
        clone.Revision = string.Empty;
        clone.UpdatedAtUtc = DateTimeOffset.UnixEpoch;
        clone.Diagnostics = [];
        clone.SourceMappings = [];
        clone.GeneratedFiles = [];
        var content = JsonSerializer.Serialize(clone, VisualPageJson.Options);
        var mutation = operations is null
            ? string.Empty
            : JsonSerializer.Serialize(operations, VisualPageJson.Options);
        return Sha256Text($"{previousRevision}\n{content}\n{mutation}");
    }

    public static string ComputeProjectRevision(
        VisualPageProjectManifest project)
    {
        var clone = VisualPageJson.Clone(project);
        clone.Revision = string.Empty;
        clone.UpdatedAtUtc = DateTimeOffset.UnixEpoch;
        return Sha256Text(JsonSerializer.Serialize(clone, VisualPageJson.Options));
    }
}

public sealed class VisualPageMutationEngine
{
    private readonly VisualPageSchemaValidator _validator;

    public VisualPageMutationEngine(VisualPageSchemaValidator validator)
    {
        _validator = validator;
    }

    public VisualPageMutationResult Apply(
        VisualPageDocument current,
        VisualPageMutationSet mutationSet)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutationSet);
        if (!string.Equals(
                current.Revision,
                mutationSet.ExpectedRevision,
                StringComparison.Ordinal))
        {
            throw new VisualPageStaleRevisionException(
                mutationSet.ExpectedRevision,
                current.Revision);
        }

        if (mutationSet.Operations.Count is 0
            or > VisualPageContract.MaximumMutationOperations)
        {
            throw new VisualPageValidationException(
                $"A mutation set must contain between 1 and {VisualPageContract.MaximumMutationOperations} operations.");
        }

        var document = VisualPageJson.Clone(current);
        var changes = new List<VisualPageSemanticChange>(
            mutationSet.Operations.Count);
        foreach (var operation in mutationSet.Operations)
        {
            changes.Add(ApplyOperation(document, operation));
        }

        _validator.ThrowIfInvalid(document);
        var previousRevision = current.Revision;
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        document.Revision = VisualPageHashing.ComputePageRevision(
            document,
            previousRevision,
            mutationSet.Operations);
        return new VisualPageMutationResult
        {
            Document = document,
            PreviousRevision = previousRevision,
            Revision = document.Revision,
            Changes = changes
        };
    }

    private static VisualPageSemanticChange ApplyOperation(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            throw new VisualPageValidationException(
                "Every mutation operation requires an operation ID.");
        }

        return operation.Kind switch
        {
            VisualPageMutationKind.SetPageMetadata =>
                SetPageMetadata(document, operation),
            VisualPageMutationKind.SetStylesheets =>
                SetStylesheets(document, operation),
            VisualPageMutationKind.InsertChild =>
                InsertChild(document, operation),
            VisualPageMutationKind.RemoveElement =>
                RemoveElement(document, operation),
            VisualPageMutationKind.MoveElement =>
                MoveElement(document, operation),
            VisualPageMutationKind.DuplicateElement =>
                DuplicateElement(document, operation),
            VisualPageMutationKind.WrapElement =>
                WrapElement(document, operation),
            VisualPageMutationKind.UnwrapElement =>
                UnwrapElement(document, operation),
            VisualPageMutationKind.SetText =>
                MutateElement(document, operation, element =>
                    element.Text = operation.Value ?? string.Empty,
                    "Changed text"),
            VisualPageMutationKind.SetAttribute =>
                SetDictionaryValue(document, operation, styles: false),
            VisualPageMutationKind.RemoveAttribute =>
                RemoveDictionaryValue(document, operation, styles: false),
            VisualPageMutationKind.SetStyle =>
                SetDictionaryValue(document, operation, styles: true),
            VisualPageMutationKind.RemoveStyle =>
                RemoveDictionaryValue(document, operation, styles: true),
            VisualPageMutationKind.SetLayout =>
                MutateElement(document, operation, element =>
                    element.Layout[operation.Breakpoint] =
                        operation.Layout
                        ?? throw new VisualPageValidationException(
                            "SetLayout requires a layout value."),
                    $"Changed {operation.Breakpoint} layout"),
            VisualPageMutationKind.SetBinding =>
                SetBinding(document, operation),
            VisualPageMutationKind.RemoveBinding =>
                RemoveBinding(document, operation),
            VisualPageMutationKind.SetEvent =>
                SetEvent(document, operation),
            VisualPageMutationKind.RemoveEvent =>
                RemoveEvent(document, operation),
            VisualPageMutationKind.SetLocked =>
                MutateElement(document, operation, element =>
                    element.IsLocked = operation.Flag ?? false,
                    operation.Flag == true ? "Locked element" : "Unlocked element"),
            VisualPageMutationKind.SetHidden =>
                MutateElement(document, operation, element =>
                    element.IsHidden = operation.Flag ?? false,
                    operation.Flag == true ? "Hid element" : "Showed element"),
            VisualPageMutationKind.AddState => AddState(document, operation),
            VisualPageMutationKind.RemoveState => RemoveState(document, operation),
            _ => throw new VisualPageValidationException(
                $"Unsupported mutation kind '{operation.Kind}'.")
        };
    }

    private static VisualPageSemanticChange SetStylesheets(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        document.StylesheetPaths = (operation.StylesheetPaths
                ?? throw new VisualPageValidationException("SetStylesheets requires stylesheet paths."))
            .Select(HyperpageStylesheetService.NormalizeStylesheetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Change(operation, document.Root.ElementId, "Updated page stylesheets");
    }

    private static VisualPageSemanticChange SetPageMetadata(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var metadata = operation.PageMetadata
                       ?? throw new VisualPageValidationException(
                           "SetPageMetadata requires page metadata.");
        document.Name = metadata.Name.Trim();
        document.Route = metadata.Route.Trim();
        document.Slug = metadata.Slug.Trim();
        document.Title = metadata.Title.Trim();
        document.Description = metadata.Description.Trim();
        return Change(operation, document.Root.ElementId, "Changed page metadata");
    }

    private static VisualPageSemanticChange InsertChild(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var parent = FindRequired(document.Root, operation.ParentElementId);
        EnsureUnlocked(parent);
        var element = operation.Element is null
            ? throw new VisualPageValidationException(
                "InsertChild requires an element.")
            : VisualPageJson.Clone(operation.Element);
        EnsureIdsDoNotCollide(document.Root, element);
        var index = NormalizeInsertIndex(operation.Index, parent.Children.Count);
        parent.Children.Insert(index, element);
        return Change(operation, element.ElementId,
            $"Added {element.Name} to {parent.Name}");
    }

    private static VisualPageSemanticChange RemoveElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var location = FindParentRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(location.Element);
        location.Parent.Children.RemoveAt(location.Index);
        return Change(operation, location.Element.ElementId,
            $"Removed {location.Element.Name}");
    }

    private static VisualPageSemanticChange MoveElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var source = FindParentRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(source.Element);
        var target = FindRequired(document.Root, operation.ParentElementId);
        EnsureUnlocked(target);
        if (Contains(source.Element, target.ElementId))
        {
            throw new VisualPageValidationException(
                "An element cannot be moved into itself or one of its descendants.");
        }

        source.Parent.Children.RemoveAt(source.Index);
        var index = NormalizeInsertIndex(operation.Index, target.Children.Count);
        target.Children.Insert(index, source.Element);
        return Change(operation, source.Element.ElementId,
            $"Moved {source.Element.Name} into {target.Name}");
    }

    private static VisualPageSemanticChange DuplicateElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var source = FindParentRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(source.Element);
        var clone = VisualPageJson.Clone(source.Element);
        RemapElementIds(clone);
        source.Parent.Children.Insert(source.Index + 1, clone);
        return Change(operation, clone.ElementId,
            $"Duplicated {source.Element.Name}");
    }

    private static VisualPageSemanticChange WrapElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var source = FindParentRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(source.Element);
        var wrapper = operation.Element is null
            ? throw new VisualPageValidationException(
                "WrapElement requires a wrapper element.")
            : VisualPageJson.Clone(operation.Element);
        if (wrapper.Children.Count > 0)
        {
            throw new VisualPageValidationException(
                "A wrapper supplied to WrapElement must not already have children.");
        }

        EnsureIdsDoNotCollide(document.Root, wrapper);
        source.Parent.Children[source.Index] = wrapper;
        wrapper.Children.Add(source.Element);
        return Change(operation, wrapper.ElementId,
            $"Wrapped {source.Element.Name} in {wrapper.Name}");
    }

    private static VisualPageSemanticChange UnwrapElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var source = FindParentRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(source.Element);
        source.Parent.Children.RemoveAt(source.Index);
        for (var index = 0; index < source.Element.Children.Count; index++)
        {
            source.Parent.Children.Insert(
                source.Index + index,
                source.Element.Children[index]);
        }

        return Change(operation, source.Element.ElementId,
            $"Unwrapped {source.Element.Name}");
    }

    private static VisualPageSemanticChange SetDictionaryValue(
        VisualPageDocument document,
        VisualPageMutationOperation operation,
        bool styles) =>
        MutateElement(document, operation, element =>
        {
            if (string.IsNullOrWhiteSpace(operation.Name))
            {
                throw new VisualPageValidationException(
                    "A property name is required.");
            }

            (styles ? element.Styles : element.Attributes)[operation.Name] =
                operation.Value ?? string.Empty;
        }, $"Changed {(styles ? "style" : "attribute")} {operation.Name}");

    private static VisualPageSemanticChange RemoveDictionaryValue(
        VisualPageDocument document,
        VisualPageMutationOperation operation,
        bool styles) =>
        MutateElement(document, operation, element =>
            (styles ? element.Styles : element.Attributes).Remove(operation.Name),
            $"Removed {(styles ? "style" : "attribute")} {operation.Name}");

    private static VisualPageSemanticChange SetBinding(
        VisualPageDocument document,
        VisualPageMutationOperation operation) =>
        MutateElement(document, operation, element =>
        {
            var binding = operation.Binding
                          ?? throw new VisualPageValidationException(
                              "SetBinding requires a binding.");
            element.Bindings.RemoveAll(existing =>
                string.Equals(existing.BindingId, binding.BindingId, StringComparison.Ordinal));
            element.Bindings.Add(VisualPageJson.Clone(binding));
        }, "Changed binding");

    private static VisualPageSemanticChange RemoveBinding(
        VisualPageDocument document,
        VisualPageMutationOperation operation) =>
        MutateElement(document, operation, element =>
            element.Bindings.RemoveAll(binding =>
                string.Equals(binding.BindingId, operation.Name, StringComparison.Ordinal)),
            "Removed binding");

    private static VisualPageSemanticChange SetEvent(
        VisualPageDocument document,
        VisualPageMutationOperation operation) =>
        MutateElement(document, operation, element =>
        {
            var eventBinding = operation.Event
                               ?? throw new VisualPageValidationException(
                                   "SetEvent requires an event binding.");
            element.Events.RemoveAll(existing =>
                string.Equals(existing.EventId, eventBinding.EventId, StringComparison.Ordinal));
            element.Events.Add(VisualPageJson.Clone(eventBinding));
        }, "Changed event handler");

    private static VisualPageSemanticChange RemoveEvent(
        VisualPageDocument document,
        VisualPageMutationOperation operation) =>
        MutateElement(document, operation, element =>
            element.Events.RemoveAll(eventBinding =>
                string.Equals(eventBinding.EventId, operation.Name, StringComparison.Ordinal)),
            "Removed event handler");

    private static VisualPageSemanticChange AddState(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var state = operation.State
                    ?? throw new VisualPageValidationException(
                        "AddState requires a state definition.");
        if (document.State.Any(existing =>
                string.Equals(existing.StateId, state.StateId, StringComparison.Ordinal)
                || string.Equals(existing.Name, state.Name, StringComparison.Ordinal)))
        {
            throw new VisualPageValidationException(
                $"State '{state.Name}' already exists.");
        }

        document.State.Add(VisualPageJson.Clone(state));
        return Change(operation, document.Root.ElementId,
            $"Added state {state.Name}");
    }

    private static VisualPageSemanticChange RemoveState(
        VisualPageDocument document,
        VisualPageMutationOperation operation)
    {
        var removed = document.State.RemoveAll(state =>
            string.Equals(state.StateId, operation.Name, StringComparison.Ordinal));
        if (removed == 0)
        {
            throw new VisualPageValidationException(
                $"State '{operation.Name}' was not found.");
        }

        return Change(operation, document.Root.ElementId, "Removed state");
    }

    private static VisualPageSemanticChange MutateElement(
        VisualPageDocument document,
        VisualPageMutationOperation operation,
        Action<VisualPageElement> mutation,
        string summary)
    {
        var element = FindRequired(document.Root, operation.TargetElementId);
        EnsureUnlocked(element);
        mutation(element);
        return Change(operation, element.ElementId, summary);
    }

    private static VisualPageSemanticChange Change(
        VisualPageMutationOperation operation,
        string elementId,
        string summary) => new()
        {
            OperationId = operation.OperationId,
            Kind = operation.Kind,
            ElementId = elementId,
            Summary = summary
        };

    public static VisualPageElement FindRequired(
        VisualPageElement root,
        string elementId)
    {
        if (string.Equals(root.ElementId, elementId, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = Find(child, elementId);
            if (found is not null)
            {
                return found;
            }
        }

        throw new VisualPageValidationException(
            $"Element '{elementId}' was not found.");
    }

    private static VisualPageElement? Find(
        VisualPageElement element,
        string elementId)
    {
        if (string.Equals(element.ElementId, elementId, StringComparison.Ordinal))
        {
            return element;
        }

        foreach (var child in element.Children)
        {
            var found = Find(child, elementId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static ElementLocation FindParentRequired(
        VisualPageElement root,
        string elementId)
    {
        for (var index = 0; index < root.Children.Count; index++)
        {
            var child = root.Children[index];
            if (string.Equals(child.ElementId, elementId, StringComparison.Ordinal))
            {
                return new ElementLocation(root, child, index);
            }

            var nested = FindParent(child, elementId);
            if (nested is not null)
            {
                return nested;
            }
        }

        throw new VisualPageValidationException(
            string.Equals(root.ElementId, elementId, StringComparison.Ordinal)
                ? "The page root cannot be removed, moved, duplicated, wrapped, or unwrapped."
                : $"Element '{elementId}' was not found.");
    }

    private static ElementLocation? FindParent(
        VisualPageElement parent,
        string elementId)
    {
        for (var index = 0; index < parent.Children.Count; index++)
        {
            var child = parent.Children[index];
            if (string.Equals(child.ElementId, elementId, StringComparison.Ordinal))
            {
                return new ElementLocation(parent, child, index);
            }

            var nested = FindParent(child, elementId);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool Contains(VisualPageElement root, string elementId) =>
        string.Equals(root.ElementId, elementId, StringComparison.Ordinal)
        || root.Children.Any(child => Contains(child, elementId));

    private static void EnsureUnlocked(VisualPageElement element)
    {
        if (element.IsLocked)
        {
            throw new VisualPageValidationException(
                $"Element '{element.Name}' is locked.");
        }
    }

    private static int NormalizeInsertIndex(int? index, int count)
    {
        if (index is null)
        {
            return count;
        }

        if (index < 0 || index > count)
        {
            throw new VisualPageValidationException(
                $"Insert index {index} is outside the valid range 0 through {count}.");
        }

        return index.Value;
    }

    private static void EnsureIdsDoNotCollide(
        VisualPageElement documentRoot,
        VisualPageElement candidate)
    {
        var documentIds = CollectIds(documentRoot);
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        CollectCandidateIds(candidate, candidateIds);
        if (candidateIds.Overlaps(documentIds))
        {
            throw new VisualPageValidationException(
                "Inserted elements must have IDs that do not already exist in the page.");
        }
    }

    private static HashSet<string> CollectIds(VisualPageElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectCandidateIds(root, ids);
        return ids;
    }

    private static void CollectCandidateIds(
        VisualPageElement element,
        HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(element.ElementId)
            || !ids.Add(element.ElementId))
        {
            throw new VisualPageValidationException(
                "Inserted element IDs must be non-empty and unique.");
        }

        foreach (var child in element.Children)
        {
            CollectCandidateIds(child, ids);
        }
    }

    public static void RemapElementIds(VisualPageElement element)
    {
        element.ElementId = Guid.NewGuid().ToString("N");
        foreach (var binding in element.Bindings)
        {
            binding.BindingId = Guid.NewGuid().ToString("N");
        }
        foreach (var eventBinding in element.Events)
        {
            eventBinding.EventId = Guid.NewGuid().ToString("N");
        }
        foreach (var child in element.Children)
        {
            RemapElementIds(child);
        }
    }

    private sealed record ElementLocation(
        VisualPageElement Parent,
        VisualPageElement Element,
        int Index);
}

public sealed class VisualPageStaleRevisionException : Exception
{
    public VisualPageStaleRevisionException(string expected, string actual)
        : base($"The page changed after this operation was prepared. Expected revision '{expected}', current revision '{actual}'.")
    {
        ExpectedRevision = expected;
        ActualRevision = actual;
    }

    public string ExpectedRevision { get; }
    public string ActualRevision { get; }
}
