namespace HyperLib.Runtime;

internal static class CoreTemplateEncyclopediaRenderer
{
    public const string Component = """
        @using System.Text.Json
        @using System.Text.RegularExpressions
        @using Microsoft.AspNetCore.Hosting
        @inject IWebHostEnvironment Environment
        @inject RendererOptions Options
        @if (_entry is { } entry)
        {
            <article class="catalog-entry">
                <header><p class="catalog-category">@entry.Category</p><h1>@entry.Title</h1><code class="catalog-id">@entry.CatalogId</code><p class="catalog-description">@entry.Description</p></header>
                <section class="catalog-example" aria-label="Visual preview"><div class="catalog-label">Visual preview</div><iframe title="@(entry.Title + " preview")" src="@PreviewUrl" sandbox="" loading="lazy"></iframe><p class="catalog-note">A visual example in a page. Use this object in a Work to try its interactive tools.</p></section>
                <div class="catalog-origin"><span>From template</span> @string.Join(" · ", entry.Origins.Select(origin => origin.Title).Distinct())</div>
                <details class="catalog-details"><summary>Object details</summary><dl><dt>Use</dt><dd>@entry.Kind</dd><dt>Reference</dt><dd><code>@entry.CatalogId</code></dd><dt>Source fingerprint</dt><dd><code>@entry.SourceFingerprint</code></dd></dl><ul>@foreach (var origin in entry.Origins) { <li><strong>@origin.Title</strong><span>@origin.TemplateId</span>@if (origin.SourceLibrary is not null) { <span>@origin.SourceLibrary</span> }<code>@origin.CatalogId</code></li> }</ul></details>
                <section class="catalog-code" aria-label="Object code"><h2>Code</h2>@foreach (var source in entry.Code) { <details><summary>@source.Path</summary><pre><code>@source.Text</code></pre></details> }</section>
            </article>
        }
        else { <p role="status">This catalog entry is unavailable. Restore its bundled documentation to view it.</p> }
        @code {
            [Parameter, EditorRequired] public BlockContext Context { get; set; } = new();
            private CatalogEntry? _entry;
            private string PreviewUrl => Options.PathBase.TrimEnd('/') + "/catalog/" + Uri.EscapeDataString(_entry!.CatalogId) + "/preview/index.html";
            protected override async Task OnParametersSetAsync()
            {
                _entry = null;
                var id = Context.Text("text");
                if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")) return;
                var file = Environment.WebRootFileProvider.GetFileInfo("catalog/" + id + "/entry.json");
                if (!file.Exists || file.Length > 16 * 1024 * 1024) return;
                await using var stream = file.CreateReadStream();
                _entry = await JsonSerializer.DeserializeAsync<CatalogEntry>(stream, HyperLib.Core.ContentJson.Options);
                if (_entry?.CatalogId != id) _entry = null;
            }
            // Documentation travels with its Work and also renders against an older compatible runtime SDK.
            private sealed class CatalogEntry
            {
                public string CatalogId { get; set; } = "";
                public string Title { get; set; } = "";
                public string Description { get; set; } = "";
                public string Category { get; set; } = "";
                public string Kind { get; set; } = "";
                public string SourceFingerprint { get; set; } = "";
                public List<CatalogOrigin> Origins { get; set; } = [];
                public List<CatalogCode> Code { get; set; } = [];
            }
            private sealed class CatalogOrigin
            {
                public string CatalogId { get; set; } = "";
                public string TemplateId { get; set; } = "";
                public string Title { get; set; } = "";
                public string? SourceLibrary { get; set; }
            }
            private sealed class CatalogCode
            {
                public string Path { get; set; } = "";
                public string Text { get; set; } = "";
            }
        }
        """;

    public const string Styles = """
        .catalog-entry{max-width:1080px;margin:auto;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#243342;line-height:1.6}
        .catalog-entry header{margin-bottom:28px}.catalog-category,.catalog-label{font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:#526a7c;font-weight:700}.catalog-entry h1{font-size:clamp(26px,4vw,42px);line-height:1.2;font-weight:620;letter-spacing:-.03em;margin:10px 0 14px}.catalog-id{font-size:12px;word-break:break-all;background:#eaf0f4;border-radius:6px;padding:5px 9px}.catalog-description{max-width:78ch;color:#526374;margin:19px 0 0}
        .catalog-example{border:1px solid #d7e1e8;border-radius:14px;overflow:hidden;background:#f8fafb}.catalog-label{padding:12px 18px;border-bottom:1px solid #d7e1e8}.catalog-example iframe{display:block;width:100%;height:520px;border:0;background:white}.catalog-note{font-size:12px;color:#607383;margin:0;padding:12px 18px;border-top:1px solid #d7e1e8}
        .catalog-origin{font-size:13px;margin:22px 0;color:#3d5265}.catalog-origin>span{color:#71818f;margin-right:8px}.catalog-details{margin:18px 0 26px}.catalog-entry details{border:1px solid #d7e1e8;border-radius:8px;margin:9px 0;background:white}.catalog-entry summary{cursor:pointer;padding:13px 16px;font-size:13px;font-weight:600;overflow-wrap:anywhere}.catalog-entry summary:hover{background:#f5f8fa}.catalog-entry details[open]>summary{border-bottom:1px solid #e0e7ed}.catalog-entry dl,.catalog-entry ul{padding:12px 20px;margin:0;list-style:none}.catalog-entry dt{font-size:12px;color:#71818f;margin-top:8px}.catalog-entry dd{margin:2px 0;overflow-wrap:anywhere;font-size:13px}.catalog-entry li{display:flex;gap:12px;flex-wrap:wrap;font-size:12px;padding:8px 0;border-top:1px solid #edf1f4}.catalog-entry li code{overflow-wrap:anywhere}.catalog-code h2{font-size:17px;font-weight:600}.catalog-code pre{margin:0;padding:20px;max-height:480px;overflow:auto;background:#f8fafc;font:12px/1.65 'Cascadia Code',Consolas,monospace;tab-size:4;white-space:pre}.catalog-code code{font:inherit}
        @media(max-width:600px){.catalog-example iframe{height:420px}.catalog-id{font-size:10px}.catalog-origin{line-height:1.7}.catalog-code pre{padding:12px}}
        """;
}
