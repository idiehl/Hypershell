using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hypershell.Services;

public interface IWorkspaceAiComputerControlTarget
{
    WorkspaceAiComputerControlMode Mode { get; }

    Task ExecuteActionsAsync(
        IReadOnlyList<WorkspaceAiComputerAction> actions,
        CancellationToken cancellationToken = default);

    Task<byte[]> CaptureScreenshotAsync(
        CancellationToken cancellationToken = default);
}

public static class WorkspaceAiComputerFunctionTool
{
    public const string FunctionName = "control_computer";
    public const string ScreenshotMarker =
        "[HYPERSHELL_COMPUTER_SCREENSHOT]";
    public const int MaximumActions = 16;

    private static readonly HashSet<string> ActionTypes =
        new(StringComparer.Ordinal)
        {
            "click",
            "double_click",
            "scroll",
            "type",
            "wait",
            "keypress",
            "drag",
            "move",
            "screenshot"
        };

    private static readonly HashSet<string> ActionProperties =
        new(StringComparer.Ordinal)
        {
            "type",
            "x",
            "y",
            "button",
            "scroll_x",
            "scroll_y",
            "text",
            "keys",
            "path"
        };

    private static readonly JsonObject Definition =
        JsonNode.Parse(
            """
            {
              "type": "function",
              "name": "control_computer",
              "description": "Capture and control the Context-authorized Hypershell window or full Windows desktop. Start with a single screenshot action. After the host attaches that screenshot, use its pixel coordinates for mouse actions. Actions execute in order and the host returns a new screenshot after every batch. Use screenshot to inspect without input; move to reposition the cursor; click or double_click with x/y and button; scroll with x/y and scroll_x/scroll_y; type with text; keypress with keys; drag with a path; wait for the UI to settle. Use [] or null for fields an action does not use.",
              "strict": true,
              "parameters": {
                "type": "object",
                "properties": {
                  "actions": {
                    "type": "array",
                    "minItems": 1,
                    "maxItems": 16,
                    "items": {
                      "type": "object",
                      "properties": {
                        "type": { "type": "string", "enum": ["screenshot", "move", "click", "double_click", "scroll", "type", "keypress", "drag", "wait"] },
                        "x": { "type": ["number", "null"] },
                        "y": { "type": ["number", "null"] },
                        "button": { "type": ["string", "null"], "enum": ["left", "right", "middle", "wheel", null] },
                        "scroll_x": { "type": ["number", "null"] },
                        "scroll_y": { "type": ["number", "null"] },
                        "text": { "type": ["string", "null"], "maxLength": 4096 },
                        "keys": { "type": "array", "maxItems": 8, "items": { "type": "string", "minLength": 1, "maxLength": 32 } },
                        "path": {
                          "type": "array",
                          "maxItems": 128,
                          "items": {
                            "type": "object",
                            "properties": {
                              "x": { "type": "number" },
                              "y": { "type": "number" }
                            },
                            "required": ["x", "y"],
                            "additionalProperties": false
                          }
                        }
                      },
                      "required": ["type", "x", "y", "button", "scroll_x", "scroll_y", "text", "keys", "path"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["actions"],
                "additionalProperties": false
              }
            }
            """)?.AsObject()
        ?? throw new InvalidOperationException(
            "The local computer-control tool definition is malformed.");

    public static JsonObject CreateDefinition() =>
        Definition.DeepClone().AsObject();

    public static WorkspaceAiComputerCall Parse(
        WorkspaceAiFunctionCall functionCall)
    {
        ArgumentNullException.ThrowIfNull(functionCall);
        if (!string.Equals(
                functionCall.Name,
                FunctionName,
                StringComparison.Ordinal))
        {
            throw Failure(
                $"The local computer-control adapter cannot execute '{functionCall.Name}'.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                functionCall.Arguments,
                new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException exception)
        {
            throw Failure(
                "The local computer-control arguments were not valid bounded JSON.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(property =>
                    !string.Equals(
                        property.Name,
                        "actions",
                        StringComparison.Ordinal))
                || !root.TryGetProperty("actions", out var actions)
                || actions.ValueKind != JsonValueKind.Array
                || actions.GetArrayLength() is < 1 or > MaximumActions)
            {
                throw Failure(
                    $"The local computer-control call requires between 1 and {MaximumActions} actions.");
            }

            var parsed = new List<WorkspaceAiComputerAction>();
            foreach (var action in actions.EnumerateArray())
            {
                ValidateAction(action);
                parsed.Add(new WorkspaceAiComputerAction(action.Clone()));
            }

            return new WorkspaceAiComputerCall(functionCall.CallId, parsed);
        }
    }

