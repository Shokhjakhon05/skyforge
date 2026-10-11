#!/usr/bin/env python3
"""Draws the sniper game's places on the laptop, without Unity.

The places are laid out by hand in Campaign.cs and nobody here can run the game, so a wrongly
turned house or a target hidden behind a container used to be found only after a forty-minute
build. This reads the same models and the same layout and paints each place from above and from
every contract's firing position: rough, unlit, but enough to see what stands where.

    SNIPER_DUMP=/tmp/layout.json tools/check/test.sh TheLayoutCanBeWritten     (writes the layout)
    python3 tools/sniper_preview.py /tmp/layout.json out_dir [place ...]
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "models"))
from glb import Glb  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

PROPS = "Assets/Sniper/Props"
W, H = 960, 432
GROUND = {"asphalt": (92, 92, 96), "concrete": (118, 116, 112), "sand": (196, 170, 120), "grass": (104, 132, 70), "snow": (226, 230, 236)}
ROLE = {"Target": (255, 40, 40), "Guard": (255, 150, 30), "Civilian": (60, 220, 90), "Ally": (60, 140, 255)}
_models = {}


def model(name):
    """Triangles of a model standing on its base at its middle, in Unity's axes, and its size."""
    if name in _models:
        return _models[name]
    path = os.path.join(PROPS, name + ".glb")
    tris = []
    if os.path.exists(path):
        for _, _, tri, rgb in Glb(path).triangles():
            tris.append(([(-p[0], p[1], p[2]) for p in tri], rgb))
    if not tris:
        print("missing model", name)
        _models[name] = ([], (1, 1, 1))
        return _models[name]
    lo = [min(p[k] for t, _ in tris for p in t) for k in range(3)]
    hi = [max(p[k] for t, _ in tris for p in t) for k in range(3)]
    cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
    moved = [([(p[0] - cx, p[1] - lo[1], p[2] - cz) for p in t], rgb) for t, rgb in tris]
    # A far tower does not need every window: keep the larger faces of heavy models.
    if len(moved) > 2500:
        def area(t):
            a, b, c = t
            u = [b[k] - a[k] for k in range(3)]
            v = [c[k] - a[k] for k in range(3)]
            n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
            return n[0] * n[0] + n[1] * n[1] + n[2] * n[2]
        moved.sort(key=lambda item: -area(item[0]))
        moved = moved[:2500]
    _models[name] = (moved, (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]))
    return _models[name]


def place(piece):
    if piece["m"] == "box":
        tint = piece.get("tint") or "808080"
        rgb = tuple(int(tint[i:i + 2], 16) / 255 for i in (0, 2, 4))
        sx, sy, sz = piece["solid"]
        base = [(-sx / 2, 0, -sz / 2), (sx / 2, 0, -sz / 2), (sx / 2, 0, sz / 2), (-sx / 2, 0, sz / 2)]
        top = [(p[0], sy, p[2]) for p in base]
        faces = [(base[0], base[1], top[1]), (base[0], top[1], top[0]), (base[1], base[2], top[2]), (base[1], top[2], top[1]),
                 (base[2], base[3], top[3]), (base[2], top[3], top[2]), (base[3], base[0], top[0]), (base[3], top[0], top[3]),
                 (top[0], top[1], top[2]), (top[0], top[2], top[3])]
        tris, size, scale = [(list(f), rgb) for f in faces], (sx, sy, sz), 1.0
    else:
        tris, size = model(piece["m"])
        scale = piece.get("scale", 0) or (piece["h"] / size[1] if piece["h"] > 0 and size[1] > 0.001 else 1.0)
    yaw = math.radians(piece["yaw"])
    c, s = math.cos(yaw), math.sin(yaw)
    ax, ay, az = piece["at"]
    out = []
    for tri, rgb in tris:
        out.append(([(ax + (p[0] * c + p[2] * s) * scale, ay + p[1] * scale, az + (-p[0] * s + p[2] * c) * scale) for p in tri], rgb))
    return out


def box(at, w, h, rgb):
    x, y, z = at
    a = [(x - w, y, z - w), (x + w, y, z - w), (x + w, y, z + w), (x - w, y, z + w)]
    b = [(p[0], y + h, p[2]) for p in a]
    faces = [(a[0], a[1], b[1]), (a[0], b[1], b[0]), (a[1], a[2], b[2]), (a[1], b[2], b[1]), (a[2], a[3], b[3]), (a[2], b[3], b[2]),
             (a[3], a[0], b[0]), (a[3], b[0], b[3]), (b[0], b[1], b[2]), (b[0], b[2], b[3])]
    c = tuple(ch / 255 for ch in rgb)
    return [(list(f), c) for f in faces]


def ground(location, half=320.0, step=40.0):
    rgb = tuple(ch / 255 for ch in GROUND.get(location["ground"], (110, 110, 110)))
    sea = (0.16, 0.36, 0.5)
    water = location["water"]
    out = []
    x = -half
    while x < half:
        z = -half
        while z < half + 400:
            wet = water > 0 and z >= water
            y = -1.6 if wet else 0.0
            quad = [(x, y, z), (x, y, z + step), (x + step, y, z + step), (x + step, y, z)]
            colour = sea if wet else rgb
            out.append(([quad[0], quad[1], quad[2]], colour, True))
            out.append(([quad[0], quad[2], quad[3]], colour, True))
            z += step
        x += step
    return out


