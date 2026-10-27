# Hypershell editor host

This small Vite project packages Monaco for the native WPF application. The
generated site is local-only and is hosted by `MonacoEditorControl` through a
WebView2 virtual host mapping.

From this directory:

```powershell
npm install
npm run build
```

The build writes to `../Hypershell/Assets/EditorHost`. Generated files are
copied into the .NET output by `Hypershell.csproj`. The Monaco MIT license is
copied into the generated directory automatically.

The native bridge accepts state, focus, insertion, Markdown styling, and
explicit editor-command messages. Find, Replace, and Go to Line stay inside Monaco. Format uses a
registered Monaco formatter when one exists; otherwise it performs only the
local whitespace fallback and reports that result to the WPF host. External
process, terminal, file-system, and network access are not exposed to this
page.
