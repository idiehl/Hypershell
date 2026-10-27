using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hypershell.Services;

/// <summary>Keeps taskbar grouping and pinning independent of the current version directory.</summary>
internal static class TaskbarIdentityService
{
    internal const string AppUserModelId = "Hypershell.Desktop";
    private static string CurrentAppUserModelId =>
        Environment.GetEnvironmentVariable("HYPERSHELL_APP_USER_MODEL_ID") is { Length: > 0 } id ? id : AppUserModelId;
    private static readonly Guid AppUserModelPropertySet =
        new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly uint[] WindowPropertyIds = [2, 3, 4, 5];

    internal static bool InitializeProcessIdentity() => TryApply(() =>
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(CurrentAppUserModelId)));

    internal static bool ApplyWindowIdentity(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return TryApply(() => WithPropertyStore(windowHandle, store =>
        {
            var command = Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_COMMAND");
            if (!string.IsNullOrWhiteSpace(command))
            {
                // Windows requires command and display name together, and the
                // relaunch properties must precede the window AppUserModelID.
                SetString(store, 2, command);
                SetString(store, 4,
                    Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_DISPLAY_NAME")
                        is { Length: > 0 } displayName ? displayName : "Hypershell");
                var icon = Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_ICON_RESOURCE");
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    SetString(store, 3, icon);
                }
            }

            SetString(store, 5, CurrentAppUserModelId);
        }));
    }

    internal static bool ClearWindowIdentity(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        // Window property-store values must be released while the HWND is
        // still alive. MainWindow calls this only after closing is confirmed.
        return TryApply(() => WithPropertyStore(windowHandle, store =>
        {
            foreach (var id in WindowPropertyIds)
            {
                var key = new PropertyKey(AppUserModelPropertySet, id);
                var empty = new PropVariant();
                Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref empty));
            }
        }));
    }

    private static void WithPropertyStore(IntPtr windowHandle, Action<IPropertyStore> apply)
    {
        var interfaceId = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(windowHandle, ref interfaceId, out var store));
        try
        {
            apply(store);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(IPropertyStore store, uint propertyId, string text)
    {
        var key = new PropertyKey(AppUserModelPropertySet, propertyId);
        var value = new PropVariant
        {
            VariantType = 31, // VT_LPWSTR
            Pointer = Marshal.StringToCoTaskMemUni(text)
        };
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static bool TryApply(Action apply)
    {
        try
        {
            apply();
            return true;
        }
        catch (ExternalException exception)
        {
            // Shell integration must not prevent opening or closing the app.
            Trace.TraceWarning("Hypershell taskbar identity: {0}", exception.Message);
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr UnionRemainder;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
