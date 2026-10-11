#!/usr/bin/env python3
"""Draws the sniper game's icon: a man's outline in the glass of a scope, on a dark field.

    python3 tools/gen_icon_sniper.py            (writes Assets/Skyforge/Branding/icon.png, 1024x1024)
"""
import math
from PIL import Image, ImageDraw, ImageFilter

S = 2048  # drawn at twice the size and scaled down, so every edge is smooth


def main():
    img = Image.new("RGB", (S, S), (8, 10, 16))
    px = img.load()
    cx = cy = S / 2
    for y in range(S):
        for x in range(0, S, 1):
            d = math.hypot(x - cx, y - cy * 0.92) / (S * 0.72)
            t = max(0.0, 1.0 - d)
            warm = max(0.0, 1.0 - math.hypot(x - cx, y - S * 1.02) / (S * 0.75))
            px[x, y] = (int(8 + 26 * t * t + 120 * warm * warm), int(11 + 40 * t * t + 22 * warm * warm), int(18 + 70 * t * t + 10 * warm * warm))
    draw = ImageDraw.Draw(img, "RGBA")

    # The glass: a faint lighter disc inside the ring.
    r = S * 0.39
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(120, 170, 220, 26))

    # The man: head and shoulders, the warm red of a marked target.
    head_r = S * 0.085
    head_y = cy - S * 0.06
    body = [(cx - S * 0.19, cy + S * 0.36), (cx - S * 0.175, cy + S * 0.16), (cx - S * 0.10, cy + S * 0.075), (cx - S * 0.045, cy + S * 0.05),
            (cx + S * 0.045, cy + S * 0.05), (cx + S * 0.10, cy + S * 0.075), (cx + S * 0.175, cy + S * 0.16), (cx + S * 0.19, cy + S * 0.36)]
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.polygon(body, fill=(255, 70, 40, 150))
    gd.ellipse([cx - head_r, head_y - head_r, cx + head_r, head_y + head_r], fill=(255, 70, 40, 150))
    img.paste(Image.alpha_composite(img.convert("RGBA"), glow.filter(ImageFilter.GaussianBlur(S * 0.03))).convert("RGB"))
    draw = ImageDraw.Draw(img, "RGBA")
    draw.polygon(body, fill=(214, 44, 30, 255))
    draw.ellipse([cx - head_r, head_y - head_r, cx + head_r, head_y + head_r], fill=(226, 52, 34, 255))
    # Cut him off at the rim of the glass.
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    base = Image.new("RGB", (S, S), (8, 10, 16))
    bp = base.load()
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - cx, y - cy * 0.92) / (S * 0.72)
            t = max(0.0, 1.0 - d)
            warm = max(0.0, 1.0 - math.hypot(x - cx, y - S * 1.02) / (S * 0.75))
            bp[x, y] = (int(8 + 26 * t * t + 120 * warm * warm), int(11 + 40 * t * t + 22 * warm * warm), int(18 + 70 * t * t + 10 * warm * warm))
    img = Image.composite(img, base, mask)
    draw = ImageDraw.Draw(img, "RGBA")

    white = (245, 247, 250, 255)
    w = int(S * 0.026)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=white, width=w)
    r2 = S * 0.20
    draw.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], outline=(245, 247, 250, 120), width=int(S * 0.006))
    # The cross: four arms that stop short of the middle, and four thick posts outside the ring.
    t = int(S * 0.008)
    gap = S * 0.035
    draw.rectangle([cx - r, cy - t, cx - gap, cy + t], fill=white)
    draw.rectangle([cx + gap, cy - t, cx + r, cy + t], fill=white)
    draw.rectangle([cx - t, cy - r, cx + t, cy - gap], fill=white)
    draw.rectangle([cx - t, cy + gap, cx + t, cy + r], fill=white)
    post = int(S * 0.02)
    for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        x0, y0 = cx + dx * r, cy + dy * r
        x1, y1 = cx + dx * (r + S * 0.075), cy + dy * (r + S * 0.075)
        draw.rectangle([min(x0, x1) - (post if dy else 0), min(y0, y1) - (post if dx else 0), max(x0, x1) + (post if dy else 0), max(y0, y1) + (post if dx else 0)], fill=white)
    for i in range(1, 5):
        for sign in (-1, 1):
            m = S * 0.012 if i % 2 else S * 0.02
            draw.rectangle([cx + sign * i * S * 0.07 - 2, cy - m, cx + sign * i * S * 0.07 + 2, cy + m], fill=white)
            draw.rectangle([cx - m, cy + sign * i * S * 0.07 - 2, cx + m, cy + sign * i * S * 0.07 + 2], fill=white)
    # The point of aim, on his head.
    dot = S * 0.014
    draw.ellipse([cx - dot, head_y - dot, cx + dot, head_y + dot], fill=(255, 255, 255, 255))

    img = img.resize((1024, 1024), Image.LANCZOS)
    img.save("Assets/Skyforge/Branding/icon.png")
    print("icon written")


if __name__ == "__main__":
    main()
