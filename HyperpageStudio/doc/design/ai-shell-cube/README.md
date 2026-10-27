# AI shell cube concept

Review-only vector exploration. The native `AiShellGlyph` remains unchanged.

- `ai-shell-current.svg` reproduces the current glyph's geometry, line widths, and opacity.
- `ai-shell-cube.svg` preserves its round shell, two orbital arms, and signal star. A three-face projected cube replaces the center scute. Its branches run left, upper-right, and lower-right, following the Comet cube's projection. Six shell seams meet the revised core.
- `ai-shell-cube-comparison.png` compares both at 192 pixels and inside 31-pixel sidebar buttons with actual 20/24-pixel glyphs. Inspect at 100% for small-size evaluation.
- Each glyph has transparent 20-, 24-, and 192-pixel PNGs rendered directly by WPF, without bitmap resizing.

Colors come from existing native controls: active `SecondaryVividBrush` is `#A3C7BD` (`App.xaml`); idle AI foreground is `#A5B7BE` (`AiLiveToggleStyle`). The cube's face tones use this same foreground with different opacities. Each SVG uses `currentColor` for straightforward recoloring.

Regenerate from a Windows PowerShell STA process:

```powershell
powershell.exe -NoProfile -STA -File .\doc\design\ai-shell-cube\Render-Concept.ps1
```

If approved for native use, add an opt-in `UseCubeCore` dependency property to `AiShellGlyph`, defaulting to `false`. Both variants can then share the exact shell/orbit/signal rendering and inherited foreground. No property or native rendering change has been made in this study.
