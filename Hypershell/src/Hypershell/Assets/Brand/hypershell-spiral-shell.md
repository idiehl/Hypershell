# Hypershell spiral shell

The user approved **AI shell revision 02** on September 28, 2026 as the shared logo geometry. A projected three-face cube sits inside a round shell. Two tapered spiral trails sweep in the same rotational direction on opposite sides. There is no star. On October 9, 2026, the user updated the palette: the cube's upper-left face is red (`#EF5D66`), its lower-left face is green (`#55C98A`), and its right face is blue (`#4B8EF1`). All remaining artwork uses the original mint (`#A3C7BD`). The original geometry and non-face layer opacities are preserved; all three cube faces are opaque.

## Canonical artwork

- `hypershell-spiral-shell.svg` preserves the ten paths from the approved native-vector concept, `doc/design/ai-shell-cube/revision-02/ai-shell-cube-spiral.svg`, with the current fixed mint and RGB palette.
- `hypershell-spiral-shell.png` is the transparent 512 × 512 rendering of the actual shared `Controls/AiShellGlyph.cs` control, checked pixel for pixel against the canonical SVG renderer.
- The SVG has explicit colors. The native control retains its inherited `Foreground` dependency property for XAML and API compatibility, but renders the fixed brand palette regardless of active or idle theme tint. Mint backing and motion layers retain the original transparency.

The drawing has a 24-unit view box, ten ordered layers, round line joins and caps, a 1.3-unit shell outline, and individually applied layer opacities. Native geometries are frozen and need no SVG parsing dependency at runtime. The standalone renderer verifies the native drawing against the canonical SVG to detect divergence. Per-brush layer opacity preserves the original WPF treatment of trails, backing, and line work.

## Direct vector generation

On Windows with .NET 10 installed, from the repository root:

```powershell
powershell.exe -NoProfile -File .\tools\Render-SpiralShellIcons.ps1
```

Optional `-OutputDirectory` and `-ArtifactsPath` keep generated files and build outputs separate from other builds. The default output is `output/spiral-shell-20260928/icons`; the default standalone tool build uses `output/.brand-renderer-artifacts`. This builds only the small renderer and linked `AiShellGlyph` source, not the native application.

The underlying CLI is:

```powershell
dotnet run --project tools/Hypershell.BrandRenderer/Hypershell.BrandRenderer.csproj -c Release --artifacts-path output/.brand-renderer-artifacts -- <repository-root> <output-directory>
```

The tool renders native 16, 20, 24, 32, 40, 48, 64, 128, 192, 256, and 512-pixel PNGs. Every size is rasterized directly from the vector; none is resized from another PNG. The 512-pixel file also refreshes the canonical brand PNG. Nine Windows ICO frames—16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels—are packaged byte for byte as 32-bit RGBA PNG payloads. The tool does not overwrite the application's `hypershell.ico`; installation or the packaging entry point performs that step.

## Verification

- The actual native control matches independent SVG rendering at all eleven sizes with both active and idle inherited tint inputs. Both inputs produce identical pixels because the palette is fixed.
- The renderer verifies the SVG palette and samples the three rendered face interiors to confirm exact opaque RGB colors. Every non-face SVG fill or stroke must use the original mint.
- When the historical revision 02 SVG is available, the renderer compares every path, stroke width, line cap/join, and non-face opacity to confirm geometry preservation. When unavailable, it explicitly reports the skipped historical check. The old monochrome PNGs are preserved history and are no longer expected to match current colors. Canonical native/SVG pixel parity is always required.
- Each ICO frame is identical to its directly rendered PNG, and all frame corners are transparent.
- The native control keeps its natural 24 × 24 size and public `AiShellGlyph`/`Foreground` API.
- Initial approved ICO SHA-256: `EECCD252F0E1480E28C87882CC0ED7373ECC5008D4D2A63A9CB117B19C8D8D50`.
- Original monochrome SVG SHA-256: `93C1B6696B414218BA2738A2FA1162836C30FF7AAA12B26630A248DB788C2D0A`. Current asset hashes are printed by each renderer run rather than maintained here.

Earlier Comet, cube revision 01, and other icon exploration assets remain preserved as design history.