    private static void ValidateAction(JsonElement action)
    {
        if (action.ValueKind != JsonValueKind.Object
            || action.EnumerateObject().Any(property =>
                !ActionProperties.Contains(property.Name))
            || !action.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !ActionTypes.Contains(typeElement.GetString() ?? string.Empty))
        {
            throw Failure(
                "The local computer-control call contained an unsupported action.");
        }

        ValidateOptionalNumber(action, "x");
        ValidateOptionalNumber(action, "y");
        ValidateOptionalNumber(action, "scroll_x");
        ValidateOptionalNumber(action, "scroll_y");
        ValidateKeys(action);
        ValidatePath(action);

        var type = typeElement.GetString()!;
        if (type is "move" or "click" or "double_click" or "scroll")
        {
            RequireNumber(action, "x");
            RequireNumber(action, "y");
        }

        if (type is "click" or "double_click"
            && action.TryGetProperty("button", out var button)
            && button.ValueKind is not (JsonValueKind.String
                or JsonValueKind.Null))
        {
            throw Failure(
                "The local computer-control call contained an invalid mouse button.");
        }

        if (type == "type"
            && (!action.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String
                || (text.GetString() ?? string.Empty).Length > 4096))
        {
            throw Failure(
                "The local computer-control type action contained invalid text.");
        }

        if (type == "keypress"
            && (!action.TryGetProperty("keys", out var keypressKeys)
                || keypressKeys.ValueKind != JsonValueKind.Array
                || keypressKeys.GetArrayLength() is < 1 or > 8))
        {
            throw Failure(
                "The local computer-control keypress action requires a bounded key list.");
        }

        if (type == "drag"
            && (!action.TryGetProperty("path", out var dragPath)
                || dragPath.ValueKind != JsonValueKind.Array
                || dragPath.GetArrayLength() is < 2 or > 128))
        {
            throw Failure(
                "The local computer-control drag action requires a bounded path.");
        }
    }

    private static void ValidateOptionalNumber(
        JsonElement action,
        string propertyName)
    {
        if (!action.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw Failure(
                $"The local computer-control call contained invalid {propertyName}.");
        }
    }

    private static void RequireNumber(
        JsonElement action,
        string propertyName)
    {
        if (!action.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw Failure(
                $"The local computer-control action requires a finite {propertyName} coordinate.");
        }
    }

    private static void ValidateKeys(JsonElement action)
    {
        if (!action.TryGetProperty("keys", out var keys))
        {
            return;
        }

        if (keys.ValueKind != JsonValueKind.Array
            || keys.GetArrayLength() > 8
            || keys.EnumerateArray().Any(key =>
                key.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(key.GetString())
                || key.GetString()!.Length > 32))
        {
            throw Failure(
                "The local computer-control call contained an invalid key list.");
        }
    }

    private static void ValidatePath(JsonElement action)
    {
        if (!action.TryGetProperty("path", out var path))
        {
            return;
        }

        if (path.ValueKind != JsonValueKind.Array
            || path.GetArrayLength() > 128)
        {
            throw Failure(
                "The local computer-control call contained an invalid drag path.");
        }

        foreach (var point in path.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object
                || point.EnumerateObject().Any(property =>
                    property.Name is not ("x" or "y")))
            {
                throw Failure(
                    "The local computer-control call contained an invalid drag point.");
            }

            RequireNumber(point, "x");
            RequireNumber(point, "y");
        }
    }

    private static WorkspaceAiProviderException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}

public sealed record WorkspaceAiComputerFunctionResult(
    string FunctionOutput,
    JsonElement ScreenshotInput);

public sealed class WorkspaceAiComputerControlService : IDisposable
{
    public const int MaximumScreenshotBytes = 3 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly WorkspaceAiContextService _contextService;
    private readonly Dictionary<WorkspaceAiComputerControlMode,
        IWorkspaceAiComputerControlTarget> _targets = [];
    private CancellationTokenSource _stopCancellation = new();
    private bool _isActive;
    private bool _isStopped;
    private bool _isDisposed;

