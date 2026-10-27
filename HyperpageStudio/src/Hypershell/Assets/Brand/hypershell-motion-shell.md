Archived design. The active Hypershell identity is now `hypershell-spiral-shell.svg`; see `hypershell-spiral-shell.md`.

# Hypershell motion-shell icon

Created 2026-09-08 for the Hypershell app/taskbar icon refresh. The original
generated image is preserved byte-for-byte as `hypershell-motion-shell.png`.
The legacy turtle-shell PNGs remain available; `hypershell.ico` is the new
multi-resolution Windows package.

## Provenance

- Mode: built-in `image_gen` tool; no CLI/API fallback.
- Original output: `C:\Users\ihigg\.codex\generated_images\01a0844e-999a-7ec0-b504-4ac03c48ddfc\exec-1408ce4a-8cb0-4f02-8292-00abc343a337.png`
- Source: 1254 x 1254 RGBA PNG, genuinely transparent exterior.
- Source SHA-256: `D804F47FB37AC2F26DFD735DD2B7EF7C5B9F63165861F6D226EE973810651E01`
- ICO SHA-256: `4C33F8B2FB773E9E9A5689114AEF6EA8ADF7ED0816137947755C59B3A9F2D8FA`
- ICO sizes: 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels.

The packaging step only resizes/encodes the supplied artwork. It does not
redraw, crop, recolor, remove transparency, or synthesize any part of the icon.

## Repackage

Run from the repository root with Python and Pillow installed:

```powershell
py -3 tools/generate-brand-icons.py --hypershell-only
```

Optional per-size PNG previews:

```powershell
py -3 tools/generate-brand-icons.py --hypershell-only --preview-directory I:\dev\.codex-build\hypershell-brand-css\qa\brand
```

`--hypershell-only` leaves ProfileServer/Atlas assets unchanged. Source alpha
extrema and all nine encoded ICO frames were checked for the full 0-255 range;
all four source corners are transparent. Source plus 16/32/48/128-pixel previews
were visually inspected for shell-pattern and motion-mark readability.

## Final generation prompt

```text
Use case: logo-brand
Asset type: production bitmap app icon for Hypershell, a sleek dark Windows desktop creative/coding workspace. One square icon only, not an icon sheet or mockup.
Primary request: replace a turtle shell with blue flames with a refined turtle-shell-patterned background and a simple visually striking motion mark.
Scene/backdrop: a single softly rounded square dark graphite/deep forest-teal tile, with genuinely transparent exterior pixels outside its rounded corners. Tile fills about 94% of the square canvas. No backdrop, shadow floor, checkerboard drawing, or surrounding scene.
Subject: broad geometric turtle-shell scutes form the tile's background, one large central scute and a restrained ring of large neighboring plates, separated by subtle lighter teal seams. Across this shell-pattern tile, a bold warm ivory-to-pale-mint swept orbital arc flows diagonally and tapers cleanly, a simple aerodynamic forward gesture. The sweeping motion mark must be the high-contrast focal silhouette; pattern is secondary but recognizable.
Style/medium: polished minimal software app branding, graphic almost-flat shapes, beautifully controlled very subtle material shading only, precise antialiased clean edges, strong composition and balanced negative space. Calm, modern, cohesive, premium; no decorative microdetail.
Composition/framing: front-facing square icon, centered. Must remain legible at 16, 24, 32, and 48 pixels. Keep the broad shell seams readable and the foreground mark thick and simple, not a tangle of thin strokes.
Color palette: graphite, deep green/teal, muted sage shell seams, warm ivory and pale mint foreground. Restrained saturation.
Constraints: actual transparent background; no text, letters, glyphs, watermark, mascot, turtle head, legs, creature silhouette, blue flames, fire, galaxy, sparkles, lens flare, neon glow, glass orb, or busy illustration. Original mark, not an existing brand logo.
```
