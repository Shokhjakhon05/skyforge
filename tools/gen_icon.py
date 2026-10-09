#!/usr/bin/env python3
"""Draws the app icon: a quadcopter seen from above inside a thin globe ring.

Run from the repository root:  python3 tools/gen_icon.py
Writes Assets/Skyforge/Branding/icon.png (1024 px, drawn at 4x and scaled down for smooth edges).
"""
import math
from PIL import Image, ImageDraw

SIZE, SCALE = 1024, 4
S = SIZE * SCALE
BACK_TOP, BACK_BOTTOM = (16, 24, 38), (6, 10, 18)
ORANGE, WHITE, GREY = (255, 158, 26), (236, 240, 245), (120, 132, 148)

image = Image.new("RGB", (S, S))
draw = ImageDraw.Draw(image)
for y in range(S):
    t = y / (S - 1)
    draw.line([(0, y), (S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(BACK_TOP, BACK_BOTTOM)))

cx = cy = S // 2


def ring(radius, width, color):
    draw.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], outline=color, width=width)


# The globe: an outer ring and two meridian arcs, kept faint.
R = int(S * 0.40)
ring(R, int(S * 0.012), GREY)
draw.arc([cx - R * 0.55, cy - R, cx + R * 0.55, cy + R], 0, 360, fill=(60, 70, 86), width=int(S * 0.006))
draw.line([(cx - R, cy), (cx + R, cy)], fill=(60, 70, 86), width=int(S * 0.006))

# The drone: four arms in an X, a rotor ring at each end, an orange body.
arm = int(S * 0.215)
for angle in (45, 135, 225, 315):
    a = math.radians(angle)
    ex, ey = cx + math.cos(a) * arm, cy + math.sin(a) * arm
    draw.line([(cx, cy), (ex, ey)], fill=WHITE, width=int(S * 0.034))
    r = int(S * 0.105)
    draw.ellipse([ex - r, ey - r, ex + r, ey + r], fill=BACK_BOTTOM, outline=WHITE, width=int(S * 0.022))
    hub = int(S * 0.022)
    draw.ellipse([ex - hub, ey - hub, ex + hub, ey + hub], fill=WHITE)

b = int(S * 0.085)
draw.rounded_rectangle([cx - b, cy - int(b * 1.25), cx + b, cy + int(b * 1.25)], radius=int(b * 0.45), fill=ORANGE)
n = int(S * 0.03)
draw.ellipse([cx - n, cy - int(b * 1.25) - n // 2, cx + n, cy - int(b * 1.25) + n * 3 // 2], fill=BACK_BOTTOM)

image.resize((SIZE, SIZE), Image.LANCZOS).save("Assets/Skyforge/Branding/icon.png")
print("icon written")
