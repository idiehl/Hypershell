using Hypershell.Models;
using System.IO;
using System.Text.Json;

namespace Hypershell.Services;

public sealed record HyperpageDocumentTypeDefinition(
    string Id,
    string Section,
    string DisplayName,
    string Extension,
    string LanguageId,
    string Description,
    string DefaultBaseName);

public static class HyperpageDocumentTypeCatalog
{
    public static IReadOnlyList<HyperpageDocumentTypeDefinition> Definitions { get; } =
    [
        new(
            "css",
            "Styles",
            "CSS stylesheet",
            ".css",
            "css",
            "Browser-ready styling referenced by HTML or a Razor host.",
            "site"),
        new(
            "scss",
            "Styles",
            "SCSS stylesheet",
            ".scss",
            "scss",
            "Sass-compatible nested CSS syntax that compiles to CSS.",
            "site"),
        new(
            "sass",
            "Styles",
            "Sass stylesheet",
            ".sass",
            "sass",
            "Indented Sass syntax that compiles to CSS.",
            "site"),
        new(
            "less",
            "Styles",
            "Less stylesheet",
            ".less",
            "less",
            "Less variables, nesting, and mixins that compile to CSS.",
            "site"),
        new(
            "javascript",
            "Code",
            "JavaScript file",
            ".js",
            "javascript",
            "Browser code loaded with a script element or module import.",
            "app"),
        new(
            "javascript-module",
            "Code",
            "JavaScript module",
            ".mjs",
            "javascript",
            "An explicit ECMAScript module with imports and exports.",
            "feature"),
        new(
            "typescript",
            "Code",
            "TypeScript file",
            ".ts",
            "typescript",
            "Typed browser code that compiles to JavaScript.",
            "app"),
        new(
            "csharp",
            "Code",
            "C# source",
            ".cs",
            "csharp",
            "Compiled .NET source included by the nearest SDK project.",
            "Feature")
    ];

    public static HyperpageDocumentTypeDefinition GetRequired(string id) =>
        Definitions.FirstOrDefault(definition => definition.Id.Equals(
            id,
            StringComparison.OrdinalIgnoreCase))
        ?? throw new VisualPageValidationException(
            $"Unknown Hyperpage document type '{id}'.");

    public static string ResolveLanguageId(string path)
    {
        var extension = Path.GetExtension(path);
        var definition = Definitions.FirstOrDefault(candidate =>
            candidate.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
        return definition?.LanguageId
               ?? EditorLanguageCatalog.ResolveLanguageId(path);
    }

    public static string DefaultDirectory(
        HyperpageDocumentTypeDefinition definition,
        VisualPageProjectManifest project)
    {
        var hostedDirectly = VisualPageProjectService.ResolvePageFileType(
            project, HyperpagePageFileType.ProjectDefault) != HyperpagePageFileType.RazorPage;
        return definition.Id switch
        {
            "css" => hostedDirectly
                ? "src/Host/wwwroot/styles"
                : "src/Pages/wwwroot/styles",
            "scss" or "sass" or "less" => "src/Styles",
            "javascript" or "javascript-module" => hostedDirectly
                ? "src/Host/wwwroot/scripts"
                : "src/Pages/wwwroot/scripts",
            "typescript" => "src/Scripts",
            "csharp" => hostedDirectly ? "src/Host/Code" : "src/Pages/Code",
            _ => string.Empty
        };
    }

    public static string CreateStarter(
        HyperpageDocumentTypeDefinition definition,
        VisualPageProjectManifest project,
        string fileName)
    {
        var symbol = MakeIdentifier(Path.GetFileNameWithoutExtension(fileName));
        return definition.Id switch
        {
            "css" => """
                /* Browser-ready CSS.
                   HTML/CSHTML: reference the published URL with <link rel="stylesheet" href="...">.
                   Razor components may instead use a collocated Component.razor.css file for CSS isolation. */

                :root {
                  color-scheme: dark light;
                }
                """,
            "scss" => """
                // SCSS is authoring syntax; compile this file to .css before a browser can load it.
                $accent: #6ea8fe;

                .component {
                  color: $accent;

                  &:focus-visible {
                    outline: 2px solid currentColor;
                  }
                }
                """,
            "sass" => """
                // Indented Sass is authoring syntax; compile this file to .css for the browser.
                $accent: #6ea8fe

                .component
                  color: $accent

                  &:focus-visible
                    outline: 2px solid currentColor
                """,
            "less" => """
                // Less is authoring syntax; compile this file to .css before linking it.
                @accent: #6ea8fe;

                .component {
                  color: @accent;

                  &:focus-visible {
                    outline: 2px solid currentColor;
                  }
                }
                """,
            "javascript" when fileName.EndsWith(
                ".razor.js",
                StringComparison.OrdinalIgnoreCase) => """
                // Collocated Razor JavaScript is an ES module, not automatic C# code-behind.
                // Import this .razor.js URL explicitly with IJSRuntime before invoking its exports.
                export function initialize(element) {
                  element.dataset.ready = 'true';
                }
                """,
            "javascript" => """
                // Classic JavaScript runs in the browser after a <script src="..."> element loads it.
                // Razor C# crosses into this runtime explicitly through JavaScript interop.
                function initialize(root = document) {
                  return root;
                }

                globalThis.Hyperpage ??= {};
                globalThis.Hyperpage.initialize = initialize;
                """,
            "javascript-module" => """
                // ECMAScript module. Load with <script type="module"> or import it from another module.
                export function initialize(options = {}) {
                  return { ...options };
                }
                """,
            "typescript" => """
                // TypeScript provides static types but must be compiled to JavaScript for the browser.
                export interface FeatureOptions {
                  enabled: boolean;
                }

                export function initialize(options: FeatureOptions): FeatureOptions {
                  return { ...options };
                }
                """,
            "csharp" => $$"""
                // C# is compiled into the server/component assembly; it does not execute as browser JavaScript.
                namespace {{project.RootNamespace}};

                public sealed class {{symbol}}
                {
                }
                """,
            _ => string.Empty
        };
    }

    private static string MakeIdentifier(string value)
    {
        var characters = value
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => char.ToUpperInvariant(part[0]) + part[1..])
            .Where(character => char.IsLetterOrDigit(character) || character == '_')
            .ToArray();
        var result = characters.Length == 0 ? "Feature" : new string(characters);
        return char.IsDigit(result[0]) ? "Feature" + result : result;
    }
}

