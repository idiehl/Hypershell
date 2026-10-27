using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Hypershell.Services;

public sealed record WorkspaceAiWindowsUiCall(
    string CallId,
    string Operation,
    string? ElementId,
    string? Value);

public interface IWorkspaceAiWindowsUiControlTarget
{
    void ResetWindowsUiSnapshot();

    Task<JsonElement> ExecuteWindowsUiAsync(
        WorkspaceAiWindowsUiCall call,
        CancellationToken cancellationToken = default);
}

public static class WorkspaceAiWindowsUiFunctionTool
{
    public const string FunctionName = "control_windows_ui";
    public const string SnapshotMarker =
        "[HYPERSHELL_WINDOWS_UI_SNAPSHOT]";

    private static readonly HashSet<string> Operations =
        new(StringComparer.Ordinal)
        {
            "inspect",
            "focus",
            "invoke",
            "set_value",
            "toggle",
            "expand",
            "collapse",
            "select",
            "scroll_into_view"
        };

    private static readonly JsonObject Definition = JsonNode.Parse(
        """
        {
          "type": "function",
          "name": "control_windows_ui",
          "description": "Inspect and operate standard Windows UI Automation controls inside the Context-authorized Hypershell window or desktop. Begin with operation inspect. The result is a bounded semantic map of named windows, buttons, fields, menus, lists, and supported actions. Use the returned element_id for exactly one follow-up action. Every result is a new snapshot, so old IDs expire immediately. Prefer this tool over pixel coordinates; use control_computer only for custom-rendered or unavailable controls. set_value never reads or returns existing field values and refuses password fields.",
          "strict": true,
          "parameters": {
            "type": "object",
            "properties": {
              "operation": { "type": "string", "enum": ["inspect", "focus", "invoke", "set_value", "toggle", "expand", "collapse", "select", "scroll_into_view"] },
              "element_id": { "type": ["string", "null"], "maxLength": 96 },
              "value": { "type": ["string", "null"], "maxLength": 4096 }
            },
            "required": ["operation", "element_id", "value"],
            "additionalProperties": false
          }
        }
        """)?.AsObject()
        ?? throw new InvalidOperationException(
            "The Windows UI Automation tool definition is malformed.");

    public static JsonObject CreateDefinition() =>
        Definition.DeepClone().AsObject();

    public static WorkspaceAiWindowsUiCall Parse(
        WorkspaceAiFunctionCall functionCall)
    {
        ArgumentNullException.ThrowIfNull(functionCall);
        if (!string.Equals(
                functionCall.Name,
                FunctionName,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"The Windows UI Automation adapter cannot execute '{functionCall.Name}'.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                functionCall.Arguments,
                new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException exception)
        {
            throw Failure(
                "The Windows UI Automation arguments were not valid bounded JSON.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(property =>
                    property.Name is not ("operation" or "element_id" or "value"))
                || !root.TryGetProperty("operation", out var operationElement)
                || operationElement.ValueKind != JsonValueKind.String)
            {
                throw Failure(
                    "The Windows UI Automation call has an unsupported shape.");
            }

            var operation = operationElement.GetString() ?? string.Empty;
            if (!Operations.Contains(operation))
            {
                throw Failure(
                    $"Windows UI Automation operation '{operation}' is not supported.");
            }

            var elementId = ReadOptionalString(
                root,
                "element_id",
                96,
                normalizeNullSentinels: true);
            var value = ReadOptionalString(
                root,
                "value",
                4096,
                allowEmpty: true,
                normalizeNullSentinels: operation != "set_value");
            if (operation == "inspect")
            {
                if (elementId is not null || value is not null)
                {
                    throw Failure(
                        "The inspect operation requires null element_id and value fields.");
                }
            }
            else if (string.IsNullOrWhiteSpace(elementId))
            {
                throw Failure(
                    $"The {operation} operation requires an element_id from the latest snapshot.");
            }

            if (operation == "set_value")
            {
                if (value is null)
                {
                    throw Failure(
                        "The set_value operation requires a value string.");
                }
            }
            else if (value is not null)
            {
                throw Failure(
                    $"The {operation} operation requires a null value field.");
            }

            return new WorkspaceAiWindowsUiCall(
                functionCall.CallId,
                operation,
                elementId,
                value);
        }
    }

