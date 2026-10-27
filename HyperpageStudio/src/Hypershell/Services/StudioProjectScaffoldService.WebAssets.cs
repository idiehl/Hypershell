using Hypershell.Models;

namespace Hypershell.Services;

public sealed partial class StudioProjectScaffoldService
{
    private const string WebProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    private const string HtmlProgram = """
        // User-owned development host. The website itself is portable static HTML/CSS/JavaScript.
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapGet("/healthz", () => Results.Json(new { status = "healthy", kind = "html" }));
        // Missing URLs intentionally return 404 instead of hiding a broken link behind the home page.
        app.Run();
        """;

    private const string RazorProgram = """
        // User-owned server. These are ordinary server-rendered .cshtml Razor Pages, with no Blazor runtime.
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorPages();
        var app = builder.Build();
        app.UseStaticFiles();
        app.MapGet("/healthz", () => Results.Json(new { status = "healthy", kind = "razor-pages" }));
        // A small example endpoint you can extend with your own server-side services.
        app.MapGet("/api/status", () => Results.Json(new
        {
            application = "Studio Razor starter",
            message = "Your server is ready.",
            utc = DateTimeOffset.UtcNow
        }));
        app.MapRazorPages();
        app.Run();
        """;

    private const string StarterCss = """
        /* USER-OWNED. Visual saves preserve this file.
           1. Set your palette here. 2. Tune the component classes. 3. Check the narrow layout. */
        :root {
          color-scheme: light;
          --studio-bg: #f6f8f4; --studio-surface: #ffffff; --studio-ink: #182d29;
          --studio-muted: #576c66; --studio-accent: #246c56; --studio-border: #dce5dc;
          --studio-soft: #e8eee4; --studio-shadow: 0 16px 50px rgb(30 57 42 / 7%);
        }
        :root[data-theme="dark"] {
          color-scheme: dark;
          --studio-bg: #14201d; --studio-surface: #1c2c27; --studio-ink: #eff5ed;
          --studio-muted: #b4c6bd; --studio-accent: #90d7b2; --studio-border: #344b40;
          --studio-soft: #253a30; --studio-shadow: 0 16px 50px rgb(0 0 0 / 15%);
        }
        * { box-sizing: border-box; }
        html { scroll-behavior: smooth; scroll-padding-top: 6rem; }
        body { margin: 0; background: var(--studio-bg); color: var(--studio-ink); font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; line-height: 1.65; }
        a { color: inherit; text-underline-offset: .2em; }
        button, input { font: inherit; }
        button { cursor: pointer; }
        button:disabled { cursor: default; opacity: .5; }
        :focus-visible { outline: 3px solid var(--studio-accent); outline-offset: 4px; }
        img { display: block; max-width: 100%; height: auto; }
        h1, h2, p, figure { margin: 0; }
        h1, h2 { line-height: 1.12; letter-spacing: -.045em; }
        h1 { font-size: clamp(2.5rem, 5.5vw, 5rem); font-weight: 650; max-width: 12ch; }
        h2 { font-size: clamp(1.45rem, 2.7vw, 2rem); font-weight: 650; }
        .studio-site { display: block; width: min(1180px, 100% - 3rem); margin: 0 auto; }
        .studio-site > header { display: flex; gap: 1.5rem; align-items: center; justify-content: space-between; padding: 1.7rem 0; border-bottom: 1px solid var(--studio-border); }
        .brand { display: inline-flex; align-items: center; gap: .6rem; font-weight: 750; letter-spacing: -.025em; text-decoration: none; font-size: 1.1rem; }
        .brand::before { content: ""; display: block; width: 1.6rem; height: 1.6rem; background: url('/assets/studio-mark.svg') center / contain no-repeat; }
        .site-nav { display: flex; align-items: center; gap: 1.5rem; font-size: .9rem; }
        .site-nav a { text-decoration: none; white-space: nowrap; }
        .site-nav a[aria-current="page"] { font-weight: 750; color: var(--studio-accent); }
        .theme-toggle { background: transparent; color: var(--studio-ink); border: 1px solid var(--studio-border); border-radius: 999px; padding: .45rem .8rem; font-size: .82rem; }
        .nav-toggle { display: none; background: var(--studio-surface); color: var(--studio-ink); border: 1px solid var(--studio-border); border-radius: .65rem; padding: .4rem .8rem; }
        .hero { padding: clamp(3rem, 8vw, 6.5rem) 0; }
        .hero-copy { display: flex; flex-direction: column; gap: 1.5rem; align-items: flex-start; }
        .eyebrow { text-transform: uppercase; letter-spacing: .17em; color: var(--studio-accent); font-size: .74rem; font-weight: 800; }
        .lede { font-size: 1.06rem; color: var(--studio-muted); max-width: 49ch; }
        .actions { display: flex; flex-wrap: wrap; gap: .7rem; margin-top: .25rem; }
        .button { display: inline-flex; align-items: center; justify-content: center; padding: .75rem 1.15rem; border-radius: .8rem; text-decoration: none; border: 1px solid var(--studio-border); font-weight: 650; font-size: .9rem; line-height: 1.3; min-height: 44px; }
        .primary { background: var(--studio-accent); border-color: var(--studio-accent); color: var(--studio-surface); }
        .secondary { background: var(--studio-surface); color: var(--studio-ink); }
        :root[data-theme="dark"] .primary { color: #14201d; }
        .button:hover { filter: brightness(.96); }
        .hero-art { overflow: hidden; border-radius: 1.7rem; background: var(--studio-soft); border: 1px solid var(--studio-border); box-shadow: var(--studio-shadow); }
        .hero-art img { width: 100%; aspect-ratio: 9 / 7; object-fit: cover; }
        .hero-art figcaption { padding: 1rem 1.4rem; font-size: .8rem; color: var(--studio-muted); }
        .features { margin: 0 0 4rem; }
        .feature-card { background: var(--studio-surface); border: 1px solid var(--studio-border); border-radius: 1.1rem; padding: 1.7rem; display: flex; flex-direction: column; gap: 1rem; }
        .feature-card h2 { font-size: 1.3rem; letter-spacing: -.025em; }
        .feature-card > p:last-child { color: var(--studio-muted); font-size: .91rem; }
        .feature-number { color: var(--studio-accent); font-size: .8rem; font-weight: 800; }
        .planner, .about-section { border-radius: 1.4rem; border: 1px solid var(--studio-border); background: var(--studio-soft); padding: clamp(1.5rem, 4vw, 3rem); margin-bottom: 4rem; }
        .planner > div:first-child { display: grid; gap: .9rem; max-width: 64ch; }
        .planner > div:first-child > p:last-child { color: var(--studio-muted); }
        .plan-form { display: grid; gap: .5rem; margin-top: 1.7rem; }
        .plan-form label { font-size: .85rem; font-weight: 650; }
        .input-row { display: flex; gap: .7rem; }
        .input-row input { flex: 1; min-width: 0; background: var(--studio-surface); color: var(--studio-ink); border: 1px solid var(--studio-border); border-radius: .8rem; padding: .7rem 1rem; }
        #plan-status { margin-top: 1rem; color: var(--studio-muted); font-size: .85rem; }
        .plan-list { list-style: none; display: grid; gap: .6rem; padding: 0; margin: 1rem 0; }
        .plan-list li { display: flex; align-items: center; gap: .75rem; padding: .75rem 1rem; background: var(--studio-surface); border: 1px solid var(--studio-border); border-radius: .8rem; }
        .plan-list input { width: 1.15rem; height: 1.15rem; accent-color: var(--studio-accent); flex: 0 0 auto; }
        .plan-list label { flex: 1; overflow-wrap: anywhere; }
        .plan-list li[data-complete="true"] label { text-decoration: line-through; color: var(--studio-muted); }
        .task-remove { border: 0; background: transparent; color: var(--studio-muted); padding: .3rem .5rem; border-radius: .3rem; font-size: .85rem; }
        .about-section { display: grid; gap: 1rem; }
        .about-section ol { display: grid; gap: .8rem; margin: 0; padding-left: 1.4rem; color: var(--studio-muted); }
        .site-footer { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 1rem; padding: 1.7rem 0 2.5rem; border-top: 1px solid var(--studio-border); font-size: .82rem; color: var(--studio-muted); }
        .site-footer p:first-child { color: var(--studio-ink); font-weight: 750; }
        @media (max-width: 760px) {
          .studio-site { width: min(1180px, 100% - 2rem); }
          .studio-site > header { flex-wrap: wrap; gap: 1rem; }
          .site-nav { width: 100%; flex-wrap: wrap; gap: 1rem; }
          .js-ready .nav-toggle { display: inline-flex; }
          .js-ready .site-nav { display: none; }
          .js-ready .site-nav[data-open="true"] { display: flex; }
          .hero { padding: 3rem 0; }
          .hero-art { max-width: 520px; width: 100%; }
        }
        @media (max-width: 460px) { .input-row { flex-direction: column; } .input-row .button { width: 100%; } }
        @media (prefers-reduced-motion: reduce) { html { scroll-behavior: auto; } }
        """;

