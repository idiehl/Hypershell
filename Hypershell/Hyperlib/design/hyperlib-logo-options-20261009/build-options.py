"""Rebuild the review board from ten small, editable vector concepts."""
from pathlib import Path
import html
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
TRAIL_A = "M 17 5.8 C 21.7 7.2 23.7 12.8 20.8 17.1 C 17.9 21.4 10.9 22.9 2.7 20.3 C 12.2 22.5 19.2 18.4 20.5 13.8 C 21.7 9.8 19.8 6.9 17 5.8 Z"
TRAIL_B = "M 7 18.2 C 2.3 16.8 .3 11.2 3.2 6.9 C 6.1 2.6 13.1 1.1 21.3 3.7 C 11.8 1.5 4.8 5.6 3.5 10.2 C 2.3 14.2 4.2 17.1 7 18.2 Z"
SHELL = "M 12 3.8 C 7.4 3.8 4.8 7.1 4.8 11.9 C 4.8 16.7 7.5 20.2 12 20.2 C 16.5 20.2 19.2 16.7 19.2 11.9 C 19.2 7.1 16.6 3.8 12 3.8 Z"
BOOK = "M 12 9.6 C 10.8 8.65 9.4 8.25 7.6 8.5 L 7.6 14.4 C 9.4 14.15 10.8 14.55 12 15.5 C 13.2 14.55 14.6 14.15 16.4 14.4 L 16.4 8.5 C 14.6 8.25 13.2 8.65 12 9.6 Z M 12 9.6 L 12 15.5"
LEFT_PAGE = "M 12 9.6 C 10.8 8.65 9.4 8.25 7.6 8.5 L 7.6 14.4 C 9.4 14.15 10.8 14.55 12 15.5 Z"
RIGHT_PAGE = "M 12 9.6 C 13.2 8.65 14.6 8.25 16.4 8.5 L 16.4 14.4 C 14.6 14.15 13.2 14.55 12 15.5 Z"

def filled(d, opacity=1):
    return f'<path d="{d}" fill="currentColor" fill-opacity="{opacity}"/>'

def outlined(d, width=1.15, opacity=1):
    return f'<path d="{d}" fill="none" stroke="currentColor" stroke-width="{width}" stroke-opacity="{opacity}"/>'

def trails(opacity=.68):
    return filled(TRAIL_A, opacity) + filled(TRAIL_B, opacity)

def shell(opacity=.10):
    return filled(SHELL, opacity) + outlined(SHELL)

def book():
    return filled(LEFT_PAGE, .18) + filled(RIGHT_PAGE, .08) + outlined(BOOK)

