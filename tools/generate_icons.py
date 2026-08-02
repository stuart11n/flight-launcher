from pathlib import Path

from PIL import Image

src_path = Path(__file__).resolve().parents[1] / "Assets" / "rocket-icon-source.png"
assets = Path(__file__).resolve().parents[1] / "Assets"
src = Image.open(src_path).convert("RGBA")

alpha = src.split()[-1]
bbox = alpha.getbbox()
if bbox:
    src = src.crop(bbox)


def fit_square(img: Image.Image, size: int, pad_ratio: float = 0.0) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    inner = max(1, int(size * (1 - 2 * pad_ratio)))
    resized = img.copy()
    resized.thumbnail((inner, inner), Image.Resampling.LANCZOS)
    x = (size - resized.width) // 2
    y = (size - resized.height) // 2
    canvas.paste(resized, (x, y), resized)
    return canvas


def solid_canvas(w: int, h: int, color, glyph: Image.Image, glyph_max: int) -> Image.Image:
    canvas = Image.new("RGBA", (w, h), color)
    g = glyph.copy()
    g.thumbnail((glyph_max, glyph_max), Image.Resampling.LANCZOS)
    x = (w - g.width) // 2
    y = (h - g.height) // 2
    canvas.paste(g, (x, y), g)
    return canvas


cx = src.width // 2
sample = src.getpixel((cx, int(src.height * 0.15)))
bg = sample if sample[3] > 200 else (30, 107, 184, 255)

ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
ico_path = assets / "AppIcon.ico"
# Pillow builds each size from this master image when sizes= is provided.
fit_square(src, 256).save(ico_path, format="ICO", sizes=ico_sizes)
print(f"Wrote {ico_path.name} ({ico_path.stat().st_size} bytes)")

outputs = {
    "StoreLogo.png": 50,
    "Square44x44Logo.scale-200.png": 88,
    "Square150x150Logo.scale-200.png": 300,
    "LockScreenLogo.scale-200.png": 48,
    "Square44x44Logo.targetsize-24_altform-unplated.png": 24,
    "Square44x44Logo.targetsize-48_altform-lightunplated.png": 48,
}
for name, size in outputs.items():
    path = assets / name
    fit_square(src, size).save(path, format="PNG")
    print(f"Wrote {name} {size}x{size}")

solid_canvas(620, 300, bg, src, 240).save(assets / "Wide310x150Logo.scale-200.png", format="PNG")
print("Wrote Wide310x150Logo.scale-200.png 620x300")

solid_canvas(1240, 600, bg, src, 360).save(assets / "SplashScreen.scale-200.png", format="PNG")
print("Wrote SplashScreen.scale-200.png 1240x600")
