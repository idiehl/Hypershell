"""Recreate the original, local demonstration media. Requires Pillow and imageio-ffmpeg."""
from pathlib import Path
import math
import struct
import subprocess
import wave
from PIL import Image, ImageDraw
import imageio_ffmpeg

root = Path(__file__).resolve().parents[1]
media = root / "Media"
media.mkdir(exist_ok=True)
with wave.open(str(media / "sample-tone.wav"), "wb") as audio:
    audio.setnchannels(1)
    audio.setsampwidth(2)
    audio.setframerate(22050)
    audio.writeframes(b"".join(struct.pack("<h", round(1700 * math.sin(2 * math.pi * 220 * i / 22050) * min(1, i / 2205, (44100 - i) / 2205))) for i in range(44100)))

width, height, fps = 640, 360, 24
process = subprocess.Popen([
    imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-f", "rawvideo", "-vcodec", "rawvideo",
    "-s", f"{width}x{height}", "-pix_fmt", "rgb24", "-r", str(fps), "-i", "-", "-an",
    "-c:v", "libvpx-vp9", "-b:v", "140k", "-pix_fmt", "yuv420p", str(media / "sample-motion.webm")
], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
for frame in range(fps * 3):
    image = Image.new("RGB", (width, height), "#17283d")
    draw = ImageDraw.Draw(image)
    for x in range(0, width, 40):
        draw.line((x, 0, x, height), fill="#253d54")
    for y in range(0, height, 40):
        draw.line((0, y, width, y), fill="#253d54")
    points = [(x, height / 2 + 95 * math.sin(x / width * math.pi * 5 - frame / fps * math.pi)) for x in range(width)]
    draw.line(points, fill="#aecbdd", width=4)
    draw.text((28, 25), "WAVE STUDY / HYPERLIB", fill="#aecbdd")
    process.stdin.write(image.tobytes())
process.stdin.close()
if process.wait():
    raise RuntimeError("The demonstration video could not be encoded.")
print("HYPERLIB_DEMO_MEDIA_OK")
