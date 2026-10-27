using Hypershell.Services;

namespace Hypershell.Extensibility;

public sealed record WorkspacePanelRegistration(
    string OwnerId,
    WorkspacePanelContribution Contribution,
    BuiltInWorkspaceSurface? BuiltInSurface = null)
{
    public bool IsBuiltIn => BuiltInSurface.HasValue;
}

public sealed class WorkspacePanelRegistry
{
    public const string CoreOwnerId = "hypershell.core";
    public const string FileExplorerPanelId = "hypershell.file-explorer";
    public const string CommandBuilderPanelId = "hypershell.command-builder";

    private readonly Dictionary<string, WorkspacePanelRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<WorkspacePanelRegistration> Registrations =>
        _registrations.Values;

    public static WorkspacePanelRegistry CreateWithBuiltIns()
    {
        var registry = new WorkspacePanelRegistry();
        registry.Register(
            new WorkspacePanelRegistration(
                CoreOwnerId,
                CreateBuiltInContribution(
                    FileExplorerPanelId,
                    "File Explorer",
                    "builtin://file-explorer",
                    WorkspacePanelSlot.Left,
                    300,
                    300,
                    WorkspaceLayoutService.DockedPanelMaximumWidth),
                BuiltInWorkspaceSurface.FileExplorer));
        registry.Register(
            new WorkspacePanelRegistration(
                CoreOwnerId,
                CreateBuiltInContribution(
                    CommandBuilderPanelId,
                    "Command Builder",
                    "builtin://command-builder",
                    WorkspacePanelSlot.Right,
                    300,
                    418,
                    WorkspaceLayoutService.DockedPanelMaximumWidth),
                BuiltInWorkspaceSurface.CommandBuilder));
        return registry;
    }

    public void Register(WorkspacePanelRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Validate(registration);
        if (!_registrations.TryAdd(registration.Contribution.Id, registration))
        {
            throw new InvalidOperationException(
                $"A workspace panel with ID '{registration.Contribution.Id}' is already registered.");
        }
    }

    public void ClearContributedPanels()
    {
        foreach (var panelId in _registrations
                     .Where(pair => !pair.Value.IsBuiltIn)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _registrations.Remove(panelId);
        }
    }

    public WorkspacePanelRegistration GetRequired(string panelId) =>
        _registrations.TryGetValue(panelId, out var registration)
            ? registration
            : throw new KeyNotFoundException(
                $"Workspace panel '{panelId}' is not registered.");

    private static WorkspacePanelContribution CreateBuiltInContribution(
        string id,
        string displayName,
        string viewResource,
        WorkspacePanelSlot preferredSlot,
        double minimumWidth,
        double preferredWidth,
        double maximumWidth) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            ViewResource = viewResource,
            Placement = new PanelPlacementPolicy
            {
                PreferredSlot = preferredSlot,
                AllowedSlots =
                [
                    WorkspacePanelSlot.Left,
                    WorkspacePanelSlot.Right,
                    WorkspacePanelSlot.Floating
                ],
                MinimumWidth = minimumWidth,
                PreferredWidth = preferredWidth,
                MaximumWidth = maximumWidth,
                AllowUserMove = true,
                AllowUserResize = true,
                AllowFloating = true
            }
        };

    private static void Validate(WorkspacePanelRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.OwnerId))
        {
            throw new ArgumentException("A panel owner ID is required.", nameof(registration));
        }

        var contribution = registration.Contribution;
        if (string.IsNullOrWhiteSpace(contribution.Id)
            || string.IsNullOrWhiteSpace(contribution.DisplayName))
        {
            throw new ArgumentException(
                "A workspace panel requires a stable ID and display name.",
                nameof(registration));
        }

        var placement = contribution.Placement;
        if (placement.AllowedSlots.Count == 0
            || !placement.AllowedSlots.Contains(placement.PreferredSlot))
        {
            throw new ArgumentException(
                "A panel's allowed slots must include its preferred slot.",
                nameof(registration));
        }

        if (placement.MinimumWidth <= 0
            || placement.MinimumWidth > placement.PreferredWidth
            || placement.PreferredWidth > placement.MaximumWidth)
        {
            throw new ArgumentException(
                "Panel width limits must be positive and ordered minimum, preferred, maximum.",
                nameof(registration));
        }

        if (placement.MinimumHeight <= 0
            || placement.MinimumHeight > placement.PreferredHeight
            || placement.PreferredHeight > placement.MaximumHeight)
        {
            throw new ArgumentException(
                "Panel height limits must be positive and ordered minimum, preferred, maximum.",
                nameof(registration));
        }
    }
}
