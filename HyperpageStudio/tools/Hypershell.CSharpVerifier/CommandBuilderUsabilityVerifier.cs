using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Search behavior and literal insertion checks using a private profile and bundled source catalogs.</summary>
public static class CommandBuilderUsabilityVerifier
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task VerifyAsync(string outputRoot, string repositoryRoot)
    {
        var fixture = Path.Combine(Path.GetFullPath(outputRoot), "command-builder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var receipt = new Receipt { Fixture = fixture };
        try
        {
            var python = ReadBundledLibrary(repositoryRoot, "python-standard-library");
            Check(receipt, python.Commands.Count >= 10000, "The search fixture uses the actual large bundled Python standard library.");
            await RunStaAsync(() =>
            {
                VerifySearch(fixture, python, receipt);
                VerifyPaths(fixture, repositoryRoot, python, receipt);
            });
            await VerifyPythonLiteralsAsync(receipt);
            receipt.Passed = true;
        }
        catch (Exception error) { receipt.Failure = error.ToString(); throw; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(fixture, "command-builder-usability.json"), JsonSerializer.Serialize(receipt, Json));
        }
        foreach (var benchmark in receipt.Benchmarks)
            Console.WriteLine($"COMMAND_BUILDER_SEARCH_MEASURE|{benchmark.QueryClass}|queries:{benchmark.Queries}|old-bytes:{benchmark.ReferenceBytes}|new-bytes:{benchmark.CurrentBytes}|old-ms:{benchmark.ReferenceMilliseconds:F2}|new-ms:{benchmark.CurrentMilliseconds:F2}");
        Console.WriteLine($"COMMAND_BUILDER_USABILITY_OK|assertions:{receipt.Assertions.Count}|{fixture}");
    }

    private static void VerifySearch(string fixture, CommandLibrary python, Receipt receipt)
    {
        var synthetic = new CommandLibrary
        {
            Id = "search-contract", DisplayName = "Contract library", Description = "metadataonly selected surface",
            Language = "Python", Kind = CommandLibraryKind.StandardLibrary,
            FrameworkName = "AcceptanceFramework", FrameworkVersion = "7.3", TargetFramework = "net-target",
            Keywords = ["librarykeywordΩ"], ExecutionTarget = ExecutionTarget.External
        };
        synthetic.Commands.AddRange([
            Entry("exact", "needle", "needle"),
            Entry("prefix", "needle-prefix", "Needle prefix"),
            Entry("contains", "some-needle-value", "Contains needle"),
            Entry("object", "object-entry", "Object source", objectName: "needle"),
            Entry("action", "action-entry", "Action source", action: "needle"),
            Entry("module", "module-entry", "Module source", module: "needle"),
            Entry("description", "description-entry", "Description source", "needle other words tail"),
            Entry("unicode", "Résumé", "Résumé Ω", "Unicode search"),
            Entry("duplicate", "first-duplicate", "First duplicate", "duplicate needle"),
            Entry("DUPLICATE", "needle", "Second duplicate", "duplicate needle")
        ]);
        for (var i = 0; i < 85; i++)
            synthetic.Commands.Add(Entry("tie-" + i, "tie-" + i, "Equal tie", "needle stable ordering"));
        synthetic.Commands[0].Parameters.Add(new() { Name = "path", Description = "parameterhelp" });
        synthetic.Commands[1].GuideDefinition = "guidehelp";
        synthetic.Commands[2].BrowsePath.Add(new() { Name = "browsehelp", DisplayName = "Browse help" });
        foreach (var command in synthetic.Commands) command.CatalogId = synthetic.Id;

        var unrelated = new CommandLibrary { Id = "unrelated", DisplayName = "Unselected catalog", Language = "Python" };
        for (var i = 0; i < 20000; i++)
            unrelated.Commands.Add(new() { Id = "unrelated-" + i, CatalogId = unrelated.Id, Name = "needle", DisplayName = "needle", Description = "pathlib metadataonly python" });
        var builder = NewBuilder(Path.Combine(fixture, "search-profile"), "Python");
        foreach (var library in new[] { unrelated, python, synthetic })
        {
            builder.Libraries.Add(library);
            foreach (var command in library.Commands) builder.Commands.Add(command);
        }

        Select(builder, synthetic);
        foreach (var query in new[] { "needle", "NEEDLE", "  needle  ", "needle\ttail", "tail needle", "metadataonly", "librarykeywordΩ", "AcceptanceFramework", "net-target", "7.3", "StandardLibrary", "parameterhelp", "guidehelp", "browsehelp", "résumé", "Ω", "duplicate", "missing-contract-entry", "", " \t\r\n" })
            CompareSearch(builder, synthetic, query, receipt);
        builder.SearchQuery = "needle";
        Check(receipt, builder.SearchResults.Count == 50 && builder.SearchResults[0].Command?.Id == "exact"
            && builder.SearchResults.All(result => result.Command?.CatalogId == synthetic.Id),
            "Search limits to fifty results, ranks exact matches first, and excludes unselected catalogs.");
        var scores = ReferenceSearch(builder, synthetic, "needle").Select(result => result.Score).Distinct().ToArray();
        Check(receipt, scores.SequenceEqual(new[] { 0, 1, 2, 3, 4 }), "The comparison exercises exact, prefix, substring, facet, and full-text ranking.");
        Check(receipt, ReferenceSearch(builder, synthetic, "needle tail").Single().Score == 5,
            "Separated search terms retain their lower-ranked all-terms match.");

        synthetic.Commands[0].Parameters[0].Description = "freshlyloadedguide";
        synthetic.Keywords.Add("freshlibrarykeyword");
        CompareSearch(builder, synthetic, "freshlyloadedguide", receipt);
        CompareSearch(builder, synthetic, "freshlibrarykeyword", receipt);
        Check(receipt, builder.SearchResults.Count == 50, "Search immediately sees changed guide and library metadata without a stale cache.");

        Select(builder, python);
        foreach (var query in new[] { "pathlib", "Path.read_text", "read text", "python", "json", "sqlite connect", "PYTHON\npathlib", "standard library", "zz-no-search-result" })
            CompareSearch(builder, python, query, receipt);
        foreach (var (name, queries) in new[]
        {
            ("specific-and-missing", new[] { "pathlib", "read text", "zz-no-search-result", "sqlite connect" }),
            ("broad-library-metadata", new[] { "python", "standard library" })
        })
        {
            // Both algorithms are warmed before measurement. Timing is evidence, never a pass threshold.
            foreach (var query in queries)
            {
                _ = ReferenceSearch(builder, python, query);
                builder.SearchQuery = query;
            }
            var oldResult = Measure(() => { foreach (var query in queries) _ = ReferenceSearch(builder, python, query); }, 3);
            var newResult = Measure(() => { foreach (var query in queries) builder.SearchQuery = query; }, 3);
            receipt.Benchmarks.Add(new(name, queries.Length * 3, python.Commands.Count, builder.Commands.Count,
                oldResult.Bytes, newResult.Bytes, oldResult.Milliseconds, newResult.Milliseconds));
        }
        var firstLibrary = new CommandLibrary { Id = "first-collision-library", Language = "Python", ExecutionTarget = ExecutionTarget.External };
        var selectedLibrary = new CommandLibrary { Id = "selected-collision-library", Language = "Python", ExecutionTarget = ExecutionTarget.External };
        var firstCommand = PathCommand(ExecutionTarget.External);
        firstCommand.CatalogId = firstLibrary.Id;
        firstCommand.Template = "wrong_library({path})";
        firstLibrary.Commands.Add(firstCommand);
        var selectedCommand = PathCommand(ExecutionTarget.External);
        selectedCommand.CatalogId = selectedLibrary.Id;
        selectedCommand.Description = "scoped-collision";
        selectedCommand.Template = "selected_library({path})";
        selectedCommand.BrowsePath.Add(new() { Kind = "Module", Name = "selected-module" });
        selectedLibrary.Commands.Add(selectedCommand);
        builder.Libraries.Add(firstLibrary);
        builder.Libraries.Add(selectedLibrary);
        // Deferred registration historically retains only the first command with this ID globally.
        builder.Commands.Add(firstCommand);
        Select(builder, selectedLibrary);
        builder.SearchQuery = "scoped-collision";
        Check(receipt, builder.SearchResults.Count == 1 && ReferenceEquals(builder.SearchResults[0].Command, selectedCommand)
            && !builder.Commands.Contains(selectedCommand),
            "Selected-catalog search discovers its own entry when another catalog reused the same command ID.");
        builder.SelectedSearchResult = builder.SearchResults[0];
        builder.InsertPaths([@"C:\new\notes.txt"]);
        Check(receipt, ReferenceEquals(builder.SelectedCommand, selectedCommand)
            && builder.SelectedBuilderSurface?.LibraryId == selectedLibrary.Id
            && builder.StagedStatementText.StartsWith("selected_library(", StringComparison.Ordinal),
            "Opening a colliding search result keeps the selected library's exact command and source template.");
        builder.SelectedBuilderSurface = null;
        builder.SearchQuery = "pathlib unselected";
        Check(receipt, builder.SearchResults.Count == 0, "No selected surface produces no authored search results.");
    }

    private static void VerifyPaths(string fixture, string repositoryRoot, CommandLibrary python, Receipt receipt)
    {
        var command = python.Commands.Single(item => item.Id == "python.pathlib-read-text");
        var guidePath = Path.Combine(repositoryRoot, "src", "Hypershell", "Assets", "CommandLibraries", "guides", "python-standard-library.guides.json");
        var guide = JsonSerializer.Deserialize<CommandGuideShard>(File.ReadAllText(guidePath), Json)!.Guides.Single(item => item.EntryId == command.Id);
        command.Parameters = guide.Parameters;
        command.IsGuideLoaded = true;
        var builder = NewBuilder(Path.Combine(fixture, "path-profile"), "Python");
        builder.SelectedCommand = command;
        Check(receipt, command.Template.Contains("Path({path})", StringComparison.Ordinal)
            && builder.ParameterEditors.Any(parameter => parameter.Definition.Name == "path"),
            "Path insertion is exercised through the real bundled Python read-text command and guide.");
        var paths = new[] { @"C:\new\notes.txt", "C:\\a\"quoted\\folder\\", @"\\server\share\tab\file.txt", "C:\\café\\📄.txt", "control\tline\nend\r.txt" };
        foreach (var path in paths)
        {
            builder.InsertPaths([path]);
            var parameter = builder.ParameterEditors.Single(item => item.Definition.Name == "path");
            var literal = parameter.Value;
            Check(receipt, parameter.IsIncluded && JsonSerializer.Deserialize<string>(literal) == path
                && builder.StagedStatementText.Contains("Path(" + literal + ")", StringComparison.Ordinal),
                "Python parameter and statement preserve the inserted path: " + JsonSerializer.Serialize(path));
            receipt.PythonCases.Add(new(path, literal, builder.StagedStatementText));
        }
        Check(receipt, builder.AddStatementCommand.CanExecute(null), "An inserted Python path leaves the command ready to compose.");
        builder.AddStatementCommand.Execute(null);
        Check(receipt, builder.CommandText.Contains(receipt.PythonCases[^1].Literal, StringComparison.Ordinal),
            "Adding the real Python command preserves the same path literal in composed source.");

        foreach (var language in new[] { "JavaScript", "TypeScript", "Java" })
        {
            builder.SelectedInputSyntax = language;
            builder.SelectedCommand = PathCommand(ExecutionTarget.External);
            foreach (var path in paths)
            {
                builder.InsertPaths([path]);
                var literal = builder.ParameterEditors.Single().Value;
                Check(receipt, JsonSerializer.Deserialize<string>(literal) == path
                    && (!path.Contains('\n') || literal.Contains("\\n", StringComparison.Ordinal))
                    && (!path.Contains('\r') || literal.Contains("\\r", StringComparison.Ordinal)),
                    language + " source literal preserves separators, quotation marks, Unicode, and control characters: " + JsonSerializer.Serialize(path));
            }
        }

        builder.SelectedInputSyntax = "PowerShell";
        builder.SelectedCommand = PathCommand(ExecutionTarget.PowerShell);
        builder.InsertPaths([@"C:\O'Brien\notes.txt", @"C:\O'Brien\notes.txt", " "]);
        Check(receipt, builder.ParameterEditors.Single().Value == "'C:\\O''Brien\\notes.txt'",
            "PowerShell keeps its existing single-quoted escaping and duplicate/empty path filtering.");
        builder.SelectedInputSyntax = "C#";
        builder.SelectedCommand = PathCommand(ExecutionTarget.CSharp);
        builder.InsertPaths([paths[0]]);
        Check(receipt, JsonSerializer.Deserialize<string>(builder.ParameterEditors.Single().Value) == paths[0],
            "C# keeps its existing round-trippable string literal formatting.");
        builder.SelectedInputSyntax = "Bash";
        builder.SelectedCommand = PathCommand(ExecutionTarget.External);
        builder.InsertPaths([paths[0]]);
        Check(receipt, builder.ParameterEditors.Single().Value == "\"" + paths[0] + "\"",
            "Other external adapters retain their existing quoting behavior.");
        builder.SelectedInputSyntax = "Python";
        builder.SelectedCommand = new() { Id = "no-parameter", Name = "fixture", ExecutionTarget = ExecutionTarget.External, CompositionMode = CommandCompositionMode.Statement };
        builder.CommandText = "";
        builder.InsertPaths([paths[0]]);
        Check(receipt, JsonSerializer.Deserialize<string>(builder.CommandText) == paths[0],
            "Direct insertion without a parameter uses the same safe Python source literal.");
        foreach (var language in new[] { "Python", "JavaScript", "TypeScript", "Java", "C#" })
        {
            builder.SelectedInputSyntax = language;
            builder.SelectedCommand = null;
            foreach (var path in paths.Take(2))
            {
                builder.CommandText = "";
                builder.InsertPaths([path]);
                Check(receipt, JsonSerializer.Deserialize<string>(builder.CommandText) == path,
                    "Explorer insertion before choosing a command respects " + language + " syntax: " + JsonSerializer.Serialize(path));
                if (language == "Python") receipt.PythonCases.Add(new(path, builder.CommandText, null));
            }
        }
        foreach (var language in new[] { "PowerShell", "Bash" })
        {
            builder.SelectedInputSyntax = language;
            builder.SelectedCommand = null;
            builder.CommandText = "";
            builder.InsertPaths([@"C:\O'Brien\notes.txt"]);
            Check(receipt, builder.CommandText == "'C:\\O''Brien\\notes.txt'",
                "Explorer insertion before choosing a command preserves existing " + language + " quoting.");
        }
    }

    private static async Task VerifyPythonLiteralsAsync(Receipt receipt)
    {
        var start = new ProcessStartInfo("python")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("import ast,json,sys\ncases=json.load(sys.stdin)\nfor item in cases:\n assert ast.literal_eval(item['Literal']) == item['Path']\n if item['Statement'] is None: continue\n tree=ast.parse(item['Statement'])\n calls=[n for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='Path']\n assert len(calls)==1 and isinstance(calls[0].args[0],ast.Constant) and calls[0].args[0].value==item['Path']\nprint('PYTHON_LITERAL_PARSE_OK',len(cases))");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python literal verifier could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(receipt.PythonCases));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Python literal parsing exceeded 20 seconds."); }
        var output = await stdout;
        var error = await stderr;
        Check(receipt, process.ExitCode == 0 && output.Contains("PYTHON_LITERAL_PARSE_OK", StringComparison.Ordinal),
            "Python ast.literal_eval and ast.parse accept every generated literal and Path argument without executing the command. " + output.Trim() + error.Trim());
    }

    private static CommandLibrary ReadBundledLibrary(string root, string id)
    {
        var directory = Path.Combine(root, "src", "Hypershell", "Assets", "CommandLibraries");
        var library = JsonSerializer.Deserialize<CommandLibrary>(File.ReadAllText(Path.Combine(directory, id + ".json")), Json)!;
        foreach (var shard in library.EntryFiles)
            library.Commands.AddRange(JsonSerializer.Deserialize<CommandLibraryEntryShard>(File.ReadAllText(Path.Combine(directory, shard)), Json)!.Entries);
        // Entry shards inherit these fields from their catalog envelope, as CommandCatalogService does.
        foreach (var command in library.Commands)
        {
            command.CatalogId = library.Id;
            command.ExecutionTarget = library.ExecutionTarget;
        }
        return library;
    }

    private static CommandDefinition Entry(string id, string name, string display, string description = "", string objectName = "", string action = "", string module = "") =>
        new() { Id = id, Name = name, DisplayName = display, Description = description, ObjectName = objectName, Action = action, ModuleName = module, ExecutionTarget = ExecutionTarget.External };
    private static CommandDefinition PathCommand(ExecutionTarget target) => new()
    {
        Id = "path-fixture", Name = "path-fixture", DisplayName = "Path fixture", ExecutionTarget = target,
        CompositionMode = CommandCompositionMode.Statement, Template = "use({path})",
        Parameters = [new() { Name = "path", DisplayName = "Path", TypeName = "Path expression", IncludeByDefault = true, IsRequired = true }]
    };
    private static CommandBuilderViewModel NewBuilder(string profile, string language)
    {
        var service = new SettingsService(profile);
        var settings = service.Load();
        settings.CommandBuilder.ActiveLanguage = language;
        return new(settings, service);
    }
    private static void Select(CommandBuilderViewModel builder, CommandLibrary library) =>
        builder.SelectedBuilderSurface = new(library.Id, library.ExecutionTarget, library.DisplayName, library.Description, library.Language, library.Kind, false);

    private static void CompareSearch(CommandBuilderViewModel builder, CommandLibrary library, string query, Receipt receipt)
    {
        var expected = ReferenceSearch(builder, library, query);
        builder.SearchQuery = query;
        var actual = builder.SearchResults;
        Check(receipt, actual.Count == expected.Count && actual.Zip(expected).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Score == pair.Second.Score
            && ReferenceEquals(pair.First.Command, pair.Second.Command)
            && pair.First.DisplayName == pair.Second.DisplayName && pair.First.ContextLabel == pair.Second.ContextLabel),
            $"Original ranking, distinctness, and visible results retained for {library.Id}: {JsonSerializer.Serialize(query)}.");
    }

    // Frozen reference of the prior global-catalog algorithm. Keep it independent of production search helpers.
    private static List<CommandSearchResultViewModel> ReferenceSearch(CommandBuilderViewModel builder, CommandLibrary library, string input)
    {
        var query = input.Trim();
        if (query.Length == 0) return [];
        var terms = query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectedIds = new HashSet<string>([library.Id], StringComparer.OrdinalIgnoreCase);
        var libraries = builder.Libraries.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var candidates = builder.Commands.Where(command => selectedIds.Contains(command.CatalogId)).Select(command =>
        {
            libraries.TryGetValue(command.CatalogId, out var source);
            var metadata = source is null ? "" : $"{source.DisplayName} {source.Description} {source.Language} {source.Kind} {source.FrameworkName} {source.FrameworkVersion} {source.TargetFramework} {string.Join(' ', source.Keywords)}";
            var text = $"{command.SearchText} {metadata}";
            var score = (command.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || command.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase)) ? 0
                : (command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) || command.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase)) ? 1
                : (command.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || command.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) ? 2
                : (command.ObjectName.Contains(query, StringComparison.OrdinalIgnoreCase) || command.Action.Contains(query, StringComparison.OrdinalIgnoreCase) || command.ModuleName.Contains(query, StringComparison.OrdinalIgnoreCase)) ? 3
                : text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4 : 5;
            return (Result: CommandSearchResultViewModel.FromCommand(command, score), SearchText: text);
        }).Where(candidate => terms.All(term => candidate.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
          .Select(candidate => candidate.Result).ToList();
        return candidates.DistinctBy(candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Score).ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase).Take(50).ToList();
    }

    private static (long Bytes, double Milliseconds) Measure(Action action, int rounds)
    {
        var started = Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < rounds; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    private static void Check(Receipt receipt, bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        receipt.Assertions.Add(message);
    }
    private static Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "Command Builder acceptance" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private sealed class Receipt
    {
        public bool Passed { get; set; }
        public string Fixture { get; init; } = "";
        public string? Failure { get; set; }
        public List<string> Assertions { get; } = [];
        public List<SearchMeasurement> Benchmarks { get; } = [];
        public List<PathCase> PythonCases { get; } = [];
    }
    private sealed record PathCase(string Path, string Literal, string? Statement);
    private sealed record SearchMeasurement(string QueryClass, int Queries, int SelectedCatalogCommands, int GlobalCommands,
        long ReferenceBytes, long CurrentBytes, double ReferenceMilliseconds, double CurrentMilliseconds);
}