concepts = [
    ("Shell book", "shell-book", "The Hypershell shell and paired spiral trails, with an open book replacing the cube.", "Closest family resemblance", trails() + shell() + book()),
    ("Orbit book", "orbit-book", "An open book framed by the shared spiral motion. Removing the shell ring gives the book more room.", "Lighter silhouette", trails() + '<g transform="translate(-1.8 -1.8) scale(1.15)">' + book() + '</g>'),
    ("Shell pages", "shell-pages", "The shell itself becomes two curved pages. A central spine turns the familiar oval into a library mark.", "Most integrated", trails(.58) + filled(SHELL, .10) + outlined(SHELL) + outlined("M 12 4.2 L 12 19.7 M 5.1 8.4 C 8.3 7.8 10.4 8.1 12 9.7 C 13.6 8.1 15.7 7.8 18.9 8.4 M 5.1 15.4 C 8.3 14.8 10.4 15.1 12 16.7 C 13.6 15.1 15.7 14.8 18.9 15.4", 1.05)),
    ("Shell folio", "shell-folio", "A single upright volume in the shared shell. Its folded corner recalls the facets in Hypershell's cube.", "Quiet document cue", trails(.58) + shell(.08) + filled("M 8.4 7.2 L 13 7.2 L 15.6 9.8 L 15.6 16.6 L 8.4 16.6 Z", .15) + outlined("M 8.4 7.2 L 13 7.2 L 15.6 9.8 L 15.6 16.6 L 8.4 16.6 Z M 13 7.2 L 13 9.8 L 15.6 9.8 M 10.5 12.2 L 13.5 12.2 M 10.5 14.3 L 13.5 14.3", 1.05)),
    ("Shell H", "shell-h", "A restrained H inside the spiral shell, using the same rounded outline and mint as Hypershell.", "Direct monogram", trails(.58) + shell(.08) + outlined("M 8.6 8.1 L 8.6 15.9 M 15.4 8.1 L 15.4 15.9 M 8.6 12 L 15.4 12", 1.6)),
    ("Linked leaves", "linked-leaves", "Two overlapping page facets sit inside the orbit. Their angular overlap comes from the shared cube.", "Hypertext connection", trails(.58) + filled("M 7.1 8.1 L 12 5.8 L 15.2 7.5 L 15.2 14.8 L 10.3 17.2 L 7.1 15.5 Z", .12) + filled("M 10.3 9.2 L 15.2 6.8 L 18.3 8.5 L 18.3 15.8 L 13.4 18.2 L 10.3 16.5 Z", .18) + outlined("M 7.1 8.1 L 12 5.8 L 15.2 7.5 L 15.2 14.8 L 10.3 17.2 L 7.1 15.5 Z M 7.1 8.1 L 10.3 9.8 L 15.2 7.5 M 10.3 9.8 L 10.3 17.2 M 10.3 9.2 L 15.2 6.8 L 18.3 8.5 L 18.3 15.8 L 13.4 18.2 L 10.3 16.5 M 13.4 10.2 L 18.3 8.5 M 13.4 10.2 L 13.4 18.2", .95)),
    ("Library shell", "library-shell", "Three simple book spines nest inside the familiar shell. A compact shelf suggests an entire collection.", "Collection first", trails(.58) + shell(.08) + filled("M 7.5 9.1 L 10.1 9.1 L 10.1 15.8 L 7.5 15.8 Z M 10.1 7.5 L 12.8 7.5 L 12.8 15.8 L 10.1 15.8 Z M 13.2 8.6 L 15.7 8 L 17.4 15.2 L 14.8 15.8 Z", .15) + outlined("M 7.5 9.1 L 10.1 9.1 L 10.1 15.8 L 7.5 15.8 Z M 10.1 7.5 L 12.8 7.5 L 12.8 15.8 L 10.1 15.8 Z M 13.2 8.6 L 15.7 8 L 17.4 15.2 L 14.8 15.8 Z", .95)),
    ("Turning pages", "turning-pages", "An open book uses two sweeping leaves to echo Hypershell's paired motion trails without the enclosing shell.", "Clear at small sizes", filled("M 3.8 7.1 C 7.3 6.7 9.7 7.6 12 9.4 L 12 18.2 C 9.5 16.6 7.2 16 3.8 16.4 Z", .12) + filled("M 12 9.4 C 14.3 7.6 16.7 6.7 20.2 7.1 L 20.2 16.4 C 16.8 16 14.5 16.6 12 18.2 Z", .08) + outlined("M 3.8 7.1 C 7.3 6.7 9.7 7.6 12 9.4 C 14.3 7.6 16.7 6.7 20.2 7.1 L 20.2 16.4 C 16.8 16 14.5 16.6 12 18.2 C 9.5 16.6 7.2 16 3.8 16.4 Z M 12 9.4 L 12 18.2", 1.15) + filled("M 5.1 5.1 C 9.1 3.4 14.9 3.7 18.5 6.1 C 13.9 4.4 8.8 4.8 5.1 7.4 Z", .65) + filled("M 18.9 19.1 C 14.9 20.8 9.1 20.5 5.5 18.1 C 10.1 19.8 15.2 19.4 18.9 17 Z", .65)),
    ("Open shell", "open-shell", "A pared back circular shell opens into an orbit around the book. One continuous sweep keeps the mark airy.", "Fewest moving parts", outlined("M 18.4 6.5 C 15.5 3 10 2.9 6.8 6 C 3.4 9.3 3.4 14.7 6.6 18 C 9.2 20.8 13.8 21 17.3 18.8 C 20.5 16.8 22 13 20.8 9.8", 1.4) + filled("M 20.8 9.8 C 20 7.5 18.4 5.9 16.4 5.1 C 19.7 5.9 22.1 8.7 22.3 12.1 C 21.9 11.4 21.4 10.6 20.8 9.8 Z", .65) + book()),
    ("Volume cube", "volume-cube", "Hypershell's three face cube becomes a dimensional volume. Its central seam reads like a book spine.", "Strong geometric family", trails(.58) + filled("M 6.9 9.4 L 12 6.5 L 17.1 9.4 L 12 12.3 Z", .62) + filled("M 6.9 9.4 L 12 12.3 L 12 18.1 L 6.9 15.2 Z", .12) + filled("M 12 12.3 L 17.1 9.4 L 17.1 15.2 L 12 18.1 Z", .32) + outlined("M 6.9 9.4 L 12 6.5 L 17.1 9.4 L 17.1 15.2 L 12 18.1 L 6.9 15.2 Z M 6.9 9.4 L 12 12.3 L 17.1 9.4 M 12 12.3 L 12 18.1 M 8.6 13.5 L 10.4 14.5 M 13.6 14.5 L 15.4 13.5", 1.05)),
]