    private const string StarterJavaScript = """
        // USER-OWNED browser code. Visual saves preserve this file.
        // Data is local to this browser. This starter has no remote API or account dependency.
        (() => {
          'use strict';
          const documentRoot = document.documentElement;
          const themeKey = 'studio-starter.theme.v1';
          // Scope each planner to this site's origin, with a readable schema version for migrations.
          const planKey = 'studio-starter.plan.v1';
          const read = (key, fallback) => { try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; } };
          const write = (key, value) => { try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch { return false; } };
          documentRoot.classList.add('js-ready');
          const themeButton = document.querySelector('.theme-toggle');
          const applyTheme = theme => {
            documentRoot.dataset.theme = theme === 'dark' ? 'dark' : 'light';
            if (themeButton) {
              themeButton.setAttribute('aria-pressed', String(theme === 'dark'));
              themeButton.textContent = theme === 'dark' ? 'Light theme' : 'Dark theme';
            }
          };
          applyTheme(read(themeKey, 'light'));
          themeButton?.addEventListener('click', () => {
            const theme = documentRoot.dataset.theme === 'dark' ? 'light' : 'dark';
            applyTheme(theme); write(themeKey, theme);
          });
          const menuButton = document.querySelector('.nav-toggle');
          const menu = document.querySelector('#site-nav');
          menuButton?.addEventListener('click', () => {
            if (!menu) return;
            const open = menu.dataset.open !== 'true';
            menu.dataset.open = String(open); menuButton.setAttribute('aria-expanded', String(open));
          });
          menu?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => {
            menu.dataset.open = 'false'; menuButton?.setAttribute('aria-expanded', 'false');
          }));
          const form = document.querySelector('#plan-form');
          const input = document.querySelector('#plan-input');
          const list = document.querySelector('#plan-list');
          const status = document.querySelector('#plan-status');
          const clearButton = document.querySelector('#clear-completed');
          if (!form || !input || !list || !status) return;
          const saved = read(planKey, []);
          let tasks = Array.isArray(saved) ? saved.filter(task => task && typeof task.id === 'string'
            && typeof task.text === 'string' && typeof task.done === 'boolean').slice(0, 100)
            .map(task => ({ id: task.id, text: task.text.slice(0, 180), done: task.done })) : [];
          let persistent = true;
          const render = () => {
            list.replaceChildren();
            tasks.forEach(task => {
              const item = document.createElement('li'); item.dataset.complete = String(task.done);
              const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.checked = task.done;
              checkbox.id = `step-${task.id}`;
              const label = document.createElement('label'); label.htmlFor = checkbox.id; label.textContent = task.text;
              const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'task-remove';
              remove.textContent = 'Remove'; remove.setAttribute('aria-label', `Remove step: ${task.text}`);
              checkbox.addEventListener('change', () => { task.done = checkbox.checked; persist(); });
              remove.addEventListener('click', () => { tasks = tasks.filter(candidate => candidate.id !== task.id); persist(); input.focus(); });
              item.append(checkbox, label, remove); list.append(item);
            });
            const complete = tasks.filter(task => task.done).length;
            status.textContent = tasks.length ? `${complete} of ${tasks.length} steps complete.` : 'Your plan is empty. Add your first step above.';
            if (!persistent) status.textContent += ' Browser storage is unavailable; this plan will last for this page session.';
            if (clearButton) clearButton.disabled = complete === 0;
          };
          const persist = () => { persistent = write(planKey, tasks); render(); };
          form.addEventListener('submit', event => {
            event.preventDefault();
            const text = input.value.trim().slice(0, 180);
            if (!text) { input.focus(); return; }
            if (tasks.length >= 100) { status.textContent = 'This starter holds up to 100 steps. Remove a step before adding another.'; return; }
            const id = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
            tasks.push({ id, text, done: false }); input.value = ''; persist(); input.focus();
          });
          clearButton?.addEventListener('click', () => { tasks = tasks.filter(task => !task.done); persist(); });
          render();
        })();
        """;

