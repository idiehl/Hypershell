using Hypershell.Extensibility;
using System.Windows;
using System.Windows.Controls;

namespace Hypershell.Controls;

public sealed class WorkspacePanelHost : ContentControl
{
    public static readonly DependencyProperty RegistrationProperty =
        DependencyProperty.Register(
            nameof(Registration),
            typeof(WorkspacePanelRegistration),
            typeof(WorkspacePanelHost),
            new PropertyMetadata(null, OnRegistrationChanged));

    public static readonly DependencyProperty CurrentSlotProperty =
        DependencyProperty.Register(
            nameof(CurrentSlot),
            typeof(WorkspacePanelSlot),
            typeof(WorkspacePanelHost),
            new FrameworkPropertyMetadata(
                WorkspacePanelSlot.Right,
                null,
                CoerceCurrentSlot));

    public static readonly DependencyProperty IsCustomizationEnabledProperty =
        DependencyProperty.Register(
            nameof(IsCustomizationEnabled),
            typeof(bool),
            typeof(WorkspacePanelHost),
            new PropertyMetadata(false));

    public WorkspacePanelHost()
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public WorkspacePanelRegistration? Registration
    {
        get => (WorkspacePanelRegistration?)GetValue(RegistrationProperty);
        set => SetValue(RegistrationProperty, value);
    }

    public WorkspacePanelSlot CurrentSlot
    {
        get => (WorkspacePanelSlot)GetValue(CurrentSlotProperty);
        set => SetValue(CurrentSlotProperty, value);
    }

    public bool IsCustomizationEnabled
    {
        get => (bool)GetValue(IsCustomizationEnabledProperty);
        set => SetValue(IsCustomizationEnabledProperty, value);
    }

    public string PanelId => Registration?.Contribution.Id ?? string.Empty;

    public string DisplayName => Registration?.Contribution.DisplayName ?? string.Empty;

    public bool CanMoveTo(WorkspacePanelSlot slot) =>
        Registration?.Contribution.Placement is { } placement
        && placement.AllowUserMove
        && (slot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
            || placement.AllowedSlots.Contains(slot))
        && (slot != WorkspacePanelSlot.Floating || placement.AllowFloating);

    public bool CanAcceptReplacement(WorkspacePanelRegistration replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return Registration?.BuiltInSurface is not { } surface
            || replacement.Contribution.Placement.MayReplace.Contains(surface);
    }

    private static void OnRegistrationChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not WorkspacePanelHost host)
        {
            return;
        }

        if (eventArgs.NewValue is WorkspacePanelRegistration registration
            && host.ReadLocalValue(CurrentSlotProperty) == DependencyProperty.UnsetValue)
        {
            host.SetCurrentValue(
                CurrentSlotProperty,
                registration.Contribution.Placement.PreferredSlot);
        }
        else
        {
            host.CoerceValue(CurrentSlotProperty);
        }

        host.SetValue(
            System.Windows.Automation.AutomationProperties.NameProperty,
            host.DisplayName);
    }

    private static object CoerceCurrentSlot(
        DependencyObject dependencyObject,
        object baseValue)
    {
        var host = (WorkspacePanelHost)dependencyObject;
        if (host.Registration?.Contribution.Placement is not { } placement)
        {
            return baseValue;
        }

        var requestedSlot = (WorkspacePanelSlot)baseValue;
        return requestedSlot is WorkspacePanelSlot.Left or WorkspacePanelSlot.Right
               || placement.AllowedSlots.Contains(requestedSlot)
            ? requestedSlot
            : placement.PreferredSlot;
    }
}