public sealed record HyperpageAuthoringParameter(
    string Id,
    string DisplayName,
    string DefaultValue,
    string Help,
    bool IsRequired = true,
    bool IsMultiline = false);

public sealed record HyperpageAuthoringRecipe(
    string Id,
    string Name,
    string Category,
    string Summary,
    string Usage,
    string Template,
    string Signature,
    string Source,
    string DocumentationUrl,
    IReadOnlyList<string> LanguageIds,
    IReadOnlyList<HyperpageAuthoringParameter> Parameters,
    bool IsReference = false)
{
    public string KindLabel => IsReference ? "REFERENCE" : "PATTERN";
    public bool HasDocumentationUrl => !string.IsNullOrWhiteSpace(
        DocumentationUrl);

    public bool Supports(string languageId) => LanguageIds.Contains(
        languageId,
        StringComparer.OrdinalIgnoreCase);

    public string Render(IReadOnlyDictionary<string, string>? values = null)
    {
        var result = Template;
        foreach (var parameter in Parameters)
        {
            var value = values?.GetValueOrDefault(parameter.Id)
                        ?? parameter.DefaultValue;
            result = result.Replace(
                $"[[{parameter.Id}]]",
                value,
                StringComparison.Ordinal);
        }
        return result;
    }

    public string Example => Render();
}

public static class HyperpageLanguageAuthoringCatalog
{
    private static readonly Lazy<IReadOnlyList<HyperpageAuthoringRecipe>> LazyRecipes =
        new(LoadCore);

    public static IReadOnlyList<HyperpageAuthoringRecipe> Recipes =>
        LazyRecipes.Value;

    public static bool Supports(string languageId) =>
        Recipes.Any(recipe => recipe.Supports(languageId));

    public static string RelationshipSummary(string languageId) =>
        languageId.ToLowerInvariant() switch
        {
            "css" =>
                "CSS is downloaded by the browser and styles the HTML that Razor renders. Link global CSS explicitly; collocated .razor.css uses build-time isolation.",
            "scss" or "sass" or "less" =>
                "This is preprocessor source. Compile it to browser-ready CSS, then link the resulting .css file from the host page.",
            "javascript" =>
                "JavaScript executes in the browser. Razor-generated HTML can load it normally; Blazor C# communicates with it through explicit JS interop.",
            "typescript" =>
                "TypeScript adds development-time types, then compiles to JavaScript. Only the emitted JavaScript executes in the browser.",
            "csharp" =>
                "C# is compiled into the .NET server or component assembly. Razor uses it during rendering and events; it is not a drop-in browser-script runtime.",
            _ => "Choose a CSS, JavaScript, TypeScript, or C# document to use the authoring library."
        };