    private const string LandscapeSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 720 560" role="img" aria-labelledby="title desc">
          <title id="title">A place to begin</title><desc id="desc">A green sun above flowing blue and mint hills.</desc>
          <defs><linearGradient id="sky" x2="0" y2="1"><stop stop-color="#d7e6d5"/><stop offset="1" stop-color="#f0eadb"/></linearGradient></defs>
          <rect width="720" height="560" fill="url(#sky)"/>
          <circle cx="486" cy="171" r="93" fill="#accda7"/>
          <path d="M0 326C156 195 252 310 379 283S596 247 720 341V560H0Z" fill="#82ac9a"/>
          <path d="M0 406C139 328 223 348 357 397S595 328 720 389V560H0Z" fill="#386f76"/>
          <path d="M0 493C168 412 287 540 466 470S622 449 720 469V560H0Z" fill="#213f49"/>
          <path d="M108 303Q186 220 251 268" fill="none" stroke="#edf3e9" stroke-width="2" opacity=".5"/>
          <path d="M435 380Q550 326 646 366" fill="none" stroke="#edf3e9" stroke-width="2" opacity=".4"/>
        </svg>
        """;

    private const string MarkSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="18" fill="#246c56"/><path d="M17 43V21h8v14l14-14h8L25 43Z" fill="#ecf3e7"/></svg>
        """;