def render(tris, eye, look, fov, path, marks=(), title=""):
    fx, fy, fz = (look[k] - eye[k] for k in range(3))
    n = math.sqrt(fx * fx + fy * fy + fz * fz) or 1
    fwd = (fx / n, fy / n, fz / n)
    rx, rz = fwd[2], -fwd[0]
    n = math.sqrt(rx * rx + rz * rz) or 1
    right = (rx / n, 0.0, rz / n)
    up = (fwd[1] * right[2] - fwd[2] * right[1], fwd[2] * right[0] - fwd[0] * right[2], fwd[0] * right[1] - fwd[1] * right[0])
    up = (-up[0], -up[1], -up[2]) if up[1] < 0 else up
    focal = (H / 2) / math.tan(math.radians(fov) / 2)

    def project(p):
        d = (p[0] - eye[0], p[1] - eye[1], p[2] - eye[2])
        depth = d[0] * fwd[0] + d[1] * fwd[1] + d[2] * fwd[2]
        if depth < 0.5:
            return None
        return (W / 2 + (d[0] * right[0] + d[2] * right[2]) / depth * focal, H / 2 - (d[0] * up[0] + d[1] * up[1] + d[2] * up[2]) / depth * focal, depth)

    image = Image.new("RGB", (W, H), (150, 190, 230))
    draw = ImageDraw.Draw(image)
    light = (0.35, 0.8, -0.45)
    items = []
    for entry in tris:
        tri, rgb = entry[0], entry[1]
        pts = [project(p) for p in tri]
        if any(p is None for p in pts):
            continue
        if all(p[0] < -200 or p[0] > W + 200 for p in pts) or all(p[1] < -200 or p[1] > H + 200 for p in pts):
            continue
        a, b, c = tri
        u = [b[k] - a[k] for k in range(3)]
        v = [c[k] - a[k] for k in range(3)]
        nn = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        length = math.sqrt(nn[0] ** 2 + nn[1] ** 2 + nn[2] ** 2) or 1
        shade = 1.0 if len(entry) > 2 else 0.55 + 0.45 * abs(sum(nn[k] / length * light[k] for k in range(3)))
        haze = min(0.6, max(p[2] for p in pts) / 900.0)
        colour = tuple(int(min(1.0, ch * shade) * 255 * (1 - haze) + (150, 190, 230)[i] * haze) for i, ch in enumerate(rgb))
        items.append((max(p[2] for p in pts), [(p[0], p[1]) for p in pts], colour))
    items.sort(key=lambda item: -item[0])
    for _, poly, colour in items:
        draw.polygon(poly, fill=colour)
    for point, rgb, label in marks:
        p = project(point)
        if p is None:
            continue
        draw.ellipse([p[0] - 4, p[1] - 4, p[0] + 4, p[1] + 4], outline=rgb, width=2)
        if label:
            draw.text((p[0] + 6, p[1] - 6), label, fill=rgb)
    draw.text((6, 4), title, fill=(255, 255, 0))
    image.save(path)


def main():
    data = json.load(open(sys.argv[1]))
    out = sys.argv[2]
    only = set(sys.argv[3:])
    os.makedirs(out, exist_ok=True)
    for location in data["locations"]:
        if only and location["id"] not in only:
            continue
        world = ground(location)
        for piece in location["pieces"]:
            world.extend(place(piece))
        print(location["id"], len(world), "triangles")
        render(world, (-70, 60, -150), (0, 0, 20), 55, os.path.join(out, location["id"] + "_0_high.png"), title=location["id"] + " from above")
        render(world, (0, 260, -10), (0, 0, 20), 50, os.path.join(out, location["id"] + "_0_top.png"), title=location["id"] + " plan (far side up)")
        render(world, location["menuEye"], location["menuLook"], 46, os.path.join(out, location["id"] + "_0_menu.png"), title=location["id"] + " behind the menu")
        for mission in data["missions"]:
            if mission["place"] != location["id"] or os.environ.get("PV_ONLY_PLACE"):
                continue
            people, marks = [], []
            first = None
            for index, actor in enumerate(mission["actors"]):
                rgb = ROLE.get(actor["role"], (255, 255, 255))
                for step, at in enumerate(actor["path"]):
                    people.extend(box(at, 0.3 if step == 0 else 0.12, 1.8 if step == 0 else 0.5, rgb))
                marks.append(((actor["path"][0][0], 2.4, actor["path"][0][2]), rgb, actor["role"][0] + str(index)))
                if "escape" in actor:
                    marks.append(((actor["escape"][0], 0.5, actor["escape"][2]), (255, 255, 255), "x" + str(index)))
                if first is None and actor["role"] == "Target":
                    first = actor["path"][0]
            scene = world + people
            render(scene, mission["perch"], mission["look"], 46, os.path.join(out, mission["id"] + "_wide.png"), marks, mission["id"] + " from the rifle")
            if first:
                render(scene, mission["perch"], (first[0], 1.3, first[2]), 46 / 6.0, os.path.join(out, mission["id"] + "_x6.png"), marks, mission["id"] + " x6 on the first target")


if __name__ == "__main__":
    main()
