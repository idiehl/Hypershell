# AI shell: spiral motion, revision 02

Review-only vector revision. Original concept files in the parent folder remain intact.

`ai-shell-cube-spiral.svg` keeps the previous cube, round shell, face tones, and mint colors. The star is removed. Two broad curved ribbons sweep around opposite sides of the shell and taper into trailing points, following the same rotation. The second arm is the first rotated 180 degrees around the 24-unit glyph's center.

The comparison board `ai-shell-cube-spiral-comparison.png` shows the previous cube concept alongside this revision at 192 pixels and at native 20/24-pixel sidebar sizes. Standalone transparent PNGs are rendered by WPF at 20, 24, 192, and 512 pixels, without bitmap resizing.

- Active mint: `#A3C7BD`, existing `SecondaryVividBrush`.
- Idle mint: `#A5B7BE`, existing AI control foreground.
- Shell stroke: 1.3 units. Spiral trails: filled paths at 68% foreground opacity.
- Cube geometry, shell geometry, face opacity, and shell seams match the prior concept.

Regenerate only this revision:

```powershell
powershell.exe -NoProfile -STA -File .\doc\design\ai-shell-cube\revision-02\Render-Concept.ps1
```

No native glyph, application icon, launcher, or live application is changed.