    private static string Readme(string name, StudioProjectKind kind, VisualPageProjectManifest project)
    {
        var type = kind == StudioProjectKind.Razor ? "server-rendered ASP.NET Core Razor Pages (.cshtml)" : "static HTML, CSS, and JavaScript";
        var deployment = kind == StudioProjectKind.Html
            ? "Deploy the contents of `src/Host/wwwroot` to a static web host. The root is index.html and /about/ is about/index.html. Keep the assets folder and all relative directory structure. Python or the optional .NET host can serve the same static files locally. Do not deploy visual documents or Studio recovery folders."
            : "Run `dotnet publish src/Host/Host.csproj -c Release -o publish`. Configure the published ASP.NET Core application under your production reverse proxy, use HTTPS, and choose deployment environment settings. The app needs the .NET 10 ASP.NET Core runtime (or publish self-contained for your platform). Copy the complete publish directory, including wwwroot. This starter has no database, authentication, or secret configuration.";
        var kindNotes = kind == StudioProjectKind.Razor
            ? "The Host project uses `AddRazorPages()` and `MapRazorPages()`. The generated `.cshtml` files render on the server and use `@page` routes with `Layout = null`. This is a Razor Pages project, not a Blazor application: there is no `_framework/blazor.web.js`, component circuit, or separate component library. `/api/status` is a small user-owned example server endpoint in Program.cs. To add a server-backed form, create a user-owned page and PageModel, use the framework's antiforgery support, and keep that page outside the generated Canvas outputs."
            : "The website runs without .NET; the optional Host.csproj is only a development server. For a second local option, run `python -m http.server 5180 --bind 127.0.0.1 --directory src/Host/wwwroot`. Serve from the domain root because the starter uses root-relative /assets URLs. Opening index.html with file:// will not resolve those URLs or reproduce an HTTP-hosted site.";
        var rows = string.Join("\n", project.Pages.SelectMany(page => new[]
        {
            $"| `{page.DocumentPath}` | Visual source for **{page.Name}**; edit its elements in Canvas. | Visual authoring |",
            $"| `{page.ComponentPath}` | Generated browser markup for route `{page.Route}`. | Generated; use Canvas |",
            $"| `src/Host/wwwroot/assets/{page.Slug}.generated.css` | Generated element styles for {page.Name}. | Generated; use Canvas |",
            $"| `src/Host/wwwroot/assets/{page.Slug}.custom.css` | The complete starter styling for {page.Name}. | User-owned |",
            $"| `src/Host/wwwroot/assets/{page.Slug}.custom.js` | Navigation, theme, and planner behavior for {page.Name}. | User-owned |"
        }));
        return $$"""
            # {{name}}

            A complete Hyperpage Studio starter for **{{type}}**. It includes Home and About pages, responsive navigation, locally supplied SVG artwork, a light/dark preference, accessible form controls, and a working local planner. Nothing depends on a remote image, CDN, account, or cloud service.

            ## Quick start

            1. Open this project folder in Hyperpage Studio. Each upper tab is a project; Canvas and the lower tabs are files within that project.
            2. Open the Home Canvas, change its heading and introduction, and save. Use the **?** beside any file to learn its purpose and editing ownership.
            3. Preview with Studio's build/host controls, or open PowerShell in this folder and run:

            ```powershell
            dotnet build .\src\Host\Host.csproj
            dotnet run --project .\src\Host\Host.csproj --urls http://127.0.0.1:5180
            ```

            Install the .NET 10 SDK to use the build command. Open **http://127.0.0.1:5180/** and **http://127.0.0.1:5180/about/** in a browser. If that port is occupied, choose another unused port. Stop the host with Ctrl+C. The health check at `/healthz` should return healthy.

            {{kindNotes}}

            ## Begin customizing, in this order

            1. **Content:** In Canvas, replace the brand, heading, introduction, cards, About content, and footer. Give images useful alternative text. Keep the planner IDs if you want its included script to continue working.
            2. **Appearance:** Open `src/Host/wwwroot/assets/home.custom.css`. The first :root block contains the color palette. Typography, navigation, hero, cards, planner, and phone layouts are grouped below it. Make equivalent changes in about.custom.css, or extract common rules into a user-owned shared stylesheet and link it to both pages through Studio.
            3. **Images:** Add local assets under wwwroot/assets, then set the Canvas image URL, dimensions, and alternative text. The original SVGs are editable vector source you may freely customize. Keep paths root-relative to work from nested page routes.
            4. **Interaction:** Open home.custom.js. The planner uses ordinary DOM events and `textContent`, with a bounded task list in localStorage. Adapt its handlers to your feature, change the storage key when changing its data schema, and add real backend persistence only when your application needs it.
            5. **Pages:** Use the project files menu to create another page and choose its route. Add explicit navigation links to the new route. Open the resulting visual document in Canvas and reuse the Elements/Templates library for additional objects.
            6. **Verification:** Check desktop and phone widths, keyboard Tab navigation, form labels, both page routes, the theme button, task completion/removal, reload persistence, and a browser with JavaScript disabled. Run dotnet build after server-side changes.

            ## Understand the editing contract

            The authoritative layout is the `Pages/*.visual-page.json` document. Studio generates markup and `.generated.css` from it and records their hashes. Change structure and text in **Canvas**, rather than directly editing generated HTML/CSHTML. A later visual save regenerates those outputs and detects outside changes instead of silently overwriting them.

            Files ending **.custom.css** and **.custom.js** are deliberately **user-owned**: Studio preserves them during visual saves. Program.cs and Host.csproj are also user-owned. The generated page already links the corresponding CSS and loads its script with defer. Classic browser JavaScript and C# server code are separate runtimes.

            Use project file tabs for direct source edits. Saving a source tab writes only that file; saving Canvas regenerates its managed outputs. If another tool changed a managed file, reload/review the change before regenerating. Keep the entire visual project in version control if you want to continue visual editing.

            ## Project file map

            | File | Purpose | Editing ownership |
            | --- | --- | --- |
            | `README.md` | This getting-started guide and customization map. | User-owned |
            | `visual-page.project.json` | Project ID, routes, type, theme, and asset ownership. | Studio-managed |
            {{rows}}
            | `src/Host/Host.csproj` | Ordinary .NET 10 web host project. | User-owned |
            | `src/Host/Program.cs` | HTTP host, routing, static assets, health endpoint. | User-owned |
            | `src/Host/wwwroot/app.css` | Shared base reset and fallback theme. | User-owned |
            | `src/Host/wwwroot/assets/hyperpage-theme.css` | Generated visual theme tokens. | Generated |
            | `src/Host/wwwroot/assets/studio-landscape.svg` | Local hero artwork. | User-owned |
            | `src/Host/wwwroot/assets/studio-mark.svg` | Local brand mark used by CSS. | User-owned |
            | `Shared/theme.css` | Portable visual theme source. | Studio-managed |
            | `.studio/project-guide.json` | Machine-readable per-file instructions for the ? button. Extend it for new files. | User-owned documentation |
            | `.gitignore` | Build/cache/local-file exclusions. | User-owned |

            The empty `Assets/` and `Templates/` folders are reserved for Studio-managed imported assets and reusable templates. `.visual-page/` contains temporary write coordination, recovery revisions, and preview caches; it is excluded from version control. `bin/` and `obj/` appear after a build and should not be edited.

            ## Local planner behavior

            The planner is a working demonstration, not a submission to a server. Add, complete, remove, and clear completed steps. The current browser remembers up to 100 steps, each with at most 180 characters. If browser storage is unavailable, the status explicitly says the plan lasts only for the current page session. Clear this site's browser storage to reset it. The theme preference is also stored locally.

            ## Publish

            {{deployment}}

            Before publication, replace demonstration copy/artwork, review links and alternative text, remove unwanted demo behavior, run the build, and check narrow layouts in the actual deployed site.
            """;
    }
}
