#!/usr/bin/env python3
"""Makes the app icon out of a frame of the game: Assets/Skyforge/Branding/icon.png, 1024x1024.

The frame is one of the "hero" screenshots the CI build saves with the HUD off. A square is cut
out of it, graded warmer and harder, darkened towards the edges, and given the game's mark: a
targeting ring in the instrument cyan and the amber delta wing at the foot.

Run from the repository root:
    python3 tools/gen_icon_frame.py frame.png [centre_x centre_y zoom]     (centre in 0..1 of the frame)
"""
import math
import sys
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

SIZE = 1024
AMBER = (255, 170, 40)
CYAN = (90, 225, 255)


def main():
    frame = Image.open(sys.argv[1]).convert("RGB")
    cx = float(sys.argv[2]) if len(sys.argv) > 2 else 0.5
    cy = float(sys.argv[3]) if len(sys.argv) > 3 else 0.5
    zoom = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
    side = min(frame.size) / zoom
    left = min(max(0, frame.width * cx - side / 2), frame.width - side)
    top = min(max(0, frame.height * cy - side / 2), frame.height - side)
    icon = frame.crop((int(left), int(top), int(left + side), int(top + side))).resize((SIZE, SIZE), Image.LANCZOS)
    icon = ImageEnhance.Contrast(icon).enhance(1.25)
    icon = ImageEnhance.Color(icon).enhance(1.35)
    icon = icon.filter(ImageFilter.UnsharpMask(radius=3, percent=60))

    # Darker towards the edges and the foot, so the mark reads and the launcher's mask cuts nothing that matters.
    shade = Image.new("L", (SIZE, SIZE), 0)
    pixels = shade.load()
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - SIZE / 2, y - SIZE * 0.46) / (SIZE * 0.72)
            edge = max(0.0, min(1.0, (d - 0.45) / 0.55)) ** 1.6
            foot = max(0.0, (y / SIZE - 0.62) / 0.38) ** 1.5
            pixels[x, y] = int(255 * min(1.0, edge * 0.85 + foot * 0.8))
    icon = Image.composite(Image.new("RGB", (SIZE, SIZE), (4, 9, 18)), icon, shade)

    big = Image.new("RGBA", (SIZE * 2, SIZE * 2), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    c, r = SIZE, SIZE * 0.80
    # The targeting ring: four arcs with gaps at the compass points, and ticks in the gaps.
    for start in (12, 102, 192, 282):
        draw.arc((c - r, c - r * 1.0, c + r, c + r * 1.0), start, start + 66, fill=CYAN + (235,), width=14)
    for angle in (0, 90, 180, 270):
        a = math.radians(angle)
        draw.line((c + math.cos(a) * (r - 44), c + math.sin(a) * (r - 44), c + math.cos(a) * (r + 30), c + math.sin(a) * (r + 30)),
                  fill=CYAN + (235,), width=10)
    # The delta wing at the foot.
    wx, wy, s = SIZE, SIZE * 1.62, 1.55
    for side_ in (-1, 1):
        draw.polygon([(wx + side_ * 10 * s, wy - 120 * s), (wx + side_ * 122 * s, wy + 72 * s), (wx + side_ * 84 * s, wy + 72 * s),
                      (wx + side_ * 10 * s, wy - 44 * s)], fill=AMBER + (255,))
    draw.polygon([(wx - 9 * s, wy - 96 * s), (wx + 9 * s, wy - 96 * s), (wx + 9 * s, wy + 40 * s), (wx - 9 * s, wy + 40 * s)], fill=(255, 255, 255, 255))
    marks = big.resize((SIZE, SIZE), Image.LANCZOS)
    glow = marks.filter(ImageFilter.GaussianBlur(14))
    out = icon.convert("RGBA")
    out.alpha_composite(glow)
    out.alpha_composite(marks)
    out.convert("RGB").save("Assets/Skyforge/Branding/icon.png")
    print("icon written from", sys.argv[1])


if __name__ == "__main__":
    main()
