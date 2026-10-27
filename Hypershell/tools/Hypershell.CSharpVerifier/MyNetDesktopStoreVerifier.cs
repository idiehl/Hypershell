using Hypershell.Models;
using Hypershell.Services;

internal static class MyNetDesktopStoreVerifier
{
    public static void Verify(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "mynet-desktop-store");
        Directory.CreateDirectory(root);
        VerifyPersistence(root);
        VerifySafeBounds();
        VerifyCorruptFilePreservation(root);
        VerifyRegistryConcurrency(root);
        Console.WriteLine("MYNET_DESKTOP_STORE_OK|layout|background|bounds|atomic-save|corrupt-file-preserved|registry-concurrency");
    }

    private static void VerifyPersistence(string root)
    {
        var path = Path.Combine(root, "desktop.json");
        var store = new MyNetDesktopStore(path);
        var state = store.Load();
        Require(!File.Exists(path), "Reading absent desktop settings must not create a file.");
        Require(state.Width == 360 && state.Height == 720 && state.Icons.Count == 0,
            "The desktop must have a usable first-run layout.");
        state.BackgroundMode = MyNetDesktopBackgroundMode.Image;
        state.BackgroundImagePath = Path.Combine(root, "wallpaper.png");
        state.Left = -360;
        state.Top = 24;
        state.Width = 460;
        state.Height = 800;
        state.Icons["atlas.meridian"] = new MyNetDesktopIconPosition { X = 72, Y = 140 };
        state.Icons["below-viewport"] = new MyNetDesktopIconPosition { X = 8, Y = 1300 };
        store.Save(state);

        var loaded = new MyNetDesktopStore(path).Load();
        Require(loaded.BackgroundMode == MyNetDesktopBackgroundMode.Image
                && loaded.BackgroundImagePath == state.BackgroundImagePath
                && loaded.Left == -360 && loaded.Top == 24
                && loaded.Width == 460 && loaded.Height == 800
                && loaded.Icons["ATLAS.MERIDIAN"].X == 72
                && loaded.Icons["atlas.meridian"].Y == 140
                && loaded.Icons["below-viewport"].Y == 1300,
            "Desktop background, window bounds, and per-app placement must survive a new store instance.");
        var cloned = loaded.Clone();
        cloned.Icons["atlas.meridian"].X = 200;
        Require(loaded.Icons["atlas.meridian"].X == 72,
            "Cloned desktop positions must not mutate an existing layout.");

        foreach (var mode in new[] { MyNetDesktopBackgroundMode.Transparent, MyNetDesktopBackgroundMode.Surface })
        {
            loaded.BackgroundMode = mode;
            store.Save(loaded);
            var recalled = store.Load();
            Require(recalled.BackgroundMode == mode && recalled.BackgroundImagePath == state.BackgroundImagePath,
                "Changing background modes must preserve the chosen image for later use.");
        }

        Require(Directory.GetFiles(root, "*.tmp").Length == 0,
            "Atomic desktop writes must not leave partial settings files.");
        var saved = File.ReadAllText(path);
        loaded.SchemaVersion++;
        ExpectInvalid(() => store.Save(loaded), "Saving unsupported settings must fail.");
        Require(File.ReadAllText(path) == saved,
            "Rejected desktop settings must leave the previous saved file intact.");
    }

    private static void VerifySafeBounds()
    {
        var normalized = MyNetDesktopStore.NormalizeAndValidate(new MyNetDesktopState
        {
            Left = double.NaN,
            Top = double.PositiveInfinity,
            Width = -40,
            Height = 999999,
            Icons = new Dictionary<string, MyNetDesktopIconPosition>
            {
                ["large"] = new() { X = 999999, Y = -25 },
                ["overflow-row"] = new() { X = 8, Y = 999999 },
                ["non-finite"] = new() { X = double.NaN, Y = double.PositiveInfinity }
            }
        });
        Require(normalized.Left is null && normalized.Top is null
                && normalized.Width == 240 && normalized.Height == 16000,
            "Unsafe window dimensions must be clamped, with non-finite coordinates unset.");
        Require(normalized.Icons["large"].X == 152 && normalized.Icons["large"].Y == 0
                && normalized.Icons["overflow-row"].Y == 100000
                && normalized.Icons["non-finite"].X == 0 && normalized.Icons["non-finite"].Y == 0,
            "Icons must stay inside the available surface with finite coordinates.");
        var narrow = MyNetDesktopStore.ClampIcon(new() { X = 80, Y = 80 }, 30, 30);
        Require(narrow.X == 0 && narrow.Y == 0,
            "A canvas smaller than an icon must produce a safe origin.");
        var invalidCanvas = MyNetDesktopStore.ClampIcon(new() { X = 80, Y = 80 }, double.NaN, double.PositiveInfinity);
        Require(invalidCanvas.X == 0 && invalidCanvas.Y == 0,
            "An unavailable canvas must not produce non-finite layout values.");
        ExpectInvalid(() => MyNetDesktopStore.NormalizeAndValidate(new()
        {
            BackgroundMode = (MyNetDesktopBackgroundMode)99
        }), "Unsupported background modes must be rejected.");
        ExpectInvalid(() => MyNetDesktopStore.NormalizeAndValidate(new()
        {
            BackgroundMode = MyNetDesktopBackgroundMode.Image,
            BackgroundImagePath = "https://example.com/wallpaper.png"
        }), "Desktop backgrounds must not refer to remote image URLs.");
        ExpectInvalid(() => MyNetDesktopStore.NormalizeAndValidate(new()
        {
            BackgroundMode = MyNetDesktopBackgroundMode.Image,
            BackgroundImagePath = @"\\server\wallpaper.png"
        }), "Desktop backgrounds must remain device-local.");
        ExpectInvalid(() => MyNetDesktopStore.NormalizeAndValidate(new()
        {
            Icons = new Dictionary<string, MyNetDesktopIconPosition>
            {
                ["atlas"] = new(),
                ["ATLAS"] = new()
            }
        }), "Duplicate icon IDs must not silently overwrite layout entries.");
    }

    private static void VerifyCorruptFilePreservation(string root)
    {
        var path = Path.Combine(root, "corrupt-desktop.json");
        const string corrupt = "{ unavailable desktop state";
        File.WriteAllText(path, corrupt);
        var store = new MyNetDesktopStore(path);
        ExpectInvalid(() => store.Load(), "Corrupt desktop settings must report an explicit read error.");
        ExpectInvalid(() => store.Save(new()), "Fallback defaults must not overwrite a corrupt settings file.");
        ExpectInvalid(() => new MyNetDesktopStore(path).Save(new()),
            "A fresh store must validate existing settings before replacing them.");
        Require(File.ReadAllText(path) == corrupt,
            "The original corrupt desktop file must remain available for recovery.");

        File.WriteAllText(path, "{\"SchemaVersion\":999}");
        ExpectInvalid(() => new MyNetDesktopStore(path).Load(), "Future settings schemas must report an explicit error.");
        File.WriteAllText(path, "null");
        ExpectInvalid(() => new MyNetDesktopStore(path).Load(), "Null settings must report an explicit error.");
        File.WriteAllText(path, "{}");
        var repaired = store.Load();
        repaired.BackgroundMode = MyNetDesktopBackgroundMode.Transparent;
        store.Save(repaired);
        Require(store.Load().BackgroundMode == MyNetDesktopBackgroundMode.Transparent,
            "A successful reload after repair must enable persistence again.");

        File.WriteAllText(path, corrupt);
        ExpectInvalid(() => store.Save(repaired), "External corruption after loading must also block a save.");
        Require(File.ReadAllText(path) == corrupt,
            "Externally corrupted settings must be preserved.");
    }

    private static void VerifyRegistryConcurrency(string root)
    {
        var registryRoot = Path.Combine(root, "shared-registry");
        Directory.CreateDirectory(registryRoot);
        AtlasMyNetAppStore CreateStore() => new(
            Path.Combine(registryRoot, "apps.json"),
            Path.Combine(registryRoot, "trust.json"),
            Path.Combine(registryRoot, "PingLab_Offline"));
        var first = CreateStore();
        var second = CreateStore();
        _ = first.Load();
        _ = second.Load();
        AtlasMyNetAppDefinition CreateApp(string id) => new()
        {
            Id = id,
            DisplayName = id,
            UrlTemplate = "http://127.0.0.1:65534/",
            Launch = new AtlasMyNetLaunchDefinition
            {
                Executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Verifier executable unavailable.")
            }
        };
        var alpha = CreateApp("verifier.concurrent-alpha");
        var beta = CreateApp("verifier.concurrent-beta");
        using var start = new ManualResetEventSlim(false);
        var updates = new[]
        {
            Task.Run(() =>
            {
                start.Wait();
                first.UpdateDefinitions(apps => apps.Add(alpha.Clone()));
                first.Trust(alpha);
            }),
            Task.Run(() =>
            {
                start.Wait();
                second.UpdateDefinitions(apps => apps.Add(beta.Clone()));
                second.Trust(beta);
            })
        };
        start.Set();
        Task.WhenAll(updates).GetAwaiter().GetResult();

        // Both clients have older in-memory snapshots when editing separate entries.
        first.UpdateDefinitions(apps => apps.Single(app => app.Id == alpha.Id).DisplayName = "Alpha edited");
        second.UpdateDefinitions(apps => apps.Single(app => app.Id == beta.Id).DisplayName = "Beta edited");
        var recalled = CreateStore();
        var definitions = recalled.Load();
        Require(definitions.Single(app => app.Id == alpha.Id).DisplayName == "Alpha edited"
                && definitions.Single(app => app.Id == beta.Id).DisplayName == "Beta edited",
            "Independent app edits from stale clients must preserve both updates.");
        Require(recalled.IsTrusted(alpha) && recalled.IsTrusted(beta),
            "Concurrent trust updates from stale clients must preserve both app grants.");

        first.RevokeTrust(alpha);
        _ = recalled.Load();
        Require(!recalled.IsTrusted(alpha) && recalled.IsTrusted(beta),
            "Revoking one app through a stale client must preserve another app's trust grant.");
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
