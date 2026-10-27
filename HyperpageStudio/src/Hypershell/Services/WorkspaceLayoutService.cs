using Hypershell.Extensibility;
using Hypershell.Profiles;

namespace Hypershell.Services;

public static class WorkspaceLayoutService
{
    public const double DockedPanelMinimumWidth = 300;
    public const double DockedPanelMaximumWidth = 4096;

    private static readonly WorkspacePanelSlot[] SupportedBuiltInSlots =
    [
        WorkspacePanelSlot.Left,
        WorkspacePanelSlot.Right
    ];

    private static readonly WorkspacePanelSlot[] SupportedExtensionSlots =
    [
        WorkspacePanelSlot.Left,
        WorkspacePanelSlot.Right,
        WorkspacePanelSlot.Bottom,
        WorkspacePanelSlot.Floating
    ];

    public static WorkspaceLayoutSnapshot CreateDefault(
        WorkspacePanelRegistry registry) =>
        Normalize(new WorkspaceLayoutSnapshot(), registry);

    public static WorkspaceLayoutSnapshot Normalize(
        WorkspaceLayoutSnapshot? layout,
        WorkspacePanelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        layout ??= new WorkspaceLayoutSnapshot();

        var sourcePanels = layout.Panels
            .Where(panel => !string.IsNullOrWhiteSpace(panel.PanelId))
            .GroupBy(panel => panel.PanelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var registrations = registry.Registrations
            .OrderBy(registration => registration.BuiltInSurface switch
            {
                BuiltInWorkspaceSurface.FileExplorer => 0,
                BuiltInWorkspaceSurface.CommandBuilder => 1,
                _ => 2
            })
            .ThenBy(
                registration => registration.Contribution.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedPanels = new List<PanelPlacementSnapshot>();
        for (var index = 0; index < registrations.Count; index++)
        {
            var registration = registrations[index];
            sourcePanels.TryGetValue(
                registration.Contribution.Id,
                out var source);
            normalizedPanels.Add(NormalizePanel(registration, source, index));
        }

        // Keep profile entries for extensions that are not installed on this
        // device. They remain dormant instead of disappearing on the next save.
        var registeredIds = registrations
            .Select(registration => registration.Contribution.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        normalizedPanels.AddRange(
            layout.Panels
                .Where(panel => !string.IsNullOrWhiteSpace(panel.PanelId)
                                && !registeredIds.Contains(panel.PanelId))
                .GroupBy(panel => panel.PanelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().Clone()));

        EnforceExclusiveExtensionHosts(
            normalizedPanels,
            registrations
                .Where(registration => !registration.IsBuiltIn)
                .Select(registration => registration.Contribution.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        return new WorkspaceLayoutSnapshot
        {
            IsUserUnlocked = layout.IsUserUnlocked,
            Panels = normalizedPanels,
            SideTools = NormalizeSideTools(layout.SideTools)
        };
    }

    public static WorkspaceSideToolPlacementSnapshot GetRequiredSideTool(
        WorkspaceLayoutSnapshot layout,
        string toolId) =>
        layout.SideTools.FirstOrDefault(tool => string.Equals(
            tool.ToolId,
            WorkspaceSideToolRegistry.NormalizeToolId(toolId),
            StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException(
            $"Workspace layout does not contain side tool '{toolId}'.");

    public static PanelPlacementSnapshot GetRequiredPanel(
        WorkspaceLayoutSnapshot layout,
        string panelId) =>
        layout.Panels.FirstOrDefault(panel => string.Equals(
            panel.PanelId,
            panelId,
            StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException(
            $"Workspace layout does not contain panel '{panelId}'.");

    private static PanelPlacementSnapshot NormalizePanel(
        WorkspacePanelRegistration registration,
        PanelPlacementSnapshot? source,
        int defaultOrder)
    {
        var placement = registration.Contribution.Placement;
        BuiltInWorkspaceSurface? replacement =
            !registration.IsBuiltIn
            && source?.Replaces is { } surface
            && placement.MayReplace.Contains(surface)
                ? surface
                : null;
        IReadOnlyList<WorkspacePanelSlot> supportedSlots = registration.IsBuiltIn
            ? SupportedBuiltInSlots
            : replacement.HasValue
                ? SupportedBuiltInSlots
                : SupportedExtensionSlots;
        var availableSlots = supportedSlots
            .Where(slot => slot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
                           || placement.AllowedSlots.Contains(slot))
            .Where(slot => slot != WorkspacePanelSlot.Floating
                           || placement.AllowFloating)
            .ToList();
        var defaultSlot = availableSlots.Contains(placement.PreferredSlot)
            ? placement.PreferredSlot
            : availableSlots.Count > 0
                ? availableSlots[0]
                : placement.PreferredSlot;
        var requestedSlot = source?.Slot ?? defaultSlot;
        var slot = availableSlots.Count == 0
            ? defaultSlot
            : availableSlots.Contains(requestedSlot)
            ? requestedSlot
            : defaultSlot;
        var isSideDocked = slot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right;
        var minimumWidth = isSideDocked
            ? DockedPanelMinimumWidth
            : placement.MinimumWidth;
        var maximumWidth = isSideDocked
            ? DockedPanelMaximumWidth
            : placement.MaximumWidth;
        var requestedWidth = source?.Width ?? Math.Max(
            placement.PreferredWidth,
            minimumWidth);
        var width = double.IsFinite(requestedWidth)
            ? Math.Clamp(
                requestedWidth,
                minimumWidth,
                maximumWidth)
            : Math.Max(placement.PreferredWidth, minimumWidth);
        var requestedHeight = source?.Height is > 0
            ? source.Height
            : placement.PreferredHeight;
        var height = double.IsFinite(requestedHeight)
            ? Math.Clamp(
                requestedHeight,
                placement.MinimumHeight,
                placement.MaximumHeight)
            : placement.PreferredHeight;
        var isVisible = source?.IsVisible ?? registration.IsBuiltIn;
        if (!registration.IsBuiltIn
            && !replacement.HasValue
            && availableSlots.Count == 0)
        {
            isVisible = false;
        }

        return new PanelPlacementSnapshot
        {
            ExtensionId = registration.OwnerId,
            PanelId = registration.Contribution.Id,
            Slot = slot,
            Replaces = replacement,
            Order = source?.Order ?? defaultOrder,
            Width = width,
            Height = height,
            IsVisible = isVisible
        };
    }

    private static IReadOnlyList<WorkspaceSideToolPlacementSnapshot> NormalizeSideTools(
        IReadOnlyList<WorkspaceSideToolPlacementSnapshot>? sourceTools)
    {
        sourceTools ??= [];
        var sourceById = sourceTools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.ToolId))
            .GroupBy(tool => tool.ToolId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        // An explicit Projects setting wins. Otherwise merge both former tools
        // so a visible Git panel remains reachable after the consolidation.
        if (!sourceById.ContainsKey(WorkspaceSideToolRegistry.ProjectsToolId))
        {
            var legacyProjects = sourceById.Values.Where(tool =>
                string.Equals(
                    WorkspaceSideToolRegistry.NormalizeToolId(tool.ToolId),
                    WorkspaceSideToolRegistry.ProjectsToolId,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (legacyProjects.Count > 0)
            {
                sourceById[WorkspaceSideToolRegistry.ProjectsToolId] = new()
                {
                    ToolId = WorkspaceSideToolRegistry.ProjectsToolId,
                    Order = legacyProjects.Min(tool => tool.Order),
                    IsVisible = legacyProjects.Any(tool => tool.IsVisible)
                };
            }
        }
        var normalized = WorkspaceSideToolRegistry.Definitions
            .Select(definition =>
            {
                sourceById.TryGetValue(definition.Id, out var source);
                return new WorkspaceSideToolPlacementSnapshot
                {
                    ToolId = definition.Id,
                    Order = source?.Order ?? definition.DefaultOrder,
                    IsVisible = source?.IsVisible ?? true
                };
            })
            .ToList();

        // A visible host must always retain one selectable built-in tool. Users
        // can hide the entire host with its panel visibility setting.
        foreach (var surface in Enum.GetValues<BuiltInWorkspaceSurface>())
        {
            var definitions = WorkspaceSideToolRegistry.Definitions
                .Where(definition => definition.HostSurface == surface)
                .OrderBy(definition => definition.DefaultOrder)
                .ToList();
            if (definitions.Count == 0
                || normalized.Any(tool => tool.IsVisible
                    && definitions.Any(definition => string.Equals(
                        definition.Id,
                        tool.ToolId,
                        StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            var fallbackId = definitions[0].Id;
            var index = normalized.FindIndex(tool => string.Equals(
                tool.ToolId,
                fallbackId,
                StringComparison.OrdinalIgnoreCase));
            var fallback = normalized[index];
            normalized[index] = new WorkspaceSideToolPlacementSnapshot
            {
                ToolId = fallback.ToolId,
                Order = fallback.Order,
                IsVisible = true
            };
        }

        var knownIds = WorkspaceSideToolRegistry.Definitions
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        normalized.AddRange(sourceTools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.ToolId)
                           && !knownIds.Contains(
                               WorkspaceSideToolRegistry.NormalizeToolId(tool.ToolId)))
            .GroupBy(tool => tool.ToolId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Clone()));
        return normalized;
    }

    private static void EnforceExclusiveExtensionHosts(
        IList<PanelPlacementSnapshot> panels,
        ISet<string> registeredExtensionPanelIds)
    {
        var occupiedReplacementHosts = new HashSet<BuiltInWorkspaceSurface>();
        var bottomHostOccupied = false;
        foreach (var index in Enumerable.Range(0, panels.Count)
                     .OrderBy(index => panels[index].Order))
        {
            var panel = panels[index];
            if (!panel.IsVisible
                || !registeredExtensionPanelIds.Contains(panel.PanelId))
            {
                continue;
            }

            var hasConflict = panel.Replaces is { } replacement
                ? !occupiedReplacementHosts.Add(replacement)
                : panel.Slot == WorkspacePanelSlot.Bottom
                  && bottomHostOccupied;
            if (panel.Replaces is null
                && panel.Slot == WorkspacePanelSlot.Bottom
                && !bottomHostOccupied)
            {
                bottomHostOccupied = true;
            }

            if (!hasConflict)
            {
                continue;
            }

            panels[index] = new PanelPlacementSnapshot
            {
                ExtensionId = panel.ExtensionId,
                PanelId = panel.PanelId,
                Slot = panel.Slot,
                Replaces = panel.Replaces,
                Order = panel.Order,
                Width = panel.Width,
                Height = panel.Height,
                IsVisible = false
            };
        }
    }
}
