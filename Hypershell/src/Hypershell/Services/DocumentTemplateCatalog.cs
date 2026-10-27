using Hypershell.Models;

namespace Hypershell.Services;

public static class DocumentTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string,
        IReadOnlyList<DocumentTemplateDefinition>> TemplatesByLanguage =
        new Dictionary<string, IReadOnlyList<DocumentTemplateDefinition>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["java"] =
            [
                new(
                    "java-console-app",
                    "Java console application",
                    "A Java 17 console entry point that can be compiled and run directly from the document menu.",
                    "java",
                    """
                    class Main {
                        public static void main(String[] args) {
                            System.out.println("Hello from Hypershell");
                        }
                    }
                    """,
                    IsDefault: true),
                new(
                    "java-record-model",
                    "Java record and stream",
                    "A small Java 17 program with an immutable record and a stream pipeline.",
                    "java",
                    """
                    import java.util.List;

                    class Main {
                        public static void main(String[] args) {
                            var tasks = List.of(
                                new Task("Build Java support", true),
                                new Task("Ship the next feature", false));

                            tasks.stream()
                                .filter(Task::complete)
                                .map(Task::title)
                                .forEach(System.out::println);
                        }
                    }

                    record Task(String title, boolean complete) {
                    }
                    """)
            ],
            ["html"] =
            [
                new(
                    "html-page",
                    "HTML page",
                    "A semantic document shell with responsive metadata and separate stylesheet and module references.",
                    "html",
                    """
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <meta name="viewport" content="width=device-width, initial-scale=1">
                      <title>Hypershell Web Page</title>
                      <link rel="stylesheet" href="styles.css">
                    </head>
                    <body>
                      <main>
                        <h1>Hello, web</h1>
                        <p>Edit this page, save it, then choose Preview in Browser.</p>
                      </main>
                      <script type="module" src="app.js"></script>
                    </body>
                    </html>
                    """,
                    IsDefault: true),
                new(
                    "html-self-contained-app",
                    "Self-contained web app",
                    "One immediately previewable HTML file with embedded CSS and JavaScript.",
                    "html",
                    """
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <meta name="viewport" content="width=device-width, initial-scale=1">
                      <title>Hypershell Web App</title>
                      <style>
                        :root {
                          color-scheme: dark;
                          font-family: system-ui, sans-serif;
                        }

                        body {
                          display: grid;
                          min-height: 100vh;
                          margin: 0;
                          place-items: center;
                          background: #0d1117;
                          color: #e6edf3;
                        }

                        main {
                          width: min(36rem, calc(100% - 2rem));
                          padding: 2rem;
                          border: 1px solid #30363d;
                          border-radius: 0.75rem;
                          background: #161b22;
                        }

                        button {
                          padding: 0.65rem 1rem;
                          border: 0;
                          border-radius: 0.4rem;
                          background: #58a6ff;
                          color: #0d1117;
                          cursor: pointer;
                        }
                      </style>
                    </head>
                    <body>
                      <main>
                        <h1>Hypershell Web App</h1>
                        <p id="status">Ready to build.</p>
                        <button id="action" type="button">Run action</button>
                      </main>
                      <script type="module">
                        const status = document.querySelector("#status");
                        const action = document.querySelector("#action");

                        action?.addEventListener("click", () => {
                          if (status) status.textContent = "The browser script is working.";
                        });
                      </script>
                    </body>
                    </html>
                    """)
            ],
            ["css"] =
            [
                new(
                    "css-stylesheet",
                    "Responsive stylesheet",
                    "A compact design foundation with custom properties, sensible sizing, and responsive content.",
                    "css",
                    """
                    :root {
                      color-scheme: dark;
                      --page-bg: #0d1117;
                      --panel-bg: #161b22;
                      --text: #e6edf3;
                      --accent: #58a6ff;
                      font-family: system-ui, sans-serif;
                    }

                    *,
                    *::before,
                    *::after {
                      box-sizing: border-box;
                    }

                    body {
                      min-height: 100vh;
                      margin: 0;
                      background: var(--page-bg);
                      color: var(--text);
                    }

                    main {
                      width: min(70rem, calc(100% - 2rem));
                      margin-inline: auto;
                      padding-block: 2rem;
                    }

                    a {
                      color: var(--accent);
                    }
                    """,
                    IsDefault: true)
            ],
            ["javascript"] =
            [
                new(
                    "javascript-browser-module",
                    "Browser module",
                    "A strict browser module that waits for the document and binds one interaction.",
                    "javascript",
                    """
                    const status = document.querySelector("#status");
                    const action = document.querySelector("#action");

                    action?.addEventListener("click", () => {
                      if (status) status.textContent = "The browser module is running.";
                    });
                    """,
                    IsDefault: true)
            ],
            ["typescript"] =
            [
                new(
                    "typescript-browser-source",
                    "Typed browser module source",
                    "Typed DOM source intended to be compiled to JavaScript before a page imports it.",
                    "typescript",
                    """
                    const status = document.querySelector<HTMLElement>("#status");
                    const action = document.querySelector<HTMLButtonElement>("#action");

                    action?.addEventListener("click", (): void => {
                      if (status) status.textContent = "The compiled TypeScript module is running.";
                    });
                    """,
                    IsDefault: true)
            ]
        };

    public static IReadOnlyList<DocumentTemplateDefinition> GetTemplates(
        string? languageId)
    {
        var language = EditorLanguageCatalog.GetLanguage(languageId);
        var blank = new DocumentTemplateDefinition(
            "blank",
            "Blank document",
            "Start with an empty document.",
            language.Id,
            string.Empty,
            IsDefault: !TemplatesByLanguage.ContainsKey(language.Id));
        return TemplatesByLanguage.TryGetValue(language.Id, out var templates)
            ? [blank, .. templates]
            : [blank];
    }
}
