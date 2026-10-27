using System.Text.Json.Nodes;
using Meridian.Host;

// An offline counterpart to the authoring API. ProjectStore exclusively owns
// .host.lock, so this cannot race a running Meridian instance or another writer.
if (args.Length != 2) throw new ArgumentException("Usage: OfflineAuthoring <data-root> <staged-folder>");
var dataRoot = Path.GetFullPath(args[0]);
var folder = Path.GetFullPath(args[1]);
JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, name)))!.AsObject();
var baseline = Read("baseline.json");
var candidate = Read("atlas.json");
var libraryBaseline = Read("library-baseline.json");
var library = Read("library.json");
var id = baseline["id"]!.GetValue<string>();
var revision = baseline["revision"]!.GetValue<int>();
ProjectValidator.Validate(candidate);
using var store = new ProjectStore(dataRoot);
using var libraries = new LibraryStore(dataRoot);
if (!JsonNode.DeepEquals(store.Read(id), baseline)) throw new InvalidOperationException("Saved atlas differs from the reviewed baseline. Rebase before applying.");
if (!JsonNode.DeepEquals(libraries.Read(), libraryBaseline)) throw new InvalidOperationException("Library differs from the reviewed baseline. Rebase before applying.");
if (File.Exists(Path.Combine(dataRoot, "projects", id, "recovery.json"))) throw new InvalidOperationException("A recovery draft exists. Reconcile it before applying.");
// Validate the library through its actual store before changing either live file.
var validationRoot = Path.Combine(folder, "library-validation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(validationRoot);
var validationCandidate = library.DeepClone().AsObject(); validationCandidate["revision"] = 0;
using (var validationStore = new LibraryStore(validationRoot)) await validationStore.Save(validationCandidate, 0);
var saved = await store.ApplyAuthoring(id, candidate, revision, null, "Before single-canvas atlas");
var savedLibrary = await libraries.Save(library, libraryBaseline["revision"]!.GetValue<int>());
if (!JsonNode.DeepEquals(store.Read(id), saved) || !JsonNode.DeepEquals(libraries.Read(), savedLibrary)) throw new IOException("Readback did not match committed content.");
File.WriteAllText(Path.Combine(folder, "installed.json"), saved.ToJsonString());
File.WriteAllText(Path.Combine(folder, "installed-library.json"), savedLibrary.ToJsonString());
Console.WriteLine(new JsonObject { ["projectId"] = id, ["revision"] = saved["revision"]!.DeepClone(), ["libraryRevision"] = savedLibrary["revision"]!.DeepClone(), ["checkpoint"] = "Before single-canvas atlas · saved", ["readbackMatches"] = true }.ToJsonString());