def svg(name, artwork, inline=False):
    color = "" if inline else ' style="color:#A3C7BD"'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24"{color} stroke-linecap="round" stroke-linejoin="round" role="img" aria-label="Hyperlib {html.escape(name)}"><title>Hyperlib concept: {html.escape(name)}</title>{artwork}</svg>'

cards = []
data = []
for index, (name, slug, description, character, artwork) in enumerate(concepts, 1):
    number = f"{index:02}"
    source = svg(name, artwork)
    ET.fromstring(source)
    (ROOT / f"{number}-{slug}.svg").write_text(source + "\n", encoding="utf-8")
    mark = svg(name, artwork, True)
    data.append({"number": number, "name": name, "description": description, "svg": mark})
    sizes = "".join(f'<span class="size"><i style="width:{size}px;height:{size}px">{mark}</i><small>{size}</small></span>' for size in (16, 20, 24, 32))
    cards.append(f'''<button class="concept{' active' if index == 3 else ''}" data-index="{index - 1}" aria-pressed="{'true' if index == 3 else 'false'}">
      <span class="card-heading"><span class="number">{number}</span><strong>{name}</strong></span>
      <span class="samples"><span class="dark-sample">{mark}</span><span class="light-sample">{mark}</span></span>
      <span class="sizes">{sizes}</span><span class="character">{character}</span><span class="description">{description}</span></button>''')