    public WorkspaceAiComputerControlService(
        WorkspaceAiContextService contextService)
    {
        _contextService = contextService;
    }

    public event EventHandler? StateChanged;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return !_isDisposed && _targets.Count > 0;
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _isActive;
            }
        }
    }

    public bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _isStopped;
            }
        }
    }

    public void Attach(IWorkspaceAiComputerControlTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (target.Mode == WorkspaceAiComputerControlMode.Off)
            {
                throw new ArgumentException(
                    "A computer-control target must have an active scope.",
                    nameof(target));
            }

            _targets[target.Mode] = target;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Detach(IWorkspaceAiComputerControlTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            if (!_targets.TryGetValue(target.Mode, out var attached)
                || !ReferenceEquals(attached, target))
            {
                return;
            }

            _targets.Remove(target.Mode);
            _stopCancellation.Cancel();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetForNewRequest()
    {
        IWorkspaceAiWindowsUiControlTarget[] windowsUiTargets;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isActive)
            {
                throw new InvalidOperationException(
                    "Wait for the active computer-control batch to finish.");
            }

            _stopCancellation.Dispose();
            _stopCancellation = new CancellationTokenSource();
            _isStopped = false;
            windowsUiTargets = _targets.Values
                .OfType<IWorkspaceAiWindowsUiControlTarget>()
                .Distinct()
                .ToArray();
        }

        foreach (var target in windowsUiTargets)
        {
            target.ResetWindowsUiSnapshot();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isStopped = true;
            _stopCancellation.Cancel();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<JsonElement> ExecuteAsync(
        string contextId,
        WorkspaceAiComputerCall call,
        CancellationToken cancellationToken = default)
    {
        var capture = await ExecuteAndCaptureAsync(
            contextId,
            call,
            cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            type = "computer_call_output",
            call_id = call.CallId,
            output = new
            {
                type = "computer_screenshot",
                image_url = capture.ImageUrl,
                detail = "original"
            }
        });
    }

    public async Task<WorkspaceAiComputerFunctionResult> ExecuteFunctionAsync(
        string contextId,
        WorkspaceAiFunctionCall functionCall,
        CancellationToken cancellationToken = default)
    {
        var call = WorkspaceAiComputerFunctionTool.Parse(functionCall);
        var capture = await ExecuteAndCaptureAsync(
            contextId,
            call,
            cancellationToken);
        var functionOutput = JsonSerializer.Serialize(new
        {
            ok = true,
            action_count = call.Actions.Count,
            screenshot_width = capture.Width,
            screenshot_height = capture.Height,
            coordinate_origin = "top-left",
            message =
                "The host completed the computer actions. The resulting screenshot is attached in the following host message."
        });
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "input_text",
                ["text"] =
                    $"{WorkspaceAiComputerFunctionTool.ScreenshotMarker} The host captured the Context-authorized computer surface after the requested actions. Coordinates use a top-left origin and are bounded to {capture.Width}x{capture.Height} pixels. Inspect this image before choosing subsequent cursor coordinates."
            },
            new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = capture.ImageUrl,
                ["detail"] = "original"
            }
        };
        var screenshotInput = JsonSerializer.SerializeToElement(
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = content
            });
        return new WorkspaceAiComputerFunctionResult(
            functionOutput,
            screenshotInput);
    }

    public async Task<string> ExecuteWindowsUiFunctionAsync(
        string contextId,
        WorkspaceAiFunctionCall functionCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        var call = WorkspaceAiWindowsUiFunctionTool.Parse(functionCall);

        IWorkspaceAiWindowsUiControlTarget target;
        CancellationToken stopToken;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var context = _contextService.GetContext(contextId)
                          ?? throw new WorkspaceAiProviderException(
                              "The selected AI context is no longer available.");
            if (context.ComputerControl == WorkspaceAiComputerControlMode.Off)
            {
                throw new WorkspaceAiProviderException(
                    "The selected Context does not allow Windows UI control.");
            }

            if (context.AskBeforeChanges)
            {
                throw new WorkspaceAiProviderException(
                    "Autonomous Windows UI control requires a Context that does not ask before each change.");
            }

            if (!_targets.TryGetValue(
                    context.ComputerControl,
                    out var selectedTarget))
            {
                throw new WorkspaceAiProviderException(
                    $"The {context.ComputerControl} computer-control target is not attached.");
            }

            target = selectedTarget
                     as IWorkspaceAiWindowsUiControlTarget
                     ?? throw new WorkspaceAiProviderException(
                         "The attached computer-control target does not expose Windows UI Automation.");
            if (_isStopped)
            {
                throw new WorkspaceAiProviderException(
                    "Computer control was stopped by the user.");
            }

            _isActive = true;
            stopToken = _stopCancellation.Token;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        using var linkedCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, stopToken);
        try
        {
            var result = await target.ExecuteWindowsUiAsync(
                call,
                linkedCancellation.Token);
            return result.GetRawText();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && stopToken.IsCancellationRequested)
        {
            throw new WorkspaceAiProviderException(
                "Computer control was stopped by the user.");
        }
        finally
        {
            lock (_gate)
            {
                _isActive = false;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<WorkspaceAiComputerCapture> ExecuteAndCaptureAsync(
        string contextId,
        WorkspaceAiComputerCall call,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentNullException.ThrowIfNull(call);

        IWorkspaceAiComputerControlTarget target;
        CancellationToken stopToken;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var context = _contextService.GetContext(contextId)
                          ?? throw new WorkspaceAiProviderException(
                              "The selected AI context is no longer available.");
            if (context.ComputerControl == WorkspaceAiComputerControlMode.Off)
            {
                throw new WorkspaceAiProviderException(
                    "The selected Context does not allow computer control.");
            }

            if (context.AskBeforeChanges)
            {
                throw new WorkspaceAiProviderException(
                    "Autonomous computer control requires a Context that does not ask before each change.");
            }

            if (!_targets.TryGetValue(
                    context.ComputerControl,
                    out var selectedTarget))
            {
                throw new WorkspaceAiProviderException(
                    $"The {context.ComputerControl} computer-control target is not attached.");
            }

            target = selectedTarget;
            if (_isStopped)
            {
                throw new WorkspaceAiProviderException(
                    "Computer control was stopped by the user.");
            }

            _isActive = true;
            stopToken = _stopCancellation.Token;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        using var linkedCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, stopToken);
        byte[]? screenshot = null;
        try
        {
            await target.ExecuteActionsAsync(
                call.Actions,
                linkedCancellation.Token);
            screenshot = await target.CaptureScreenshotAsync(
                linkedCancellation.Token);
            if (screenshot.Length is < 1 or > MaximumScreenshotBytes)
            {
                throw new WorkspaceAiProviderException(
                    "The computer-control screenshot exceeded the host transport allocation.");
            }

            var (width, height) = ReadPngDimensions(screenshot);
            return new WorkspaceAiComputerCapture(
                $"data:image/png;base64,{Convert.ToBase64String(screenshot)}",
                width,
                height);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && stopToken.IsCancellationRequested)
        {
            throw new WorkspaceAiProviderException(
                "Computer control was stopped by the user.");
        }
        finally
        {
            if (screenshot is not null)
            {
                Array.Clear(screenshot);
            }

            lock (_gate)
            {
                _isActive = false;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] payload)
    {
        ReadOnlySpan<byte> pngSignature =
            [137, 80, 78, 71, 13, 10, 26, 10];
        if (payload.Length < 24
            || !payload.AsSpan(0, pngSignature.Length)
                .SequenceEqual(pngSignature))
        {
            throw new WorkspaceAiProviderException(
                "The computer-control target returned an invalid PNG screenshot.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(
            payload.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(
            payload.AsSpan(20, 4));
        if (width < 1 || height < 1)
        {
            throw new WorkspaceAiProviderException(
                "The computer-control target returned invalid screenshot dimensions.");
        }

        return (width, height);
    }

    private sealed record WorkspaceAiComputerCapture(
        string ImageUrl,
        int Width,
        int Height);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _targets.Clear();
            _stopCancellation.Cancel();
            _stopCancellation.Dispose();
            _isActive = false;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed partial class WpfWindowComputerControlTarget
    : IWorkspaceAiComputerControlTarget
{
    private const int MaximumCaptureWidth = 1600;
    private const int MaximumCaptureHeight = 1000;
    private const int VirtualScreenX = 76;
    private const int VirtualScreenY = 77;
    private const int VirtualScreenWidth = 78;
    private const int VirtualScreenHeight = 79;
    private const int StretchModeHalftone = 4;
    private const uint RasterSourceCopy = 0x00CC0020;
    private const uint RasterCaptureLayeredWindows = 0x40000000;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHorizontalWheel = 0x01000;
    private const int WheelDelta = 120;

    private readonly Window _window;
    private readonly WorkspaceAiComputerControlMode _mode;
    private int _lastCaptureWidth;
    private int _lastCaptureHeight;
    private int _lastCaptureOriginX;
    private int _lastCaptureOriginY;
    private int _lastCaptureSourceWidth;
    private int _lastCaptureSourceHeight;

    public WpfWindowComputerControlTarget(
        Window window,
        WorkspaceAiComputerControlMode mode =
            WorkspaceAiComputerControlMode.Hypershell)
    {
        if (mode is not (WorkspaceAiComputerControlMode.Hypershell
            or WorkspaceAiComputerControlMode.Desktop))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _window = window;
        _mode = mode;
    }

    public WorkspaceAiComputerControlMode Mode => _mode;

    public async Task ExecuteActionsAsync(
        IReadOnlyList<WorkspaceAiComputerAction> actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.Type == "wait")
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
                continue;
            }

            if (action.Type == "screenshot")
            {
                continue;
            }

            var operation = _window.Dispatcher.InvokeAsync(
                () => ExecuteAction(action.Payload),
                DispatcherPriority.Input,
                cancellationToken);
            await operation.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
        }
    }

    public async Task<byte[]> CaptureScreenshotAsync(
        CancellationToken cancellationToken = default)
    {
        var operation = _window.Dispatcher.InvokeAsync(
            CaptureScreenshotCore,
            DispatcherPriority.Render,
            cancellationToken);
        return await operation.Task;
    }

    private byte[] CaptureScreenshotCore()
    {
        if (_mode == WorkspaceAiComputerControlMode.Desktop)
        {
            return CaptureDesktopScreenshotCore();
        }

        if (!_window.IsVisible
            || _window.WindowState == WindowState.Minimized
            || _window.ActualWidth < 1
            || _window.ActualHeight < 1)
        {
            throw new WorkspaceAiProviderException(
                "Hypershell must be visible and restored for computer control.");
        }

        EnsureWindowCanReceiveInput();
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero
            || !GetWindowRect(handle, out var bounds))
        {
            throw new WorkspaceAiProviderException(
                "Windows could not read the visible Hypershell bounds.");
        }

        var nativeWidth = bounds.Right - bounds.Left;
        var nativeHeight = bounds.Bottom - bounds.Top;
        if (nativeWidth < 1 || nativeHeight < 1)
        {
            throw new WorkspaceAiProviderException(
                "The visible Hypershell window has invalid bounds.");
        }

        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumCaptureWidth / nativeWidth,
                (double)MaximumCaptureHeight / nativeHeight));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var pixelWidth = Math.Max(1, (int)Math.Round(nativeWidth * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(nativeHeight * scale));
            var payload = CaptureDesktopToPng(
                bounds.Left,
                bounds.Top,
                nativeWidth,
                nativeHeight,
                pixelWidth,
                pixelHeight);
            if (payload.Length <=
                WorkspaceAiComputerControlService.MaximumScreenshotBytes)
            {
                _lastCaptureWidth = pixelWidth;
                _lastCaptureHeight = pixelHeight;
                _lastCaptureOriginX = bounds.Left;
                _lastCaptureOriginY = bounds.Top;
                _lastCaptureSourceWidth = nativeWidth;
                _lastCaptureSourceHeight = nativeHeight;
                return payload;
            }

            Array.Clear(payload);
            scale *= 0.78d;
        }

        throw new WorkspaceAiProviderException(
            "Hypershell could not create a bounded computer-control screenshot.");
    }

    private byte[] CaptureDesktopScreenshotCore()
    {
        var originX = GetSystemMetrics(VirtualScreenX);
        var originY = GetSystemMetrics(VirtualScreenY);
        var sourceWidth = GetSystemMetrics(VirtualScreenWidth);
        var sourceHeight = GetSystemMetrics(VirtualScreenHeight);
        if (sourceWidth < 1 || sourceHeight < 1)
        {
            throw new WorkspaceAiProviderException(
                "Windows did not expose an interactive desktop to capture.");
        }

        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumCaptureWidth / sourceWidth,
                (double)MaximumCaptureHeight / sourceHeight));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var pixelWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var payload = CaptureDesktopToPng(
                originX,
                originY,
                sourceWidth,
                sourceHeight,
                pixelWidth,
                pixelHeight);
            if (payload.Length <=
                WorkspaceAiComputerControlService.MaximumScreenshotBytes)
            {
                _lastCaptureWidth = pixelWidth;
                _lastCaptureHeight = pixelHeight;
                _lastCaptureOriginX = originX;
                _lastCaptureOriginY = originY;
                _lastCaptureSourceWidth = sourceWidth;
                _lastCaptureSourceHeight = sourceHeight;
                return payload;
            }

            Array.Clear(payload);
            scale *= 0.78d;
        }

        throw new WorkspaceAiProviderException(
            "Windows could not create a bounded desktop screenshot.");
    }

    private static byte[] CaptureDesktopToPng(
        int originX,
        int originY,
        int sourceWidth,
        int sourceHeight,
        int pixelWidth,
        int pixelHeight)
    {
        var desktopDc = GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            throw new WorkspaceAiProviderException(
                "Windows denied access to the desktop display surface.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(desktopDc);
            bitmap = CreateCompatibleBitmap(desktopDc, pixelWidth, pixelHeight);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new WorkspaceAiProviderException(
                    "Windows could not allocate the desktop capture surface.");
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            _ = SetStretchBltMode(memoryDc, StretchModeHalftone);
            if (!StretchBlt(
                    memoryDc,
                    0,
                    0,
                    pixelWidth,
                    pixelHeight,
                    desktopDc,
                    originX,
                    originY,
                    sourceWidth,
                    sourceHeight,
                    RasterSourceCopy | RasterCaptureLayeredWindows))
            {
                throw new WorkspaceAiProviderException(
                    "Windows rejected the desktop screenshot request.");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    private void ExecuteAction(JsonElement action)
    {
        EnsureWindowCanReceiveInput();
        var type = RequiredString(action, "type", 32);
        switch (type)
        {
            case "move":
                WithMouseModifiers(action, () => MoveCursor(action));
                break;
            case "click":
                WithMouseModifiers(action, () =>
                {
                    MoveCursor(action);
                    Click(ReadButton(action), doubleClick: false);
                });
                break;
            case "double_click":
                WithMouseModifiers(action, () =>
                {
                    MoveCursor(action);
                    Click(ReadButton(action), doubleClick: true);
                });
                break;
            case "scroll":
                WithMouseModifiers(action, () => Scroll(action));
                break;
            case "drag":
                WithMouseModifiers(action, () => Drag(action));
                break;
            case "type":
                TypeText(RequiredString(action, "text", 4096, true));
                break;
            case "keypress":
                Keypress(ReadKeys(action, required: true));
                break;
            default:
                throw new WorkspaceAiProviderException(
                    $"Computer action '{type}' is not implemented by the Windows host.");
        }
    }

    private void EnsureWindowCanReceiveInput()
    {
        if (_mode == WorkspaceAiComputerControlMode.Desktop)
        {
            return;
        }

        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized)
        {
            throw new WorkspaceAiProviderException(
                "Hypershell must be visible and restored for computer control.");
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero
            || GetForegroundWindow() != handle
            && !SetForegroundWindow(handle))
        {
            throw new WorkspaceAiProviderException(
                "Hypershell could not safely acquire keyboard focus for computer control.");
        }
    }

    private void MoveCursor(JsonElement action)
    {
        var point = ReadScreenPoint(action);
        if (!SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)))
        {
            throw new WorkspaceAiProviderException(
                "Windows rejected the requested cursor movement.");
        }
    }

    private Point ReadScreenPoint(JsonElement action)
    {
        if (_lastCaptureWidth < 1 || _lastCaptureHeight < 1)
        {
            throw new WorkspaceAiProviderException(
                "Computer control requires a Hypershell screenshot before coordinate actions.");
        }

        var x = RequiredNumber(action, "x");
        var y = RequiredNumber(action, "y");
        if (x < 0 || y < 0
            || x >= _lastCaptureWidth
            || y >= _lastCaptureHeight)
        {
            throw new WorkspaceAiProviderException(
                _mode == WorkspaceAiComputerControlMode.Desktop
                    ? "The requested cursor coordinate is outside the captured Windows desktop."
                    : "The requested cursor coordinate is outside the captured Hypershell window.");
        }

        return new Point(
            _lastCaptureOriginX
            + x * _lastCaptureSourceWidth / _lastCaptureWidth,
            _lastCaptureOriginY
            + y * _lastCaptureSourceHeight / _lastCaptureHeight);
    }

    private void Scroll(JsonElement action)
    {
        MoveCursor(action);
        var horizontal = OptionalNumber(action, "scroll_x");
        var vertical = OptionalNumber(action, "scroll_y");
        if (Math.Abs(vertical) > double.Epsilon)
        {
            SendMouse(
                MouseWheel,
                unchecked((uint)ToWheelDelta(-vertical)));
        }

        if (Math.Abs(horizontal) > double.Epsilon)
        {
            SendMouse(
                MouseHorizontalWheel,
                unchecked((uint)ToWheelDelta(horizontal)));
        }
    }

    private void Drag(JsonElement action)
    {
        if (!action.TryGetProperty("path", out var path)
            || path.ValueKind != JsonValueKind.Array
            || path.GetArrayLength() is < 2 or > 128)
        {
            throw new WorkspaceAiProviderException(
                "A drag action requires a bounded path with at least two points.");
        }

        var points = path.EnumerateArray()
            .Select(ReadPathPoint)
            .ToList();
        SetCursorPos((int)Math.Round(points[0].X), (int)Math.Round(points[0].Y));
        SendMouse(MouseLeftDown);
        try
        {
            foreach (var point in points.Skip(1))
            {
                SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y));
            }
        }
        finally
        {
            SendMouse(MouseLeftUp);
        }
    }

    private Point ReadPathPoint(JsonElement value)
    {
        double x;
        double y;
        if (value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() >= 2)
        {
            x = value[0].GetDouble();
            y = value[1].GetDouble();
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            x = RequiredNumber(value, "x");
            y = RequiredNumber(value, "y");
        }
        else
        {
            throw new WorkspaceAiProviderException(
                "A drag path contained an invalid point.");
        }

        using var pointDocument = JsonDocument.Parse(
            $"{{\"x\":{x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        return ReadScreenPoint(pointDocument.RootElement);
    }

    private void WithMouseModifiers(JsonElement action, Action operation)
    {
        var keys = ReadKeys(action, required: false);
        if (keys.Any(key => !IsModifierKey(key)))
        {
            throw new WorkspaceAiProviderException(
                "Mouse action modifiers may contain only CTRL, SHIFT, ALT, or META.");
        }

        var virtualKeys = keys.Select(MapVirtualKey).ToList();
        foreach (var key in virtualKeys)
        {
            SendVirtualKey(key, keyUp: false);
        }

        try
        {
            operation();
        }
        finally
        {
            foreach (var key in virtualKeys.AsEnumerable().Reverse())
            {
                SendVirtualKey(key, keyUp: true);
            }
        }
    }

    private static void Click(string button, bool doubleClick)
    {
        var (down, up) = button switch
        {
            "left" => (MouseLeftDown, MouseLeftUp),
            "right" => (MouseRightDown, MouseRightUp),
            "wheel" or "middle" => (MouseMiddleDown, MouseMiddleUp),
            _ => throw new WorkspaceAiProviderException(
                $"Mouse button '{button}' is not supported.")
        };
        var count = doubleClick ? 2 : 1;
        for (var index = 0; index < count; index++)
        {
            SendMouse(down);
            SendMouse(up);
        }
    }

    private static string ReadButton(JsonElement action) =>
        action.TryGetProperty("button", out var button)
        && button.ValueKind == JsonValueKind.String
            ? (button.GetString() ?? "left").ToLowerInvariant()
            : "left";

    private static void TypeText(string text)
    {
        foreach (var character in text)
        {
            SendUnicode(character, keyUp: false);
            SendUnicode(character, keyUp: true);
        }
    }

    private static void Keypress(IReadOnlyList<string> keys)
    {
        var modifiers = keys.Where(IsModifierKey).Select(MapVirtualKey).ToList();
        var regularKeys = keys.Where(key => !IsModifierKey(key))
            .Select(MapVirtualKey)
            .ToList();
        foreach (var key in modifiers)
        {
            SendVirtualKey(key, keyUp: false);
        }

        try
        {
            foreach (var key in regularKeys)
            {
                SendVirtualKey(key, keyUp: false);
                SendVirtualKey(key, keyUp: true);
            }
        }
        finally
        {
            foreach (var key in modifiers.AsEnumerable().Reverse())
            {
                SendVirtualKey(key, keyUp: true);
            }
        }
    }

    private static IReadOnlyList<string> ReadKeys(
        JsonElement action,
        bool required)
    {
        if (!action.TryGetProperty("keys", out var keys))
        {
            if (required)
            {
                throw new WorkspaceAiProviderException(
                    "The keypress action omitted its keys.");
            }

            return [];
        }

        if (keys.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                throw new WorkspaceAiProviderException(
                    "The keypress action omitted its keys.");
            }

            return [];
        }

        if (keys.ValueKind != JsonValueKind.Array)
        {
            throw new WorkspaceAiProviderException(
                "The computer action contained an invalid key list.");
        }

        var parsedKeys = new List<string>();
        foreach (var key in keys.EnumerateArray())
        {
            if (key.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(key.GetString()))
            {
                throw new WorkspaceAiProviderException(
                    "The computer action contained an invalid key list.");
            }

            parsedKeys.Add(key.GetString()!.Trim().ToUpperInvariant());
        }

        // Mouse actions use an optional keys array for held modifiers. OpenAI
        // may emit no property, null, or an empty array when none are needed.
        if (required && parsedKeys.Count == 0)
        {
            throw new WorkspaceAiProviderException(
                "The keypress action omitted its keys.");
        }

        return parsedKeys;
    }

    private static bool IsModifierKey(string key) =>
        key is "CTRL" or "CONTROL" or "SHIFT" or "ALT" or "OPTION"
            or "META" or "CMD" or "COMMAND" or "WIN" or "WINDOWS";

    private static ushort MapVirtualKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            return char.ToUpperInvariant(key[0]);
        }

        return key switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" or "OPTION" => 0x12,
            "META" or "CMD" or "COMMAND" or "WIN" or "WINDOWS" => 0x5B,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "SPACE" => 0x20,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "UP" or "ARROWUP" => 0x26,
            "DOWN" or "ARROWDOWN" => 0x28,
            "LEFT" or "ARROWLEFT" => 0x25,
            "RIGHT" or "ARROWRIGHT" => 0x27,
            _ when key.Length is 2 or 3
                   && key[0] == 'F'
                   && int.TryParse(key[1..], out var function)
                   && function is >= 1 and <= 12 =>
                (ushort)(0x70 + function - 1),
            _ => throw new WorkspaceAiProviderException(
                $"Keyboard key '{key}' is not supported by the Windows host.")
        };
    }

    private static string RequiredString(
        JsonElement value,
        string property,
        int maximumLength,
        bool allowEmpty = false)
    {
        if (!value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new WorkspaceAiProviderException(
                $"The computer action omitted {property}.");
        }

        var text = element.GetString() ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text))
            || text.Length > maximumLength
            || text.IndexOf('\0') >= 0)
        {
            throw new WorkspaceAiProviderException(
                $"The computer action contained invalid {property}.");
        }

        return text;
    }

    private static double RequiredNumber(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw new WorkspaceAiProviderException(
                $"The computer action contained invalid {property}.");
        }

        return number;
    }

    private static double OptionalNumber(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetDouble(out var number)
        && double.IsFinite(number)
            ? number
            : 0;

    private static int ToWheelDelta(double distance)
    {
        var notches = Math.Clamp(
            (int)Math.Ceiling(Math.Abs(distance) / 100d),
            1,
            10);
        return Math.Sign(distance) * WheelDelta * notches;
    }

    private static void SendMouse(uint flags, uint data = 0)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Union = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    MouseData = data,
                    Flags = flags
                }
            }
        };
        Send(input);
    }

    private static void SendVirtualKey(ushort key, bool keyUp)
    {
        var input = new NativeInput
        {
            Type = InputKeyboard,
            Union = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
        Send(input);
    }

    private static void SendUnicode(char character, bool keyUp)
    {
        var input = new NativeInput
        {
            Type = InputKeyboard,
            Union = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    ScanCode = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
                }
            }
        };
        Send(input);
    }

    private static void Send(NativeInput input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<NativeInput>()) != 1)
        {
            throw new WorkspaceAiProviderException(
                "Windows rejected a bounded computer-control input event.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        NativeInput[] inputs,
        int sizeOfInput);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr deviceContext,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(
        IntPtr deviceContext,
        IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(
        IntPtr deviceContext,
        int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        IntPtr source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint rasterOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
