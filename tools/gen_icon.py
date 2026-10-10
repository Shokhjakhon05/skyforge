#!/usr/bin/env python3
"""Draws the app icon: Assets/Skyforge/Branding/icon.png, 1024x1024.

The mark is the one the game opens with: a delta wing inside a targeting ring, in the
interface's own colours (amber on deep navy, cyan for the instrument lines). Drawn at twice
the size and scaled down, so the edges are smooth.

Run from the repository root:  python3 tools/gen_icon.py
"""
import math
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SIZE = 1024
S = 2  # supersampling
N = SIZE * S
AMBER = (255, 158, 26)
CYAN = (77, 219, 255)


def background():
    image = Image.new("RGB", (N, N))
    pixels = image.load()
    cx, cy = N * 0.5, N * 0.42
    for y in range(N):
        for x in range(N):
            d = math.hypot(x - cx, y - cy) / (N * 0.75)
            t = min(1.0, d)
            # Deep navy, lighter and bluer towards the middle.
            r = int(6 + 22 * (1 - t) ** 2)
            g = int(12 + 44 * (1 - t) ** 2)
            b = int(22 + 78 * (1 - t) ** 1.6)
            pixels[x, y] = (r, g, b)
    return image


def glow(layer, radius, strength=1.0):
    """The layer's own light, blurred, laid under it."""
    halo = layer.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        alpha = halo.getchannel("A").point(lambda v: int(min(255, v * strength)))
        halo.putalpha(alpha)
    return Image.alpha_composite(halo, layer)


def main():
    icon = background().convert("RGBA")
    cx, cy = N // 2, int(N * 0.47)

    # A faint grid, like a map under glass.
    grid = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    draw = ImageDraw.Draw(grid)
    step = N // 12
    for i in range(1, 12):
        draw.line([(i * step, 0), (i * step, N)], fill=CYAN + (16,), width=S)
        draw.line([(0, i * step), (N, i * step)], fill=CYAN + (16,), width=S)
    icon = Image.alpha_composite(icon, grid)

    # The targeting ring: a broken circle with four ticks.
    ring = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    draw = ImageDraw.Draw(ring)
    radius = int(N * 0.335)
    box = [cx - radius, cy - radius, cx + radius, cy + radius]
    for start in (20, 110, 200, 290):
        draw.arc(box, start, start + 70, fill=CYAN + (255,), width=int(N * 0.022))
    inner = int(N * 0.275)
    draw.ellipse([cx - inner, cy - inner, cx + inner, cy + inner], outline=CYAN + (70,), width=S * 2)
    for angle in (0, 90, 180, 270):
        a = math.radians(angle)
        x0, y0 = cx + math.cos(a) * radius * 0.93, cy + math.sin(a) * radius * 0.93
        x1, y1 = cx + math.cos(a) * radius * 1.16, cy + math.sin(a) * radius * 1.16
        draw.line([(x0, y0), (x1, y1)], fill=CYAN + (255,), width=int(N * 0.016))
    icon = Image.alpha_composite(icon, glow(ring, N * 0.012, 0.9))

    # The delta wing: two swept amber wings and a pale spine, pointing up.
    wing = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    draw = ImageDraw.Draw(wing)
    u = N * 0.01

    def at(x, y):
        return (cx + x * u, cy + y * u)

    left = [at(-3.2, -23), at(-25, 17), at(-13.5, 17), at(-3.2, -3)]
    right = [(2 * cx - x, y) for x, y in left]
    draw.polygon(left, fill=AMBER + (255,))
    draw.polygon(right, fill=AMBER + (255,))
    # Darker undersides give the wings an edge.
    draw.polygon([at(-25, 17), at(-13.5, 17), at(-15.5, 12.5), at(-22.2, 12.5)], fill=(190, 96, 10, 255))
    draw.polygon([(2 * cx - x, y) for x, y in [at(-25, 17), at(-13.5, 17), at(-15.5, 12.5), at(-22.2, 12.5)]], fill=(190, 96, 10, 255))
    draw.polygon([at(0, -27), at(2.4, -6), at(2.4, 21), at(0, 25), at(-2.4, 21), at(-2.4, -6)], fill=(245, 248, 252, 255))
    icon = Image.alpha_composite(icon, glow(wing, N * 0.02, 0.75))

    # A sheen across the top half, and a darkened rim.
    sheen = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    draw = ImageDraw.Draw(sheen)
    draw.ellipse([-N * 0.3, -N * 0.95, N * 1.3, N * 0.36], fill=(255, 255, 255, 20))
    icon = Image.alpha_composite(icon, sheen)
    rim = Image.new("L", (N, N), 0)
    ImageDraw.Draw(rim).rectangle([0, 0, N - 1, N - 1], outline=255, width=int(N * 0.05))
    rim = rim.filter(ImageFilter.GaussianBlur(N * 0.04))
    dark = Image.new("RGBA", (N, N), (0, 0, 0, 255))
    dark.putalpha(rim.point(lambda v: int(v * 0.55)))
    icon = Image.alpha_composite(icon, dark)

    icon.convert("RGB").resize((SIZE, SIZE), Image.LANCZOS).save("Assets/Skyforge/Branding/icon.png")
    print("icon written")


if __name__ == "__main__":
    main()