    private static string? ReadOptionalString(
        JsonElement root,
        string propertyName,
        int maximumLength,
        bool allowEmpty = false,
        bool normalizeNullSentinels = false)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Failure(
                $"The Windows UI Automation call contained an invalid {propertyName}.");
        }

        var value = element.GetString() ?? string.Empty;
        if (normalizeNullSentinels
            && (string.IsNullOrWhiteSpace(value)
                || string.Equals(
                    value,
                    "none",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    value,
                    "null",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (value.Length > maximumLength
            || value.IndexOf('\0') >= 0
            || !allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw Failure(
                $"The Windows UI Automation call contained an invalid {propertyName}.");
        }

        return value;
    }

    private static WorkspaceAiProviderException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}

public sealed partial class WpfWindowComputerControlTarget
    : IWorkspaceAiWindowsUiControlTarget
{
    private const int MaximumWindowsUiElements = 96;
    private const int MaximumWindowsUiDepth = 6;
    private const int MaximumTopLevelWindows = 24;
    private const int MaximumUiTextLength = 160;

    private readonly object _windowsUiGate = new();
    private readonly Dictionary<string, AutomationElement>
        _windowsUiElements = new(StringComparer.Ordinal);
    private long _windowsUiSnapshotSequence;

    public void ResetWindowsUiSnapshot()
    {
        lock (_windowsUiGate)
        {
            _windowsUiElements.Clear();
        }
    }

    public async Task<JsonElement> ExecuteWindowsUiAsync(
        WorkspaceAiWindowsUiCall call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        var hypershellHandle = IntPtr.Zero;
        if (_mode == WorkspaceAiComputerControlMode.Hypershell)
        {
            var handleOperation = _window.Dispatcher.InvokeAsync(
                () => new WindowInteropHelper(_window).Handle,
                DispatcherPriority.Send,
                cancellationToken);
            hypershellHandle = await handleOperation.Task;
        }

        return await Task.Run(
            () => ExecuteWindowsUiCore(
                call,
                hypershellHandle,
                cancellationToken),
            cancellationToken);
    }

    private JsonElement ExecuteWindowsUiCore(
        WorkspaceAiWindowsUiCall call,
        IntPtr hypershellHandle,
        CancellationToken cancellationToken)
    {
        lock (_windowsUiGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actionSucceeded = true;
            var message = "Windows UI Automation state inspected.";
            if (call.Operation != "inspect")
            {
                if (!_windowsUiElements.TryGetValue(
                        call.ElementId ?? string.Empty,
                        out var element))
                {
                    actionSucceeded = false;
                    message =
                        "That element_id is absent or expired. Use an ID from the snapshot returned with this result.";
                }
                else
                {
                    (actionSucceeded, message) = TryExecuteWindowsUiAction(
                        element,
                        call);
                    if (actionSucceeded)
                    {
                        Thread.Sleep(120);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = CaptureWindowsUiSnapshotCore(
                hypershellHandle,
                cancellationToken);
            snapshot.Insert(0, "message", message);
            snapshot.Insert(0, "operation", call.Operation);
            snapshot.Insert(0, "ok", actionSucceeded);
            return JsonSerializer.SerializeToElement(snapshot);
        }
    }

    private (bool Succeeded, string Message) TryExecuteWindowsUiAction(
        AutomationElement element,
        WorkspaceAiWindowsUiCall call)
    {
        try
        {
            switch (call.Operation)
            {
                case "focus":
                    RestoreAndFocus(element);
                    return (true, "Focused the requested Windows control.");
                case "invoke":
                    RequirePattern<InvokePattern>(
                        element,
                        InvokePattern.Pattern,
                        "invoke").Invoke();
                    return (true, "Invoked the requested Windows control.");
                case "set_value":
                    if (ReadBooleanProperty(
                            element,
                            AutomationElement.IsPasswordProperty))
                    {
                        return (
                            false,
                            "Hypershell refuses semantic text injection into password fields.");
                    }

                    var valuePattern = RequirePattern<ValuePattern>(
                        element,
                        ValuePattern.Pattern,
                        "set_value");
                    if (valuePattern.Current.IsReadOnly)
                    {
                        return (false, "The requested field is read-only.");
                    }

                    valuePattern.SetValue(call.Value ?? string.Empty);
                    return (true, "Set the requested field value without reading its prior contents.");
                case "toggle":
                    RequirePattern<TogglePattern>(
                        element,
                        TogglePattern.Pattern,
                        "toggle").Toggle();
                    return (true, "Toggled the requested Windows control.");
                case "expand":
                    RequirePattern<ExpandCollapsePattern>(
                        element,
                        ExpandCollapsePattern.Pattern,
                        "expand").Expand();
                    return (true, "Expanded the requested Windows control.");
                case "collapse":
                    RequirePattern<ExpandCollapsePattern>(
                        element,
                        ExpandCollapsePattern.Pattern,
                        "collapse").Collapse();
                    return (true, "Collapsed the requested Windows control.");
                case "select":
                    RequirePattern<SelectionItemPattern>(
                        element,
                        SelectionItemPattern.Pattern,
                        "select").Select();
                    return (true, "Selected the requested Windows item.");
                case "scroll_into_view":
                    RequirePattern<ScrollItemPattern>(
                        element,
                        ScrollItemPattern.Pattern,
                        "scroll_into_view").ScrollIntoView();
                    return (true, "Scrolled the requested Windows item into view.");
                default:
                    return (
                        false,
                        $"Windows UI operation '{call.Operation}' is not implemented.");
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            return (
                false,
                $"Windows could not complete that semantic action: {exception.Message}");
        }
    }

    private static void RestoreAndFocus(AutomationElement element)
    {
        if (TryGetPattern<WindowPattern>(
                element,
                WindowPattern.Pattern,
                out var windowPattern)
            && windowPattern.Current.WindowVisualState
            == WindowVisualState.Minimized)
        {
            windowPattern.SetWindowVisualState(WindowVisualState.Normal);
        }

        var nativeHandle = ReadIntProperty(
            element,
            AutomationElement.NativeWindowHandleProperty);
        if (nativeHandle != 0)
        {
            _ = SetForegroundWindow(new IntPtr(nativeHandle));
        }

        element.SetFocus();
    }

    private JsonObject CaptureWindowsUiSnapshotCore(
        IntPtr hypershellHandle,
        CancellationToken cancellationToken)
    {
        _windowsUiElements.Clear();
        var snapshotNumber = ++_windowsUiSnapshotSequence;
        var elements = new JsonArray();
        var truncated = false;
        try
        {
            if (_mode == WorkspaceAiComputerControlMode.Hypershell)
            {
                if (hypershellHandle == IntPtr.Zero)
                {
                    return UnavailableSnapshot(
                        snapshotNumber,
                        "Hypershell does not yet have a visible native window handle.");
                }

                var root = AutomationElement.FromHandle(hypershellHandle);
                var rootId = AddWindowsUiElement(
                    root,
                    parentId: null,
                    depth: 0,
                    snapshotNumber,
                    isActiveWindow: true,
                    elements);
                TraverseWindowsUiChildren(
                    root,
                    rootId,
                    depth: 1,
                    snapshotNumber,
                    isActiveWindow: true,
                    elements,
                    ref truncated,
                    cancellationToken);
                return AvailableSnapshot(
                    snapshotNumber,
                    SafeString(() => root.Current.Name),
                    elements,
                    truncated);
            }

            var desktop = AutomationElement.RootElement;
            var foregroundHandle = GetForegroundWindow();
            AutomationElement? foreground = null;
            if (foregroundHandle != IntPtr.Zero)
            {
                try
                {
                    foreground = AutomationElement.FromHandle(
                        foregroundHandle);
                }
                catch (ElementNotAvailableException)
                {
                    foreground = null;
                }
            }

            var walker = TreeWalker.ControlViewWalker;
            var topLevel = walker.GetFirstChild(desktop);
            var windowCount = 0;
            var activeWindowFound = false;
            while (topLevel is not null
                   && windowCount < MaximumTopLevelWindows
                   && elements.Count < MaximumWindowsUiElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nativeHandle = ReadIntProperty(
                    topLevel,
                    AutomationElement.NativeWindowHandleProperty);
                var isActive = foregroundHandle != IntPtr.Zero
                               && nativeHandle == unchecked(
                                   (int)foregroundHandle.ToInt64());
                var topLevelId = AddWindowsUiElement(
                    topLevel,
                    parentId: null,
                    depth: 0,
                    snapshotNumber,
                    isActive,
                    elements,
                    forceInclude: true);
                if (isActive)
                {
                    activeWindowFound = true;
                    TraverseWindowsUiChildren(
                        topLevel,
                        topLevelId,
                        depth: 1,
                        snapshotNumber,
                        isActiveWindow: true,
                        elements,
                        ref truncated,
                        cancellationToken);
                }

                windowCount++;
                topLevel = walker.GetNextSibling(topLevel);
            }

            if (!activeWindowFound
                && foreground is not null
                && elements.Count < MaximumWindowsUiElements)
            {
                var foregroundId = AddWindowsUiElement(
                    foreground,
                    parentId: null,
                    depth: 0,
                    snapshotNumber,
                    isActiveWindow: true,
                    elements,
                    forceInclude: true);
                TraverseWindowsUiChildren(
                    foreground,
                    foregroundId,
                    depth: 1,
                    snapshotNumber,
                    isActiveWindow: true,
                    elements,
                    ref truncated,
                    cancellationToken);
            }

            if (topLevel is not null
                || elements.Count >= MaximumWindowsUiElements)
            {
                truncated = true;
            }

            return AvailableSnapshot(
                snapshotNumber,
                foreground is null
                    ? string.Empty
                    : SafeString(() => foreground.Current.Name),
                elements,
                truncated);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            _windowsUiElements.Clear();
            return UnavailableSnapshot(
                snapshotNumber,
                $"Windows UI Automation is temporarily unavailable: {exception.Message}");
        }
    }

    private void TraverseWindowsUiChildren(
        AutomationElement parent,
        string? parentId,
        int depth,
        long snapshotNumber,
        bool isActiveWindow,
        JsonArray elements,
        ref bool truncated,
        CancellationToken cancellationToken)
    {
        if (depth > MaximumWindowsUiDepth
            || elements.Count >= MaximumWindowsUiElements)
        {
            truncated = true;
            return;
        }

        var walker = TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(parent);
        while (child is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (elements.Count >= MaximumWindowsUiElements)
            {
                truncated = true;
                return;
            }

            var childId = AddWindowsUiElement(
                child,
                parentId,
                depth,
                snapshotNumber,
                isActiveWindow,
                elements);
            TraverseWindowsUiChildren(
                child,
                childId ?? parentId,
                depth + 1,
                snapshotNumber,
                isActiveWindow,
                elements,
                ref truncated,
                cancellationToken);
            child = walker.GetNextSibling(child);
        }
    }

    private string? AddWindowsUiElement(
        AutomationElement element,
        string? parentId,
        int depth,
        long snapshotNumber,
        bool isActiveWindow,
        JsonArray elements,
        bool forceInclude = false)
    {
        var name = SafeString(() => element.Current.Name);
        var automationId = SafeString(
            () => element.Current.AutomationId);
        var className = SafeString(() => element.Current.ClassName);
        var controlType = SafeString(
            () => element.Current.ControlType?.ProgrammaticName
                  ?.Replace("ControlType.", string.Empty,
                      StringComparison.Ordinal));
        var actions = ReadSupportedActions(element);
        if (!forceInclude
            && string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(automationId)
            && actions.Count == 0)
        {
            return null;
        }

        var elementId = $"uia-{snapshotNumber}-{elements.Count + 1}";
        _windowsUiElements[elementId] = element;
        var bounds = ReadBounds(element);
        elements.Add(new JsonObject
        {
            ["element_id"] = elementId,
            ["parent_id"] = parentId,
            ["depth"] = depth,
            ["control_type"] = controlType,
            ["name"] = name,
            ["automation_id"] = automationId,
            ["class_name"] = className,
            ["enabled"] = ReadBooleanProperty(
                element,
                AutomationElement.IsEnabledProperty),
            ["offscreen"] = ReadBooleanProperty(
                element,
                AutomationElement.IsOffscreenProperty),
            ["password"] = ReadBooleanProperty(
                element,
                AutomationElement.IsPasswordProperty),
            ["active_window"] = isActiveWindow && depth == 0,
            ["bounds"] = bounds,
            ["actions"] = new JsonArray(
                actions.Select(action => JsonValue.Create(action)).ToArray())
        });
        return elementId;
    }

    private JsonObject AvailableSnapshot(
        long snapshotNumber,
        string activeWindow,
        JsonArray elements,
        bool truncated) =>
        new()
        {
            ["available"] = true,
            ["host_marker"] = WorkspaceAiWindowsUiFunctionTool.SnapshotMarker,
            ["scope"] = _mode.ToString(),
            ["snapshot_id"] = $"uia-{snapshotNumber}",
            ["ids_valid_until_next_result"] = true,
            ["active_window"] = activeWindow,
            ["element_count"] = elements.Count,
            ["truncated"] = truncated,
            ["elements"] = elements
        };

    private JsonObject UnavailableSnapshot(
        long snapshotNumber,
        string message) =>
        new()
        {
            ["available"] = false,
            ["host_marker"] = WorkspaceAiWindowsUiFunctionTool.SnapshotMarker,
            ["scope"] = _mode.ToString(),
            ["snapshot_id"] = $"uia-{snapshotNumber}",
            ["ids_valid_until_next_result"] = true,
            ["message"] = message,
            ["element_count"] = 0,
            ["truncated"] = false,
            ["elements"] = new JsonArray()
        };

    private static IReadOnlyList<string> ReadSupportedActions(
        AutomationElement element)
    {
        var actions = new List<string>();
        try
        {
            if (ReadBooleanProperty(
                    element,
                    AutomationElement.IsEnabledProperty)
                && (ReadBooleanProperty(
                        element,
                        AutomationElement.IsKeyboardFocusableProperty)
                    || ReadIntProperty(
                        element,
                        AutomationElement.NativeWindowHandleProperty) != 0))
            {
                actions.Add("focus");
            }

            AddPatternAction<InvokePattern>(
                element,
                InvokePattern.Pattern,
                "invoke",
                actions);
            if (!ReadBooleanProperty(
                    element,
                    AutomationElement.IsPasswordProperty)
                && TryGetPattern<ValuePattern>(
                    element,
                    ValuePattern.Pattern,
                    out var valuePattern)
                && !valuePattern.Current.IsReadOnly)
            {
                actions.Add("set_value");
            }

            AddPatternAction<TogglePattern>(
                element,
                TogglePattern.Pattern,
                "toggle",
                actions);
            if (TryGetPattern<ExpandCollapsePattern>(
                    element,
                    ExpandCollapsePattern.Pattern,
                    out var expandPattern)
                && expandPattern.Current.ExpandCollapseState
                != ExpandCollapseState.LeafNode)
            {
                actions.Add("expand");
                actions.Add("collapse");
            }

            AddPatternAction<SelectionItemPattern>(
                element,
                SelectionItemPattern.Pattern,
                "select",
                actions);
            AddPatternAction<ScrollItemPattern>(
                element,
                ScrollItemPattern.Pattern,
                "scroll_into_view",
                actions);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            // A single stale provider must not invalidate the entire bounded
            // snapshot. The controls already discovered remain actionable.
        }

        return actions;
    }

    private static void AddPatternAction<TPattern>(
        AutomationElement element,
        AutomationPattern pattern,
        string action,
        ICollection<string> actions)
        where TPattern : class
    {
        if (TryGetPattern<TPattern>(element, pattern, out _))
        {
            actions.Add(action);
        }
    }

    private static TPattern RequirePattern<TPattern>(
        AutomationElement element,
        AutomationPattern pattern,
        string operation)
        where TPattern : class
    {
        if (TryGetPattern<TPattern>(element, pattern, out var result))
        {
            return result;
        }

        throw new InvalidOperationException(
            $"The requested control does not support {operation}.");
    }

    private static bool TryGetPattern<TPattern>(
        AutomationElement element,
        AutomationPattern pattern,
        out TPattern result)
        where TPattern : class
    {
        try
        {
            if (element.TryGetCurrentPattern(pattern, out var value)
                && value is TPattern typed)
            {
                result = typed;
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        result = null!;
        return false;
    }

    private static bool ReadBooleanProperty(
        AutomationElement element,
        AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(
                       property,
                       ignoreDefaultValue: true)
                   is bool value
                   && value;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            return false;
        }
    }

    private static int ReadIntProperty(
        AutomationElement element,
        AutomationProperty property)
    {
        try
        {
            return element.GetCurrentPropertyValue(
                       property,
                       ignoreDefaultValue: true)
                   is int value
                ? value
                : 0;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            return 0;
        }
    }

    private static JsonObject ReadBounds(AutomationElement element)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty
                || !double.IsFinite(bounds.X)
                || !double.IsFinite(bounds.Y)
                || !double.IsFinite(bounds.Width)
                || !double.IsFinite(bounds.Height))
            {
                return new JsonObject();
            }

            return new JsonObject
            {
                ["x"] = Math.Round(bounds.X, 1),
                ["y"] = Math.Round(bounds.Y, 1),
                ["width"] = Math.Round(bounds.Width, 1),
                ["height"] = Math.Round(bounds.Height, 1)
            };
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            return new JsonObject();
        }
    }

    private static string SafeString(Func<string?> read)
    {
        try
        {
            var value = (read() ?? string.Empty).Trim();
            return value.Length <= MaximumUiTextLength
                ? value
                : value[..MaximumUiTextLength];
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
                                          or InvalidOperationException
                                          or COMException)
        {
            return string.Empty;
        }
    }
}