    private static IReadOnlyList<HyperpageAuthoringRecipe> LoadCore()
    {
        var recipes = CreatePatterns().ToList();
        var cssReferencePath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Hyperpage",
            "css-properties-2.31.0.json");
        TryLoadReferenceCatalog(
            cssReferencePath,
            entry => CreateCssReference(entry, indentedSass: false),
            recipes);
        TryLoadReferenceCatalog(
            cssReferencePath,
            entry => CreateCssReference(entry, indentedSass: true),
            recipes);
        TryLoadReferenceCatalog(
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Hyperpage",
                "ecmascript-2024-declarations.json"),
            entry => CreateJavaScriptReference(entry, "ECMAScript 2024"),
            recipes);
        TryLoadReferenceCatalog(
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Hyperpage",
                "web-platform-declarations.json"),
            entry => CreateJavaScriptReference(
                entry,
                "Web platform · " + Pretty(entry.SurfaceId)),
            recipes);

        try
        {
            recipes.AddRange(CSharpKeywordCatalogService.Load().Select(keyword =>
                new HyperpageAuthoringRecipe(
                    "csharp-keyword." + keyword.Keyword,
                    keyword.Keyword,
                    "Keywords · " + keyword.CategoryLabel,
                    keyword.Description,
                    "Language keyword reference. Insert it where the C# grammar permits this lexical form.",
                    keyword.Keyword,
                    keyword.Keyword,
                    "C# keyword catalog",
                    string.Empty,
                    ["csharp"],
                    [],
                    IsReference: true)));
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException)
        {
            // Practical patterns remain available when a packaged reference index
            // is missing from an unusual development output.
        }

        return recipes
            .GroupBy(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(recipe => recipe.IsReference)
            .ThenBy(recipe => recipe.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HyperpageAuthoringRecipe CreateJavaScriptReference(
        HyperpageLanguageReferenceEntry entry,
        string source)
    {
        var invocation = entry.SymbolKind.Equals(
            "Function",
            StringComparison.OrdinalIgnoreCase)
            || entry.SymbolKind.Equals("Method", StringComparison.OrdinalIgnoreCase)
            ? entry.Name + "()"
            : entry.Name;
        return new HyperpageAuthoringRecipe(
            "javascript-ref." + source.Replace(' ', '-').ToLowerInvariant() + "." + entry.Id,
            entry.DisplayName,
            Pretty(entry.SymbolKind) + " · " + source,
            entry.Signature,
            "API reference from the packaged language inventory. Check the signature and target runtime before using it.",
            invocation,
            entry.Signature,
            source,
            entry.DocumentationUrl,
            ["javascript", "typescript"],
            [],
            IsReference: true);
    }

    private static HyperpageAuthoringRecipe CreateCssReference(
        HyperpageLanguageReferenceEntry entry,
        bool indentedSass)
    {
        return new HyperpageAuthoringRecipe(
            (indentedSass ? "sass-ref." : "css-ref.") + entry.Id,
            entry.DisplayName,
            "Properties · " + Pretty(entry.SurfaceId),
            entry.Signature,
            "A standards-indexed CSS property. Supply a value valid for the active selector and browser targets.",
            entry.Name + ": [[value]]" + (indentedSass ? string.Empty : ";"),
            entry.Signature,
            "MDN data 2.31.0 / CSS specifications",
            entry.DocumentationUrl,
            indentedSass ? ["sass"] : ["css", "scss", "less"],
            [new("value", "Value", "initial", "CSS value or expression.")],
            IsReference: true);
    }

    private static void TryLoadReferenceCatalog(
        string path,
        Func<HyperpageLanguageReferenceEntry, HyperpageAuthoringRecipe> convert,
        ICollection<HyperpageAuthoringRecipe> target)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            var catalog = JsonSerializer.Deserialize<HyperpageLanguageReferenceCatalog>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (catalog?.Entries is null)
            {
                return;
            }
            foreach (var entry in catalog.Entries.Where(entry =>
                         !string.IsNullOrWhiteSpace(entry.Id)
                         && !string.IsNullOrWhiteSpace(entry.Name)))
            {
                target.Add(convert(entry));
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or JsonException)
        {
            // Keep the handcrafted patterns usable if an optional reference
            // inventory is unavailable or corrupt.
        }
    }

    private static IEnumerable<HyperpageAuthoringRecipe> CreatePatterns()
    {
        yield return Pattern(
            "css.rule", "Rule", "Structure", "Create a selector and declaration block.",
            "Use a class for reusable styling; keep selectors as specific as necessary, but no more.",
            """
            [[selector]] {
              [[property]]: [[value]];
            }
            """,
            "selector { property: value; }", ["css", "scss", "less"],
            new("selector", "Selector", ".component", "Class, element, ID, or compound selector."),
            new("property", "Property", "display", "CSS property name."),
            new("value", "Value", "grid", "Property value."));
        yield return Pattern(
            "css.tokens", "Design tokens", "Structure", "Define reusable custom properties.",
            "Custom properties participate in the cascade and are read with var(--name).",
            """
            :root {
              --[[name]]: [[value]];
            }
            """,
            ":root { --token: value; }", ["css", "scss", "less"],
            new("name", "Token", "surface", "Name without the leading double dash."),
            new("value", "Value", "#111820", "Any valid CSS value."));
        yield return Pattern(
            "css.media", "Media query", "Responsive", "Apply rules when a viewport condition matches.",
            "Media queries adapt presentation to viewport, input, motion, contrast, or user preferences.",
            """
            @media ([[condition]]) {
              [[selector]] {
                [[property]]: [[value]];
              }
            }
            """,
            "@media (condition) { ... }", ["css", "scss", "less"],
            new("condition", "Condition", "width >= 48rem", "Modern range or classic media condition."),
            new("selector", "Selector", ".component", "Rule selector."),
            new("property", "Property", "grid-template-columns", "CSS property."),
            new("value", "Value", "repeat(2, minmax(0, 1fr))", "Responsive value."));
        yield return Pattern(
            "css.container", "Container query", "Responsive", "Respond to a containing element instead of the viewport.",
            "Set container-type on an ancestor before querying its inline size.",
            """
            .[[container]] {
              container-type: inline-size;
            }

            @container (width >= [[width]]) {
              [[selector]] {
                [[property]]: [[value]];
              }
            }
            """,
            "@container (width >= size) { ... }", ["css", "scss", "less"],
            new("container", "Container", "layout", "Container class without the dot."),
            new("width", "Width", "40rem", "Container threshold."),
            new("selector", "Selector", ".component", "Descendant selector."),
            new("property", "Property", "display", "CSS property."),
            new("value", "Value", "grid", "Responsive value."));
        yield return Pattern(
            "css.supports", "Feature query", "Compatibility", "Gate declarations behind CSS feature support.",
            "Use @supports when a useful fallback can remain outside the query.",
            """
            @supports ([[condition]]) {
              [[selector]] {
                [[property]]: [[value]];
              }
            }
            """,
            "@supports (property: value) { ... }", ["css", "scss", "less"],
            new("condition", "Condition", "display: grid", "Feature expression."),
            new("selector", "Selector", ".component", "Rule selector."),
            new("property", "Property", "display", "CSS property."),
            new("value", "Value", "grid", "Supported value."));
        yield return Pattern(
            "css.keyframes", "Keyframes", "Motion", "Define a reusable CSS animation.",
            "Pair the keyframes name with animation or animation-name. Respect reduced-motion preferences.",
            """
            @keyframes [[name]] {
              from { [[property]]: [[from]]; }
              to { [[property]]: [[to]]; }
            }
            """,
            "@keyframes name { from { ... } to { ... } }", ["css", "scss", "less"],
            new("name", "Name", "fade-in", "Animation identifier."),
            new("property", "Property", "opacity", "Animated property."),
            new("from", "From", "0", "Starting value."),
            new("to", "To", "1", "Ending value."));
        yield return Pattern(
            "css.grid", "Grid layout", "Layout", "Create a resilient grid with responsive tracks.",
            "minmax(0, 1fr) prevents long content from forcing a track beyond its container.",
            """
            [[selector]] {
              display: grid;
              grid-template-columns: [[columns]];
              gap: [[gap]];
            }
            """,
            "display: grid;", ["css", "scss", "less"],
            new("selector", "Selector", ".layout", "Grid container selector."),
            new("columns", "Columns", "repeat(auto-fit, minmax(16rem, 1fr))", "Track definition."),
            new("gap", "Gap", "1rem", "Row and column gap."));
        yield return Pattern(
            "css.flex", "Flex layout", "Layout", "Create a one-dimensional flexible layout.",
            "Flexbox is well suited to rows, toolbars, and content groups with one primary axis.",
            """
            [[selector]] {
              display: flex;
              align-items: [[align]];
              justify-content: [[justify]];
              gap: [[gap]];
            }
            """,
            "display: flex;", ["css", "scss", "less"],
            new("selector", "Selector", ".cluster", "Flex container selector."),
            new("align", "Align", "center", "Cross-axis alignment."),
            new("justify", "Justify", "space-between", "Main-axis distribution."),
            new("gap", "Gap", "0.75rem", "Spacing between items."));
        yield return Pattern(
            "css.razor-isolation", "Razor isolated descendant", "Razor", "Style descendant markup from a collocated component stylesheet.",
            "Use ::deep only in Component.razor.css. Razor's build rewrites the selector so the component's generated scope attribute anchors the rule.",
            "[[ancestor]] ::deep [[descendant]] {\n  [[property]]: [[value]];\n}",
            ".component ::deep .descendant { ... }", ["css"],
            new("ancestor", "Component selector", ".component", "Selector on markup owned by the component."),
            new("descendant", "Descendant", ".label", "Selector inside rendered child content."),
            new("property", "Property", "color", "CSS property."),
            new("value", "Value", "inherit", "Property value."));
        yield return Pattern(
            "scss.variable", "Sass variable", "Preprocessor", "Define a compile-time Sass value.",
            "Sass variables disappear during compilation; CSS custom properties remain at runtime.",
            "$[[name]]: [[value]];", "$name: value;", ["scss"],
            new("name", "Name", "accent", "Variable name without $."),
            new("value", "Value", "#6ea8fe", "Sass value."));
        yield return Pattern(
            "scss.mixin", "Sass mixin", "Preprocessor", "Define and include a parameterized declaration group.",
            "Use mixins for repeated declaration groups that need compile-time parameters.",
            """
            @mixin [[name]]($[[parameter]]: [[default]]) {
              [[property]]: $[[parameter]];
            }

            [[selector]] {
              @include [[name]]([[value]]);
            }
            """,
            "@mixin name($value) { ... }", ["scss"],
            new("name", "Mixin", "focus-ring", "Mixin name."),
            new("parameter", "Parameter", "color", "Parameter without $."),
            new("default", "Default", "currentColor", "Default parameter value."),
            new("property", "Property", "outline-color", "Declaration controlled by the mixin."),
            new("selector", "Selector", ".button", "Selector using the mixin."),
            new("value", "Argument", "#6ea8fe", "Value passed to the mixin."));
        yield return Pattern(
            "less.mixin", "Less mixin", "Preprocessor", "Define and call a parameterized Less mixin.",
            "A guarded or parameterized mixin expands declarations at compile time.",
            """
            .[[name]](@[[parameter]]: [[default]]) {
              [[property]]: @[[parameter]];
            }

            [[selector]] {
              .[[name]]([[value]]);
            }
            """,
            ".mixin(@value) { ... }", ["less"],
            new("name", "Mixin", "focus-ring", "Mixin name without the dot."),
            new("parameter", "Parameter", "color", "Parameter name without @."),
            new("default", "Default", "currentColor", "Default value."),
            new("property", "Property", "outline-color", "Generated property."),
            new("selector", "Selector", ".button", "Selector using the mixin."),
            new("value", "Argument", "#6ea8fe", "Value passed to the mixin."));
        yield return Pattern(
            "sass.rule", "Sass rule", "Preprocessor", "Create an indented Sass selector and declaration.",
            "Indented Sass uses indentation instead of braces and compiles to ordinary CSS.",
            """
            [[selector]]
              [[property]]: [[value]]
            """,
            ".selector\n  property: value", ["sass"],
            new("selector", "Selector", ".component", "Sass selector."),
            new("property", "Property", "display", "CSS property."),
            new("value", "Value", "grid", "Property value."));
        yield return Pattern(
            "sass.variable", "Sass variable", "Preprocessor", "Define an indented-syntax Sass value.",
            "Sass variables are resolved while compiling; use CSS custom properties for runtime values.",
            "$[[name]]: [[value]]", "$name: value", ["sass"],
            new("name", "Name", "accent", "Variable name without $."),
            new("value", "Value", "#6ea8fe", "Sass value."));
        yield return Pattern(
            "sass.mixin", "Sass mixin", "Preprocessor", "Define an indented Sass mixin.",
            "Include the mixin inside an indented selector to expand its declarations.",
            """
            =[[name]]($[[parameter]]: [[default]])
              [[property]]: $[[parameter]]

            [[selector]]
              +[[name]]([[value]])
            """,
            "=mixin($value)\n  property: $value", ["sass"],
            new("name", "Mixin", "focus-ring", "Mixin name."),
            new("parameter", "Parameter", "color", "Parameter without $."),
            new("default", "Default", "currentColor", "Default value."),
            new("property", "Property", "outline-color", "Generated property."),
            new("selector", "Selector", ".button", "Selector using the mixin."),
            new("value", "Argument", "#6ea8fe", "Mixin argument."));

        yield return Pattern(
            "js.constant", "Constant", "Declarations", "Declare a block-scoped immutable binding.",
            "const prevents rebinding; the referenced object may still be mutable.",
            "const [[name]] = [[value]];", "const name = value;", ["javascript", "typescript"],
            new("name", "Name", "value", "Binding name."),
            new("value", "Value", "{}", "JavaScript expression."));
        yield return Pattern(
            "js.function", "Function", "Functions", "Declare a reusable function.",
            "Prefer a small explicit parameter surface and a clear return value.",
            """
            function [[name]]([[parameters]]) {
              [[body]]
            }
            """,
            "function name(parameters) { ... }", ["javascript"],
            new("name", "Name", "transform", "Function name."),
            new("parameters", "Parameters", "value", "Comma-separated parameters.", false),
            new("body", "Body", "return value;", "Function statements.", true, true));
        yield return Pattern(
            "js.async", "Async function", "Async", "Declare an asynchronous function with error propagation.",
            "await pauses this function without blocking the browser thread; callers receive a Promise.",
            """
            async function [[name]]([[parameters]]) {
              [[body]]
            }
            """,
            "async function name() { await ... }", ["javascript", "typescript"],
            new("name", "Name", "loadData", "Function name."),
            new("parameters", "Parameters", "signal", "Comma-separated parameters.", false),
            new("body", "Body", "const response = await fetch('/api/data', { signal });\nreturn response.json();", "Async statements.", true, true));
        yield return Pattern(
            "js.object", "Object", "Objects", "Create an object literal with named properties.",
            "Object literals group related values and behavior without declaring a class.",
            """
            const [[name]] = {
              [[property]]: [[value]],
            };
            """,
            "const object = { property: value };", ["javascript", "typescript"],
            new("name", "Name", "options", "Object binding name."),
            new("property", "Property", "enabled", "First property name."),
            new("value", "Value", "true", "First property expression."));
        yield return Pattern(
            "js.class", "Class", "Objects", "Declare a class with initialized state.",
            "Use a class when instances share behavior and maintain their own state.",
            """
            class [[name]] {
              constructor([[parameter]]) {
                this.[[property]] = [[parameter]];
              }
            }
            """,
            "class Name { constructor(value) { ... } }", ["javascript"],
            new("name", "Name", "Controller", "Class name."),
            new("parameter", "Parameter", "root", "Constructor parameter."),
            new("property", "Property", "root", "Instance property."));
        yield return Pattern(
            "js.event", "Event listener", "DOM", "Attach a browser event handler.",
            "Keep a reference to the handler when it must later be removed.",
            """
            document.querySelector('[[selector]]')?.addEventListener('[[event]]', ([[parameter]]) => {
              [[body]]
            });
            """,
            "element.addEventListener('event', handler);", ["javascript", "typescript"],
            new("selector", "Selector", "[data-action]", "CSS selector for the target."),
            new("event", "Event", "click", "DOM event name."),
            new("parameter", "Parameter", "event", "Callback parameter."),
            new("body", "Body", "console.log(event.currentTarget);", "Handler statements.", true, true));
        yield return Pattern(
            "js.fetch", "Fetch JSON", "Network", "Request JSON with status and cancellation handling.",
            "fetch resolves on HTTP errors, so check response.ok before reading the body.",
            """
            export async function [[name]](signal) {
              const response = await fetch('[[url]]', { signal });
              if (!response.ok) {
                throw new Error(`Request failed: ${response.status}`);
              }
              return response.json();
            }
            """,
            "await fetch(url, { signal });", ["javascript"],
            new("name", "Name", "loadData", "Exported function name."),
            new("url", "URL", "/api/data", "Relative or absolute request URL."));
        yield return Pattern(
            "js.import", "Module import", "Modules", "Import named bindings from another module.",
            "Browser module paths are URLs and normally include the emitted file extension.",
            "import { [[members]] } from '[[path]]';", "import { member } from './module.js';", ["javascript", "typescript"],
            new("members", "Members", "initialize", "Comma-separated exported names."),
            new("path", "Path", "./feature.js", "Module URL or package specifier."));
        yield return Pattern(
            "js.export", "Named export", "Modules", "Export a declaration for other modules.",
            "Named exports provide statically discoverable module contracts.",
            "export const [[name]] = [[value]];", "export const name = value;", ["javascript", "typescript"],
            new("name", "Name", "feature", "Exported binding."),
            new("value", "Value", "{}", "Exported expression."));
        yield return Pattern(
            "js.blazor-module", "Blazor module export", "Razor interop", "Export a function from a collocated .razor.js module.",
            "A .razor.js file is not an automatic code-behind. Import its URL through IJSRuntime, retain the returned module reference, and dispose it with the component.",
            "export function [[name]]([[parameter]]) {\n  [[body]]\n}",
            "export function initialize(element) { ... }", ["javascript", "typescript"],
            new("name", "Export", "initialize", "Function name invoked through the module reference."),
            new("parameter", "Parameter", "element", "Parameter received from .NET.", false),
            new("body", "Body", "element.dataset.ready = 'true';", "Browser-side statements.", true, true));
        yield return Pattern(
            "ts.interface", "Interface", "Types", "Describe the shape of a TypeScript value.",
            "Interfaces are erased during compilation and do not create runtime objects.",
            """
            export interface [[name]] {
              [[property]]: [[type]];
            }
            """,
            "interface Name { property: Type; }", ["typescript"],
            new("name", "Name", "FeatureOptions", "Interface name."),
            new("property", "Property", "enabled", "Property name."),
            new("type", "Type", "boolean", "Type annotation."));
        yield return Pattern(
            "ts.type", "Type alias", "Types", "Name a union, intersection, tuple, or object type.",
            "Use a union to constrain a value to a finite set of alternatives.",
            "export type [[name]] = [[type]];", "type Name = A | B;", ["typescript"],
            new("name", "Name", "Status", "Type alias name."),
            new("type", "Type", "'idle' | 'loading' | 'ready' | 'error'", "Type expression."));
        yield return Pattern(
            "ts.function", "Typed function", "Functions", "Declare typed parameters and a return value.",
            "Explicit public signatures document intent while local types can often be inferred.",
            """
            export function [[name]]([[parameter]]: [[type]]): [[returnType]] {
              [[body]]
            }
            """,
            "function name(value: Type): Result { ... }", ["typescript"],
            new("name", "Name", "transform", "Function name."),
            new("parameter", "Parameter", "value", "Parameter name."),
            new("type", "Type", "string", "Parameter type."),
            new("returnType", "Returns", "string", "Return type."),
            new("body", "Body", "return value.trim();", "Function statements.", true, true));

        yield return Pattern(
            "csharp.class", "Class", "Types", "Declare a reference type with encapsulated behavior.",
            "SDK-style .NET projects compile .cs files beneath the project directory automatically.",
            """
            public sealed class [[name]]
            {
              [[body]]
            }
            """,
            "public sealed class Name { ... }", ["csharp"],
            new("name", "Name", "FeatureService", "PascalCase type name."),
            new("body", "Body", "", "Members and nested types.", false, true));
        yield return Pattern(
            "csharp.record", "Record", "Types", "Declare a value-oriented data contract.",
            "Records provide value equality and concise immutable models by default.",
            "public sealed record [[name]]([[parameters]]);", "public sealed record Name(Type Value);", ["csharp"],
            new("name", "Name", "FeatureOptions", "PascalCase record name."),
            new("parameters", "Parameters", "bool Enabled", "Primary-constructor parameters.", false));
        yield return Pattern(
            "csharp.interface", "Interface", "Types", "Declare a behavior contract.",
            "Keep interfaces cohesive and name asynchronous methods with an Async suffix.",
            """
            public interface [[name]]
            {
              [[returnType]] [[method]]([[parameters]]);
            }
            """,
            "public interface IName { Result Method(); }", ["csharp"],
            new("name", "Name", "IFeatureService", "Interface name."),
            new("returnType", "Returns", "Task", "Return type."),
            new("method", "Method", "RunAsync", "Method name."),
            new("parameters", "Parameters", "CancellationToken cancellationToken = default", "Parameter list.", false));
        yield return Pattern(
            "csharp.property", "Property", "Members", "Declare an initialized auto-property.",
            "Use init for construction-time assignment or set when later mutation is part of the contract.",
            "public [[type]] [[name]] { get; [[setter]]; } = [[value]];", "public string Name { get; init; } = string.Empty;", ["csharp"],
            new("type", "Type", "string", "Property type."),
            new("name", "Name", "Name", "PascalCase property name."),
            new("setter", "Setter", "init", "init or set."),
            new("value", "Initial", "string.Empty", "Initializer expression."));
        yield return Pattern(
            "csharp.method", "Method", "Members", "Declare a method with an explicit return contract.",
            "A method belongs inside a class, record, or struct unless it is a local function.",
            """
            public [[returnType]] [[name]]([[parameters]])
            {
              [[body]]
            }
            """,
            "public Result Method(parameters) { ... }", ["csharp"],
            new("returnType", "Returns", "void", "Return type."),
            new("name", "Name", "Run", "PascalCase method name."),
            new("parameters", "Parameters", "", "Comma-separated typed parameters.", false),
            new("body", "Body", "", "Method statements.", false, true));
        yield return Pattern(
            "csharp.async", "Async method", "Async", "Declare a cancellation-aware asynchronous method.",
            "Pass CancellationToken through I/O calls and avoid blocking on Task results.",
            """
            public async Task<[[type]]> [[name]](CancellationToken cancellationToken = default)
            {
              [[body]]
            }
            """,
            "public async Task<T> MethodAsync(CancellationToken cancellationToken) { ... }", ["csharp"],
            new("type", "Result", "string", "Task result type."),
            new("name", "Name", "LoadAsync", "Async-suffixed method name."),
            new("body", "Body", "await Task.Yield();\nreturn string.Empty;", "Awaitable body.", true, true));
        yield return Pattern(
            "csharp.di", "Service registration", "ASP.NET Core", "Register an implementation with dependency injection.",
            "Choose singleton, scoped, or transient lifetime to match ownership and shared state.",
            "builder.Services.Add[[lifetime]]<[[service]], [[implementation]]>();", "builder.Services.AddScoped<IService, Service>();", ["csharp"],
            new("lifetime", "Lifetime", "Scoped", "Singleton, Scoped, or Transient."),
            new("service", "Service", "IFeatureService", "Service contract type."),
            new("implementation", "Implementation", "FeatureService", "Concrete type."));
        yield return Pattern(
            "csharp.endpoint", "API endpoint", "ASP.NET Core", "Map a minimal API endpoint.",
            "Map endpoints in the host startup pipeline and return a serializable result.",
            """
            app.Map[[verb]]("[[route]]", ([[parameters]]) =>
            {
              return [[result]];
            });
            """,
            "app.MapGet(\"/route\", () => Results.Ok());", ["csharp"],
            new("verb", "Verb", "Get", "Get, Post, Put, Patch, or Delete."),
            new("route", "Route", "/api/feature", "URL route pattern."),
            new("parameters", "Parameters", "", "Handler parameters.", false),
            new("result", "Result", "Results.Ok()", "Handler result expression."));
        yield return Pattern(
            "csharp.razor-partial", "Razor component code-behind", "Razor", "Create the partial class paired with a .razor component.",
            "Save this as Component.razor.cs, use the component's namespace and class name, and keep the partial modifier. Both halves compile into one component type.",
            "namespace [[namespace]];\n\npublic partial class [[component]]\n{\n  [[body]]\n}",
            "public partial class Component { ... }", ["csharp"],
            new("namespace", "Namespace", "App.Components", "Namespace used by the .razor component."),
            new("component", "Component", "FeatureCard", "Component file name without .razor."),
            new("body", "Body", "", "Fields, properties, lifecycle methods, and event handlers.", false, true));
        yield return Pattern(
            "csharp.page-model", "Razor Page model", "Razor", "Create the PageModel paired with a .cshtml Razor Page.",
            "Save this as Page.cshtml.cs and reference the class from Page.cshtml with @model. Handler methods run on the server before the response is rendered.",
            "using Microsoft.AspNetCore.Mvc.RazorPages;\n\nnamespace [[namespace]];\n\npublic sealed class [[name]]Model : PageModel\n{\n  public void OnGet()\n  {\n    [[body]]\n  }\n}",
            "public sealed class PageModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel", ["csharp"],
            new("namespace", "Namespace", "App.Pages", "Namespace used by the .cshtml page."),
            new("name", "Page", "Index", "PageModel name without the Model suffix."),
            new("body", "OnGet body", "", "Server-side request handler statements.", false, true));
        yield return Pattern(
            "csharp.js-interop", "JavaScript interop call", "Razor interop", "Invoke an explicitly loaded browser function from a Razor component.",
            "C# and JavaScript use separate runtimes. IJSRuntime serializes the call across that boundary; use a module reference for collocated .razor.js exports.",
            "[Microsoft.AspNetCore.Components.Inject]\nprivate Microsoft.JSInterop.IJSRuntime JavaScript { get; set; } = default!;\n\nprivate async ValueTask [[name]]Async()\n{\n  await JavaScript.InvokeVoidAsync(\"[[identifier]]\"[[arguments]]);\n}",
            "await JavaScript.InvokeVoidAsync(\"identifier\", arguments);", ["csharp"],
            new("name", "Method", "Initialize", "Method name without the Async suffix."),
            new("identifier", "Identifier", "Hyperpage.initialize", "Global JavaScript function identifier."),
            new("arguments", "Arguments", ", element", "Optional comma-prefixed serialized arguments.", false));
        yield return Pattern(
            "csharp.razor-parameter", "Razor parameter", "Razor", "Expose a component parameter to parent markup.",
            "Place this member in a .razor @code block or the component's partial .razor.cs class.",
            """
            [Parameter]
            public [[type]] [[name]] { get; set; }[[initial]]
            """,
            "[Parameter] public string Value { get; set; } = string.Empty;", ["csharp"],
            new("type", "Type", "string", "Parameter type."),
            new("name", "Name", "Value", "PascalCase parameter name."),
            new("initial", "Initial", " = string.Empty;", "Initializer including semicolon."));
    }

    private static HyperpageAuthoringRecipe Pattern(
        string id,
        string name,
        string category,
        string summary,
        string usage,
        string template,
        string signature,
        IReadOnlyList<string> languages,
        params HyperpageAuthoringParameter[] parameters) =>
        new(
            id,
            name,
            category,
            summary,
            usage,
            template,
            signature,
            "Hyperpage practical patterns",
            string.Empty,
            languages,
            parameters);

    private static string Pretty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Core";
        }
        return string.Join(
            " ",
            value.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private sealed class HyperpageLanguageReferenceCatalog
    {
        public int EntryCount { get; set; }
        public List<HyperpageLanguageReferenceEntry> Entries { get; set; } = [];
    }

    private sealed class HyperpageLanguageReferenceEntry
    {
        public string Id { get; set; } = string.Empty;
        public string SurfaceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string DocumentationUrl { get; set; } = string.Empty;
    }
}
