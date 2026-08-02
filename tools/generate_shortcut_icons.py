from pathlib import Path

from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parents[1] / "Assets"
sizes = [16, 24, 32, 48, 64, 128, 256]
GREEN = (46, 125, 50, 255)  # #2E7D32
RED = (198, 40, 40, 255)  # #C62828
WHITE = (255, 255, 255, 255)


def rounded_bg(size: int, color: tuple[int, int, int, int]) -> tuple[Image.Image, ImageDraw.ImageDraw]:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    pad = max(1, size // 16)
    radius = max(2, size // 5)
    draw.rounded_rectangle([pad, pad, size - pad - 1, size - pad - 1], radius=radius, fill=color)
    return img, draw


def make_play(size: int) -> Image.Image:
    img, draw = rounded_bg(size, GREEN)
    left = int(size * 0.34)
    right = int(size * 0.70)
    top = int(size * 0.28)
    bot = int(size * 0.72)
    mid = size // 2
    draw.polygon([(left, top), (right, mid), (left, bot)], fill=WHITE)
    return img


def make_stop(size: int) -> Image.Image:
    img, draw = rounded_bg(size, RED)
    m = int(size * 0.30)
    draw.rounded_rectangle(
        [m, m, size - m - 1, size - m - 1],
        radius=max(1, size // 16),
        fill=WHITE,
    )
    return img


def save_ico(name: str, factory) -> None:
    images = [factory(s) for s in sizes]
    path = assets / name
    images[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes])
    print(f"Wrote {path.name} ({path.stat().st_size} bytes)")


if __name__ == "__main__":
    save_ico("StartShortcut.ico", make_play)
    save_ico("StopShortcut.ico", make_stop)
