import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
PROFILE_WWWROOT = ROOT / "src" / "Hypershell.ProfileServer" / "wwwroot"
HYPERSHELL_BRAND = ROOT / "src" / "Hypershell" / "Assets" / "Brand"
HYPERSHELL_ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def create_atlas_icon() -> None:
    import base64

    # All Atlas surfaces use the approved master, preserving its color and alpha.
    brand = PROFILE_WWWROOT / "brand"
    with Image.open(brand / "atlas-emblem.png") as source:
        image = source.convert("RGBA")
    for name, size in {
        "favicon-16.png": 16,
        "favicon-32.png": 32,
        "atlas-emblem-96.png": 96,
        "apple-touch-icon.png": 180,
        "icon-192.png": 192,
        "icon-512.png": 512,
    }.items():
        image.resize((size, size), Image.Resampling.LANCZOS).save(
            brand / name, optimize=True,
        )
    shutil.copyfile(brand / "favicon-32.png", PROFILE_WWWROOT / "favicon-32x32.png")
    shutil.copyfile(brand / "apple-touch-icon.png", PROFILE_WWWROOT / "apple-touch-icon.png")
    image.save(
        PROFILE_WWWROOT / "favicon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    # Cached pages and shortcuts may still request the former SVG URL.
    encoded = base64.b64encode((brand / "atlas-emblem-96.png").read_bytes()).decode("ascii")
    (PROFILE_WWWROOT / "atlas-admin-mark.svg").write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" '
        'role="img" aria-label="Atlas Universalis">'
        f'<image width="96" height="96" href="data:image/png;base64,{encoded}" />'
        '</svg>\n',
        encoding="utf-8",
    )


def create_hypershell_icon(preview_directory: Path | None = None) -> None:
    # Rasterize the approved vector at every native size; the renderer also
    # checks the shared in-app glyph against the canonical SVG.
    with tempfile.TemporaryDirectory(prefix="HypershellBrand-") as temporary:
        scratch = Path(temporary)
        output = preview_directory.resolve() if preview_directory else scratch / "icons"
        output.mkdir(parents=True, exist_ok=True)
        subprocess.run(
            ["dotnet", "run", "--project", str(ROOT / "tools" / "Hypershell.BrandRenderer" / "Hypershell.BrandRenderer.csproj"),
             "-c", "Release", "--artifacts-path", str(scratch / "artifacts"),
             "--", str(ROOT), str(output)],
            check=True,
        )
        shutil.copyfile(output / "hypershell.ico", HYPERSHELL_BRAND / "hypershell.ico")
    print(f"Packaged spiral shell: {', '.join(map(str, HYPERSHELL_ICON_SIZES))} px")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Package the repository's brand icons.")
    parser.add_argument(
        "--hypershell-only",
        action="store_true",
        help="Package only Hypershell, leaving ProfileServer/Atlas assets untouched.",
    )
    parser.add_argument(
        "--preview-directory",
        type=Path,
        help="Also save each Hypershell icon size as a PNG for visual inspection.",
    )
    args = parser.parse_args()
    if not args.hypershell_only:
        create_atlas_icon()
    create_hypershell_icon(args.preview_directory)
