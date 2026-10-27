using System.IO;
using System.Text.Json;
using HyperLib.Core;
using Hypershell;
using Hypershell.Models;
using Hypershell.ViewModels;

/// <summary>Exercises the exact one-shot WebView message guard and atomic native movement API without a live browser profile.</summary>
public static class StudioHeaderDragNativeVerifier
{
    public static async Task VerifyAsync(string outputDirectory)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputDirectory), "studio-header-drag-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture); var checks = new List<string>();
        var document = new Uri("http://127.0.0.1:49123/interactive/01234567890123456789012345678901?fixture=current");
        var original = new HyperlibHeaderLayoutDefinition { Enabled = true, Blocks = [new() { Id = "row", Arrangement = "positioned", Elements =
            [new() { Id = "text", X = 11.125, Y = 12.25, WidthPercent = 44.375 }, new() { Id = "box", Kind = "box", Arrangement = "positioned" }] }] };
        // Children intentionally have their own positioned parent, independent of the outer row.
        original.Blocks[0].Elements[1].Children = [new() { Id = "nested", Kind = "image", X = 5.125, Y = 6.25, WidthMode = "pixels", WidthPixels = 143.375, Height = 87.625 }];
        HyperlibHeaderDragCapability Session(HyperlibHeaderLayoutDefinition? layout = null) => new("secret", 7, "document-session", "landing", document, layout ?? ContentJson.Clone(original));
        Dictionary<string, object?> Message(string type = "studio-header-drag") => new() { ["type"] = type, ["token"] = "secret", ["generation"] = 7L, ["documentId"] = "document-session",
            ["elementId"] = "text", ["parentId"] = "row", ["x"] = 35.125, ["y"] = 41.25, ["previousX"] = 11.125, ["previousY"] = 12.25, ["sequence"] = 1L };
        string Json(Dictionary<string, object?> message) => JsonSerializer.Serialize(message);
        void Ready(HyperlibHeaderDragCapability session) => session.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message("studio-header-drag-ready")), 7, out _);
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        void Rejected(string name, Action<Dictionary<string, object?>>? edit = null, string? source = null, string? current = null, long version = 7, HyperlibHeaderLayoutDefinition? layout = null)
        {
            var session = Session(layout); Ready(session); var message = Message(); edit?.Invoke(message);
            Require(!session.TryReceive(source ?? document.AbsoluteUri, current ?? document.AbsoluteUri, Json(message), version, out _), name);
        }
        try
        {
            var pending = Session(); Require(!pending.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()), 7, out _), "A drop cannot commit before the current document's ready handshake.");
            Rejected("A different preview route is rejected.", source: document.AbsoluteUri.Replace("interactive/", "preview/", StringComparison.Ordinal));
            Rejected("Another request on the same preview host is rejected.", source: document.AbsoluteUri.Replace("01234567890123456789012345678901", "11234567890123456789012345678901", StringComparison.Ordinal));
            Rejected("A changed query is rejected.", source: document.AbsoluteUri.Replace("current", "stale", StringComparison.Ordinal));
            Rejected("A different origin is rejected.", source: document.AbsoluteUri.Replace(":49123", ":49124", StringComparison.Ordinal));
            Rejected("A stale current top-level document rejects even a valid message source.", current: "about:blank");
            Rejected("A previous draft generation is rejected.", version: 8);
            Rejected("A wrong session token is rejected.", value => value["token"] = "other");
            Rejected("A wrong document identity is rejected.", value => value["documentId"] = "old-document");
            Rejected("A mismatched generation in the message is rejected.", value => value["generation"] = 6);
            Rejected("Unknown element identities are rejected.", value => value["elementId"] = "missing");
            Rejected("A nested element cannot claim the outer row as its parent.", value => value["elementId"] = "nested");
            Rejected("A changed immediate parent is rejected.", value => value["parentId"] = "box");
            Rejected("Changed model start coordinates are rejected.", value => value["previousX"] = 11.126);
            Rejected("Negative coordinates are rejected.", value => value["x"] = -.001);
            Rejected("Coordinates beyond the parent are rejected.", value => value["y"] = 100.001);
            Rejected("Non-numeric coordinates are rejected.", value => value["x"] = "NaN");
            Rejected("Zero or missing drop sequences are rejected.", value => value["sequence"] = 0);
            var horizontal = ContentJson.Clone(original); horizontal.Blocks[0].Arrangement = "horizontal"; Rejected("A checked side-by-side row cannot be dragged.", layout: horizontal);
            var disabled = ContentJson.Clone(original); disabled.Enabled = false; Rejected("A disabled header cannot accept drops.", layout: disabled);
            var duplicates = ContentJson.Clone(original); duplicates.Blocks[0].Elements.Add(ContentJson.Clone(duplicates.Blocks[0].Elements[0])); Rejected("Duplicate element identities cannot select an ambiguous target.", layout: duplicates);
            var malformed = Session(); Ready(malformed); Require(!malformed.TryReceive(document.AbsoluteUri, document.AbsoluteUri, "{", 7, out _), "Malformed JSON is ignored without throwing.");
            Require(!malformed.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()).Replace("\"token\":\"secret\"", "\"token\":\"secret\",\"token\":\"other\"", StringComparison.Ordinal), 7, out _), "Duplicate JSON property names are rejected.");
            Require(!malformed.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()).Replace("35.125", "1e999", StringComparison.Ordinal), 7, out _), "Infinite JSON numeric values are rejected.");
            var session = Session(); Ready(session); Require(session.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()), 7, out var move) && move is not null, "A current ready document can request one valid free-placement move.");
            Require(!session.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()), 7, out _), "Replaying an accepted drop cannot create a second move or undo entry.");
            var vm = new HyperlibWizardHeaderLayoutViewModel(); vm.LoadDefinition(original); var notifications = 0; vm.DefinitionChanged += () => notifications++;
            Require(vm.TryMoveElement(move!.ElementId, move.ParentId, move.X, move.Y, move.PreviousX, move.PreviousY) && notifications == 1
                && vm.SelectedElement?.Id == "text" && vm.Blocks[0].Elements[0].WidthPercent == 44.375, "A guarded request commits atomically, selects its target, and preserves its size.");
            vm.Undo(); Require(vm.Blocks[0].Elements[0].X == 11.125 && vm.Blocks[0].Elements[0].Y == 12.25, "One Undo restores both coordinates from a single drag.");
            var nestedSession = Session(); Ready(nestedSession); var nested = Message(); nested["elementId"] = "nested"; nested["parentId"] = "box"; nested["previousX"] = 5.125; nested["previousY"] = 6.25;
            Require(nestedSession.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(nested), 7, out var nestedMove)
                && vm.TryMoveElement(nestedMove!.ElementId, nestedMove.ParentId, nestedMove.X, nestedMove.Y, nestedMove.PreviousX, nestedMove.PreviousY)
                && vm.Blocks[0].Elements[1].Children[0].WidthPixels == 143.375, "A nested Box accepts its own child's drag and retains exact pixel sizing.");
            var stale = Session(); Ready(stale); Require(stale.TryReceive(document.AbsoluteUri, document.AbsoluteUri, Json(Message()), 7, out var staleMove), "A previously rendered draft can be parsed for the live-model stale check.");
            vm.Blocks[0].Elements[0].X = 19; Require(!vm.TryMoveElement(staleMove!.ElementId, staleMove.ParentId, staleMove.X, staleMove.Y, staleMove.PreviousX, staleMove.PreviousY), "A model change after rendering rejects the stale move without overwriting the newer edit.");
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-header-drag-native-verification.json"), JsonSerializer.Serialize(new { Passed = true, Assertions = checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "studio-header-drag-native-verification.json"), JsonSerializer.Serialize(new { Passed = false, Assertions = checks, Failure = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); throw;
        }
        Console.WriteLine($"STUDIO_HEADER_DRAG_NATIVE_OK|checks={checks.Count}|fixture={fixture}");
    }
}
