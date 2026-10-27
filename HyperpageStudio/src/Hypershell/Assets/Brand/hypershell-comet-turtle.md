Archived design. The active Hypershell identity is now `hypershell-spiral-shell.svg`; see `hypershell-spiral-shell.md`.

# Hypershell Comet Turtle icon

The user selected original design **#26 Comet Turtle** on September 27, 2026. It depicts an orange turtle with a central cream/gold cube, swept flippers, and two curved flame trails on a navy rounded-square tile. This replaces the folded H as the source for the Windows application and stable launcher icon. The later palette and propulsion refinements are not used.

## Provenance

- Created September 26, 2026 with the built-in `image_gen` tool using the shell concept and cube-geometry references.
- Selected original: `I:\dev\Hypershell\output\icon-design-variants-20260926\26-comet-turtle.png`.
- Prompt metadata: `I:\dev\Hypershell\output\icon-design-variants-20260926\prompts.json`, concept 26.
- Reference inputs: `reference-cube-shell.png` and `reference-cube-geometry.png` in that exploration folder.
- Repository source: `hypershell-comet-turtle.png`, copied byte for byte from the selected original.
- Source: 1254 × 1254 RGBA PNG, transparent exterior.
- Source SHA-256: `BE3A27D5BD471822A8271801813E26CC581AE5158850B804BD900BF4865207A3`.
- ICO SHA-256: `F342E592C65EF10C0BC7F9B6565CDBF08F655D2C8FC2458A70DA39C912349C96`.
- ICO sizes: 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels; 32-bit RGBA PNG frames.

The packaging step performs only mechanical LANCZOS resizing and ICO encoding. It does not redraw, crop, recolor, alter the composition, or flatten transparency. The original folded H, motion-shell, and legacy turtle-shell PNGs remain preserved as design history.

## Repackage

From the repository root with Python and Pillow installed:

```powershell
python tools/generate-brand-icons.py --hypershell-only
```

To include per-size PNG previews:

```powershell
python tools/generate-brand-icons.py --hypershell-only --preview-directory I:\dev\Hypershell\output\comet-icon-20260927\previews
```

`--hypershell-only` leaves ProfileServer/Atlas assets unchanged. All nine ICO frames and preview PNGs were checked pixel for pixel against a resize of the selected source. Every frame retains a 0–255 alpha range. Source corners are fully transparent; normal LANCZOS interpolation gives the 20-pixel frame corner alpha 1/255, with zero at the corners of the other sizes. No alpha correction is applied.

Copying or generating these repository assets does not update an installed application; build and launcher installation are separate steps.

## Original generation prompt

```text
Use case: logo-brand concept variation. Create ONE new Hypershell app-icon design. Input image 1 is a CONCEPT REFERENCE only: turtle-shell geometry with a central cube. Input image 2 is the user's red-line GEOMETRY GUIDE for the cube. This request is a genuinely different construction and silhouette as described below, NOT just a recolor of the reference. The invariant is the turtle/shell concept plus a central hexagonal plate perceived as a SOLID CUBE PROJECTING OUTWARD. The guide's three branches run from the central corner horizontally LEFT, diagonally UP-RIGHT and diagonally DOWN-RIGHT to alternating vertices of a flat-topped hexagon. This gives three four-sided faces: upper-left, lower-left and right. Use clear face shades and/or clean seams along those same directions. The red strokes are guides, not required colors. Keep the cube's original flat-top/flat-bottom hex silhouette and do not rotate it independently of its shell. Surrounding silhouette, shell construction, anatomy, effects, depth and composition should change substantially according to this variant. The orbit ring is gone. NO MINT GREEN or seafoam anywhere. Keep the result compact, memorable and readable as a Windows taskbar icon: a few broad shapes, clear silhouette and bold contrast. One square icon on a dark rounded-square tile that fills about94% of the image; genuinely transparent exterior beyond the rounded corners. No words, letters, number labels, watermark, mockup, icon sheet or background scenery.
Distinct design: A streamlined full turtle swimming diagonally toward the upper right, with a visible head, two long swept flippers and short hind legs. The carapace is narrow and aerodynamic with the outward cube integrated into its center. Two broad tapered flame streams emerge behind its tail toward the lower left, forming a graphic comet wake. Combine recognizable turtle anatomy with propulsion and a strong diagonal composition. Use a red-orange shell and cream/gold cube faces, with dark red flame bases. No ring, no separate space objects. Rotate the cube together with the animal while preserving the relative three-face geometry.
Palette: Vermilion, burnt red, pale gold and warm ivory against deep navy.
Treatment: Fast dynamic flat emblem with two sculpted fire trails.
```