page = '''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Hyperlib · ten logo directions</title><style>
*{box-sizing:border-box}body{margin:0;background:#0f1519;color:#e5ecec;font:14px/1.5 'Segoe UI',system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:40px 40px 28px}.eyebrow{font-size:11px;letter-spacing:2px;color:#A3C7BD;text-transform:uppercase;margin:0 0 10px}h1{font-size:32px;line-height:1.2;font-weight:550;letter-spacing:-.7px;margin:0 0 12px}.intro{max-width:840px;color:#96a9b0;margin:0 0 24px}.reference{display:flex;align-items:center;gap:14px;color:#95a9af;font-size:12px}.reference svg{width:30px;height:30px;color:#A3C7BD;flex:none}.reference strong{color:#d0dcd9;font-weight:550}.top{display:flex;align-items:flex-start;justify-content:space-between;gap:36px;margin-bottom:24px}.swatch{display:inline-block;width:8px;height:8px;background:#A3C7BD;border-radius:50%;margin-right:6px}.toolbar-wrap{margin:0 0 28px;border:1px solid #2b373e;border-radius:10px;overflow:hidden}.toolbar{height:42px;padding-left:10px;background:#181f25;display:flex;align-items:center;gap:4px;color:#c4d5d6}.nav{width:30px;display:inline-flex;justify-content:center;font-size:21px;font-weight:300;color:#afc1c8}.toolbar-mark{width:24px;height:24px;margin:0 14px 0 10px;color:#A3C7BD}.toolbar-mark svg{display:block;width:100%;height:100%}.mode{display:inline-flex;align-items:center;gap:10px;font-size:12px;color:#b1c1c7}.switch{width:26px;height:14px;border:1px solid #657978;border-radius:10px;display:inline-flex;align-items:center;padding:2px}.switch:before{content:'';display:block;width:8px;height:8px;border-radius:50%;background:#93aaa7}.spacer{flex:1}.windows{display:flex;height:100%;font-size:17px}.windows span{display:flex;align-items:center;justify-content:center;width:44px}.preview-meta{padding:12px 16px;border-top:1px solid #28353b;display:flex;align-items:center;gap:14px;background:#121a1f}.preview-meta strong{font-weight:550;white-space:nowrap;font-size:12px;color:#A3C7BD}.preview-meta span{font-size:12px;color:#8fa4ad}.grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:14px}.concept{appearance:none;cursor:pointer;padding:0;text-align:left;border-radius:10px;overflow:hidden;color:inherit;background:#151e23;border:1px solid #29363d;font:inherit;display:flex;flex-direction:column;transition:border-color .12s,background .12s}.concept:hover{border-color:#708b86;background:#1a252b}.concept.active{border-color:#A3C7BD;box-shadow:0 0 0 1px #A3C7BD28}.concept:focus-visible{outline:2px solid #A3C7BD;outline-offset:3px}.card-heading{display:flex;align-items:center;gap:11px;padding:15px 14px 13px}.number{font:11px 'Consolas',monospace;color:#73928c}.card-heading strong{font-size:13px;font-weight:550}.samples{display:grid;grid-template-columns:1fr 1fr;height:104px}.dark-sample,.light-sample{display:flex;align-items:center;justify-content:center}.dark-sample{background:#0e161a;color:#A3C7BD}.light-sample{background:#e5ece7;color:#416a61}.samples svg{width:64px;height:64px}.sizes{display:flex;align-items:flex-end;justify-content:space-around;height:73px;padding:13px 15px 9px;border-bottom:1px solid #25343a;color:#A3C7BD}.size{display:flex;flex-direction:column;align-items:center;justify-content:flex-end;gap:6px;min-width:28px}.size i{display:block}.size svg{display:block;width:100%;height:100%}.size small{font:9px 'Consolas',monospace;color:#7e959d}.character{padding:14px 14px 5px;font-size:11px;color:#A3C7BD}.description{padding:0 14px 17px;font-size:12px;line-height:1.5;color:#94a8b0;min-height:92px}.footer{margin-top:25px;display:flex;gap:24px;justify-content:space-between;color:#748c95;font-size:11px}.footer strong{font-weight:500;color:#a9c2bb}.footer a{color:#A3C7BD;text-decoration:none}.footer a:hover{text-decoration:underline}@media(max-width:1180px){.grid{grid-template-columns:repeat(3,minmax(0,1fr))}.top{display:block}.reference{margin-top:16px}}@media(max-width:760px){main{padding:24px 18px}.grid{grid-template-columns:repeat(2,minmax(0,1fr))}.preview-meta{align-items:flex-start;flex-direction:column;gap:4px}.footer{flex-direction:column;gap:8px}h1{font-size:28px}}@media(max-width:430px){.grid{grid-template-columns:1fr}.mode{gap:6px}.windows span{width:28px}.toolbar-mark{margin-right:8px}}
</style></head><body><main><div class="top"><div><p class="eyebrow">Hyperlib / identity exploration / 09 October 2026</p><h1>Ten directions. One family.</h1><p class="intro">Restrained library marks built from Hypershell's approved spiral shell, curved motion, and geometric facets. Each option is shown on dark and light surfaces, with real toolbar sizes below. Select an option to preview it in the compact desktop bar.</p></div><div class="reference">REFERENCE_SVG<div><strong>Hypershell reference</strong><br>Approved spiral shell + cube<br><span class="swatch"></span>Shared mint · #A3C7BD</div></div></div>
<div class="toolbar-wrap"><div class="toolbar" aria-label="Compact Hyperlib toolbar preview"><span class="nav">←</span><span class="nav">→</span><span class="toolbar-mark" id="toolbar-mark"></span><span class="mode">Edit Mode <span class="switch"></span></span><span class="spacer"></span><span class="windows"><span>−</span><span>□</span><span>×</span></span></div><div class="preview-meta"><strong id="preview-title"></strong><span id="preview-description"></span></div></div>
<div class="grid">CARDS</div><div class="footer"><span><strong>Selected: 03 · Shell pages.</strong> The native toolbar, web mark, and MyNet icon now use this direction.</span><span>Editable SVGs · monochrome · 24-unit view boxes · <a href="contact-sheet.svg">Contact sheet</a></span></div></main><script>
const concepts = CONCEPTS;
function select(index){const c=concepts[index];document.getElementById('toolbar-mark').innerHTML=c.svg;document.getElementById('preview-title').textContent=c.number+' · '+c.name;document.getElementById('preview-description').textContent=c.description;document.querySelectorAll('.concept').forEach((card,i)=>{card.classList.toggle('active',i===index);card.setAttribute('aria-pressed',String(i===index));});}
document.querySelectorAll('.concept').forEach(card=>card.addEventListener('click',()=>select(Number(card.dataset.index))));select(2);
</script></body></html>'''
reference = (ROOT.parents[2] / "src/Hypershell/Assets/Brand/hypershell-spiral-shell.svg").read_text(encoding="utf-8").strip()
page = page.replace("REFERENCE_SVG", reference).replace("CARDS", "\n".join(cards)).replace("CONCEPTS", json.dumps(data, ensure_ascii=False))
(ROOT / "index.html").write_text(page, encoding="utf-8")

