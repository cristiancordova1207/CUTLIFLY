"""Genera icons/cutlifly.ico y assets/logo.png (requiere Pillow)."""
from PIL import Image, ImageDraw, ImageFilter
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def render(size):
    s = 8  # supersampling
    S = size * s
    top, bottom = (166, 140, 255), (108, 76, 245)
    grad = Image.new("RGB", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bottom, y / S))
    # brillo rosado sutil arriba a la izquierda
    glow = Image.new("L", (S, S), 0)
    ImageDraw.Draw(glow).ellipse([-S * 0.4, -S * 0.4, S * 0.7, S * 0.7], fill=90)
    glow = glow.filter(ImageFilter.GaussianBlur(S * 0.12))
    grad = Image.composite(Image.new("RGB", (S, S), (255, 190, 225)), grad, glow)

    radius = S * 0.24
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    small = size <= 24
    w = S * (0.095 if small else 0.075)
    m = S * (0.22 if small else 0.24)       # margen del marco
    L = S * (0.20 if small else 0.17)       # largo de cada esquina
    white = (255, 255, 255, 255)
    for cx, cy, dx, dy in [(m, m, 1, 1), (S - m, m, -1, 1), (m, S - m, 1, -1), (S - m, S - m, -1, -1)]:
        d.rounded_rectangle(sorted_box(cx, cy, cx + dx * L, cy + dy * w), radius=w / 2, fill=white)
        d.rounded_rectangle(sorted_box(cx, cy, cx + dx * w, cy + dy * L), radius=w / 2, fill=white)
    # destello central (captura)
    c = S / 2
    r = S * (0.13 if small else 0.15)
    k = r * 0.28
    pts = [(c, c - r), (c + k, c - k), (c + r, c), (c + k, c + k), (c, c + r), (c - k, c + k), (c - r, c), (c - k, c - k)]
    d.polygon(pts, fill=(255, 236, 246, 255))
    return img.resize((size, size), Image.LANCZOS)

def sorted_box(x0, y0, x1, y1):
    return [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]

if __name__ == "__main__":
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [render(sz) for sz in sizes]
    imgs[-1].save(os.path.join(HERE, "cutlifly.ico"), sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    imgs[-1].save(os.path.join(ROOT, "assets", "logo.png"))
    for sz, im in zip(sizes, imgs):
        im.save(os.path.join(HERE, f"cutlifly-{sz}.png"))
    print("ok")