sheet = ['<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="930" viewBox="0 0 1600 930"><rect width="1600" height="930" fill="#0f1519"/><g font-family="Segoe UI, sans-serif">', '<text x="48" y="49" font-size="12" letter-spacing="2" fill="#A3C7BD">HYPERLIB · LOGO EXPLORATION</text><text x="48" y="90" font-size="30" font-weight="550" fill="#e5ecec">Ten directions. One family.</text><text x="48" y="118" font-size="14" fill="#94a8b0">Hypershell spiral shell, library cues, and shared mint. Draft concepts for review.</text>']
for index, (name, slug, description, character, artwork) in enumerate(concepts):
    x = 48 + (index % 5) * 306
    y = 154 + (index // 5) * 358
    sheet.append(f'<rect x="{x}" y="{y}" width="280" height="330" rx="10" fill="#151e23" stroke="{("#A3C7BD" if index == 2 else "#29363d")}"/><text x="{x+16}" y="{y+27}" font-size="12" fill="#73928c">{index+1:02}</text><text x="{x+48}" y="{y+27}" font-size="14" font-weight="550" fill="#e5ecec">{name}</text><rect x="{x+1}" y="{y+43}" width="139" height="137" fill="#0e161a"/><rect x="{x+140}" y="{y+43}" width="139" height="137" fill="#e5ece7"/>')
    for offset, tint in ((30, "#A3C7BD"), (170, "#416a61")):
        sheet.append(f'<g transform="translate({x+offset} {y+72}) scale(3.3333)" color="{tint}" stroke-linecap="round" stroke-linejoin="round">{artwork}</g>')
    for j, size in enumerate((16, 20, 24, 32)):
        sx = x + 38 + j * 59
        sheet.append(f'<g transform="translate({sx-size/2} {y+222-size}) scale({size/24})" color="#A3C7BD" stroke-linecap="round" stroke-linejoin="round">{artwork}</g><text x="{sx}" y="{y+242}" text-anchor="middle" font-size="10" fill="#73928c">{size}</text>')
    sheet.append(f'<text x="{x+16}" y="{y+272}" font-size="12" fill="#A3C7BD">{character}</text>')
    # The contact sheet keeps explanations short; the interactive board carries full rationale.
    sheet.append(f'<text x="{x+16}" y="{y+297}" font-size="11" fill="#94a8b0">{slug.replace("-", " ").title()} · vector concept</text>')
sheet.append('<text x="48" y="901" font-size="12" fill="#8ba69e">Selected: 03 · Shell pages. Shared shell, spiral trails, and curved library pages.</text></g></svg>')
(ROOT / "contact-sheet.svg").write_text("".join(sheet), encoding="utf-8")

readme = "# Hyperlib logo concepts\n\nTen vector concepts for the compact native Hyperlib toolbar. The user selected **03, Shell pages**. These use the actual approved Hypershell spiral shell as the family reference, its mint `#A3C7BD`, and an open book or other library cue. The other nine concepts remain preserved as design history.\n\nOpen `index.html` for dark/light comparisons, 16/20/24/32 px specimens, and an interactive toolbar preview. `contact-sheet.svg` is a portable static comparison. Each numbered SVG is self-contained, transparent, and editable with a 24-unit view box. SVGs use `currentColor`; their default is brand mint.\n\n"
for index, (name, slug, description, character, artwork) in enumerate(concepts, 1):
    readme += f"{index}. **{name}** (`{index:02}-{slug}.svg`) — {description}\n"
readme += "\n**Selected: 03, Shell pages.** The shell itself becomes two curved pages with a central spine. The production native toolbar, web logo/favicon, and MyNet icon use this selected artwork.\n\nThe native selected resource `src/Hypershell/Assets/Brand/hyperlib-shell-pages.xaml` mirrors option 03 as `DrawingImage` key `HyperlibLogo` and uses its scoped `HyperlibBrandBrush` in fixed brand mint `#A3C7BD`. The earlier `hyperlib-shell-book.xaml` draft remains preserved as design history.\n\nSource references: `src/Hypershell/Assets/Brand/hypershell-spiral-shell.svg`, its accompanying brand documentation, and the previous `Hyperlib/src/HyperLib.Host/wwwroot/favicon.svg` open-book mark.\n\nRebuild this board by running `build-options.py` with Python.\n"
(ROOT / "README.md").write_text(readme, encoding="utf-8")
print(f"Created {len(concepts)} SVG concepts, HTML board, contact sheet, and README in {ROOT}")
