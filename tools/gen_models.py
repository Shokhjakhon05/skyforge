#!/usr/bin/env python3
"""Bakes the downloaded 3D models into the game's own mesh files.

Nobody opens this project in the Unity editor, so models do not go through Unity's importer.
Each source GLB in tools/models/src is flattened here into flat-shaded triangles with one
palette colour per face and written to Assets/Skyforge/Resources/Models/<name>.bytes, which
ModelLibrary reads at run time. Parts that move (rotors, turret heads) are kept apart.

Sources are from poly.pizza; the licence of each is in the .json beside it and in
docs/CREDITS.md, which this script also writes.

Run from the repository root:  python3 tools/gen_models.py [--preview out.png]
"""
import json
import math
import os
import re
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "models"))
from glb import Glb  # noqa: E402

SRC = "tools/models/src"
OUT = "Assets/Skyforge/Resources/Models"

# name: source id, length in metres along the nose axis, degrees to turn so the nose points +Z,
# what the origin's height means ("ground": lowest point at 0; a number: lowest point at that y),
# and the moving parts as {part name: pattern on the node path}.
MODELS = {
    "tank": dict(src="7GG1xDtc8l", size=7.4, yaw=0),
    "tank_heavy": dict(src="egcLMSGiuA", size=9.0, yaw=-90),
    "truck": dict(src="EWp0hGvZPV", size=7.0, yaw=0),
    "truck_cargo": dict(src="jHwRymyg2C", size=8.0, yaw=-90),
    "truck_fuel": dict(src="64ayx6pW3O", size=8.4, yaw=-90),
    "humvee": dict(src="Ebryot9iKM", size=5.2, yaw=-90),
    "jeep": dict(src="AcSdGGrgYP", size=4.8, yaw=0),
    "heli": dict(src="hG2Qr0A3zR", size=15.0, yaw=0),
    "sam": dict(src="RqnAj5N9fL", size=5.0, yaw=0, parts={"head": r"Head|Guns"}),
    "aagun": dict(src="rxgAVyYKh6", size=5.4, yaw=0, parts={"head": r"GunCannon"}),
    "tank_light": dict(src="S1jUTRmAjD", size=6.2, yaw=0),
    "apc": dict(src="VvX8nmoCN5", size=6.8, yaw=0),
    "truck_army": dict(src="3XAD5t0Djqv", size=7.6, yaw=0),
    "gatling": dict(src="T8ofhRSenf", size=4.6, yaw=0, parts={"head": r"Pipes|Gun1|Laser|AmmoBox|Middle"}),
    "heli_transport": dict(src="cTzINMr0WdS", size=13.0, yaw=180),
    "warship": dict(src="cqV6mUkn7Ow", size=58.0, yaw=0, tris=7000),
    "missile": dict(src="dPVCvXP-S58", size=1.5, yaw=90, lift=-0.15, tris=500),
    "boat": dict(src="wouBxOe3CD", size=11.0, yaw=0),
    "sandbags": dict(src="LW3jwpPfiN", size=4.2, yaw=0, axis="x", tris=1400),
    "barrel": dict(src="1orHe0kCc1", size=1.1, yaw=0, axis="y"),
    "drone_quad": dict(src="DNbUoMtG3H", size=1.25, yaw=0, lift=-0.38,
                       parts={"rotor0": "Rotor_FL", "rotor1": "Rotor_FR", "rotor2": "Rotor_BL", "rotor3": "Rotor_BR"}, tris=6000),
    "drone_scout": dict(src="3Ae_y67lzvd", size=1.3, yaw=0, lift=-0.38,
                        parts={"rotor0": "group1083488708", "rotor1": "group255131489",
                               "rotor2": "group1326258638", "rotor3": "group1301670615"}),
    "drone_wing": dict(src="3Eio09miiAF", size=2.3, yaw=0, lift=-0.38),
    "drone_heavy": dict(src="6CUQX98vha4", size=1.7, yaw=-90, lift=-0.38),
    "drone_jet": dict(src="6fyLMORhgGK", size=2.6, yaw=0, lift=-0.38),
    "drone_delta": dict(src="1fi8ZIDdFCP", size=2.2, yaw=0, lift=-0.38),
    "drone_fighter": dict(src="dukcCKsLDrS", size=3.0, yaw=0, lift=-0.38),
    "drone_prop": dict(src="9VeIc0cybp4", size=2.6, yaw=0, lift=-0.38),
    "drone_heli": dict(src="EQJ2MECUbx", size=2.8, yaw=0, lift=-0.38),
    "drone_gunship": dict(src="hG2Qr0A3zR", size=3.2, yaw=0, lift=-0.38),
    "drone_lifter": dict(src="cTzINMr0WdS", size=3.2, yaw=180, lift=-0.38),
    "drone_hover": dict(src="eGrzfhSGpOg", size=2.4, yaw=180, lift=-0.38),
    # Scenery: what a town at war is built from.
    "b_large1": dict(src="ppwtREejXg", size=13.5, yaw=0, axis="y", tris=1100),
    "b_large2": dict(src="h7Jaq7bqMq", size=12.5, yaw=0, axis="y", tris=1100),
    "b_large3": dict(src="sxXonOmtct", size=9.4, yaw=0, axis="y", tris=1000),
    "b_large4": dict(src="yKo7F36Qk2", size=13.0, yaw=0, axis="y", tris=1000),
    "b_large5": dict(src="JgGLJH2iXj", size=9.4, yaw=0, axis="y", tris=1000),
    "b_small1": dict(src="QjL4Fo9dU9", size=7.0, yaw=0, axis="y", tris=800),
    "b_small2": dict(src="gyjF60t7CG", size=6.5, yaw=0, axis="y", tris=800),
    "b_small3": dict(src="Rq572hdKEz", size=8.5, yaw=0, axis="y", tris=800),
    "b_small4": dict(src="yLvnMqC9ZG", size=8.0, yaw=0, axis="y", tris=800),
    "apartment": dict(src="01lqee-dZAr", size=14.0, yaw=0, axis="y", tris=1500),
    "warehouse": dict(src="fsxbktzB1zo", size=7.0, yaw=0, axis="y", tris=1500),
    "fortress": dict(src="4XMaa7Ovrwx", size=8.0, yaw=0, axis="y", tris=1500),
    "palm1": dict(src="A6cKJYFsIb", size=7.5, yaw=0, axis="y", tris=500),
    "palm2": dict(src="DsrrAYmucG", size=8.5, yaw=0, axis="y", tris=500),
    "palm3": dict(src="bjGeBbKhAVN", size=9.0, yaw=0, axis="y", tris=500),
    "rock1": dict(src="4TpBWdzKDf2", size=5.0, yaw=0, axis="y", tris=300),
    "rock2": dict(src="54jZKTAt5p", size=4.0, yaw=0, axis="y", tris=300),
    "rock3": dict(src="pRY9BCFbmQ", size=7.0, yaw=0, axis="y", tris=300),
    "rock4": dict(src="s1OJ3bBzqc", size=3.5, yaw=0, axis="y", tris=500),
    "container1": dict(src="CQMziXZfYh", size=2.6, yaw=0, axis="y", tris=400),
    "container2": dict(src="83Cai5aKxa", size=2.6, yaw=0, axis="y", tris=500),
    "barrier1": dict(src="5Cg2oW7EFn", size=1.1, yaw=0, axis="y", tris=200),
    "barrier2": dict(src="wCUxgt2jSP", size=1.2, yaw=0, axis="y", tris=200),
    "wall": dict(src="tdeAOh3LQV", size=2.4, yaw=0, axis="y", tris=300),
    "trash": dict(src="53AXNH8J9N", size=2.2, yaw=0, axis="y", tris=600),
    "guardtower": dict(src="sbaM8I229r", size=8.5, yaw=0, axis="y", tris=500),
    "radiotower": dict(src="eWEYV9ppUjv", size=9.4, yaw=0, axis="y", tris=700),
    "watertower": dict(src="tMK8bhapAK", size=9.0, yaw=0, axis="y", tris=500),
    "tent": dict(src="vQANVbSE0v", size=3.2, yaw=0, axis="y", tris=500),
    "crate": dict(src="vXaxy7ortM", size=1.3, yaw=0, axis="y", tris=500),
}


def load(name, cfg):
    """Returns {part: [(tri, rgb)]} in Unity's axes (x mirrored), scaled, nose along +Z."""
    yaw = math.radians(cfg.get("yaw", 0))
    cy, sy = math.cos(yaw), math.sin(yaw)
    parts = {}
    for path, _, tri, rgb in Glb(os.path.join(SRC, cfg["src"] + ".glb")).triangles():
        part = "body"
        for pname, pattern in cfg.get("parts", {}).items():
            if re.search(pattern, path):
                part = pname
        # glTF is right-handed, Unity left-handed: mirror X and reverse the winding.
        pts = []
        for x, y, z in tri:
            x = -x
            pts.append((x * cy + z * sy, y, -x * sy + z * cy))
        parts.setdefault(part, []).append(([pts[0], pts[2], pts[1]], rgb))

    every = [p for tris in parts.values() for tri, _ in tris for p in tri]
    lo = [min(p[k] for p in every) for k in range(3)]
    hi = [max(p[k] for p in every) for k in range(3)]
    axis = {"x": 0, "y": 1, "z": 2}[cfg.get("axis", "z")]
    scale = cfg["size"] / (hi[axis] - lo[axis])
    cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
    base = lo[1] - cfg.get("lift", 0.0) / scale
    for tris in parts.values():
        for tri, _ in tris:
            for i, (x, y, z) in enumerate(tri):
                tri[i] = ((x - cx) * scale, (y - base) * scale, (z - cz) * scale)
    return parts


def decimate(parts, limit):
    """Vertex clustering: snaps vertices to a grid that coarsens until the model fits the budget.

    Crude, but these are low-poly models seen from a drone, and a phone draws dozens of them."""
    def count(p):
        return sum(len(tris) for tris in p.values())
    if count(parts) <= limit:
        return parts
    every = [pt for tris in parts.values() for tri, _ in tris for pt in tri]
    span = max(max(pt[k] for pt in every) - min(pt[k] for pt in every) for k in range(3))
    cell = span / 160.0
    while True:
        result = {}
        for name, tris in parts.items():
            cells = {}
            for tri, _ in tris:
                for pt in tri:
                    key = tuple(int(math.floor(pt[k] / cell)) for k in range(3))
                    entry = cells.setdefault(key, [0.0, 0.0, 0.0, 0])
                    for k in range(3):
                        entry[k] += pt[k]
                    entry[3] += 1
            kept = []
            seen = set()
            for tri, rgb in tris:
                keys = [tuple(int(math.floor(pt[k] / cell)) for k in range(3)) for pt in tri]
                if keys[0] == keys[1] or keys[1] == keys[2] or keys[0] == keys[2]:
                    continue
                face = (frozenset(keys), tuple(round(c, 2) for c in rgb))
                if face in seen:
                    continue
                seen.add(face)
                kept.append(([tuple(cells[key][k] / cells[key][3] for k in range(3)) for key in keys], rgb))
            result[name] = kept
        if count(result) <= limit:
            return result
        cell *= 1.25


def quantize(parts, limit=64):
    """One palette for the model: colours are rounded until at most `limit` are left."""
    step = 8
    while True:
        palette = {}
        for tris in parts.values():
            for _, rgb in tris:
                key = tuple(min(255, int(round(c * 255 / step)) * step) for c in rgb)
                palette.setdefault(key, len(palette))
        if len(palette) <= limit:
            return palette, step
        step *= 2


def write(name, parts):
    palette, step = quantize(parts)
    out = bytearray(b"SFM1")
    out += struct.pack("<H", len(palette))
    for key in palette:
        out += bytes(key)
    out += struct.pack("<H", len(parts))
    total = 0
    for pname in sorted(parts, key=lambda p: (p != "body", p)):
        tris = parts[pname]
        pts = [p for tri, _ in tris for p in tri]
        pivot = (0.0, 0.0, 0.0)
        if pname != "body":
            pivot = tuple((min(p[k] for p in pts) + max(p[k] for p in pts)) / 2 for k in range(3))
        vertices = {}
        order = []
        indices = []
        for tri, rgb in tris:
            a, b, c = tri
            u = [b[k] - a[k] for k in range(3)]
            v = [c[k] - a[k] for k in range(3)]
            n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            length = math.sqrt(sum(x * x for x in n))
            if length < 1e-10:
                continue
            normal = tuple(int(round(x / length * 127)) for x in n)
            colour = palette[tuple(min(255, int(round(ch * 255 / step)) * step) for ch in rgb)]
            for p in tri:
                key = (tuple(round(p[k] - pivot[k], 4) for k in range(3)), normal, colour)
                if key not in vertices:
                    vertices[key] = len(order)
                    order.append(key)
                indices.append(vertices[key])
        encoded = pname.encode("ascii")
        out += struct.pack("<B", len(encoded)) + encoded
        out += struct.pack("<3f", *pivot)
        out += struct.pack("<II", len(order), len(indices))
        for pos, normal, colour in order:
            out += struct.pack("<3f3bB", *pos, *normal, colour)
        out += struct.pack("<%dI" % len(indices), *indices)
        total += len(indices) // 3
    with open(os.path.join(OUT, name + ".bytes"), "wb") as handle:
        handle.write(out)
    return total, len(palette), len(out)


# Everything else that was not made for this game: what it is, where from, whose, and the licence.
OTHER = [
    ("Sky panoramas", "polyhaven.com", "Greg Zaal, Jarod Guest, Sergej Majboroda, Dimitrios Savva and others", "CC0 1.0"),
    ("Surface textures", "ambientcg.com", "Lennart Demes", "CC0 1.0"),
    ("Font Play", "fonts.google.com", "Jonas Hecksher, Playtype", "SIL OFL 1.1"),
    ("Font Russo One", "fonts.google.com", "Jovanny Lemonad", "SIL OFL 1.1"),
    ("Music: War Theme", "opengameart.org", "Spring Spring", "CC0 1.0"),
    ("Music: Dramatic Boss Encounter, Epic Endgame Cinematic", "opengameart.org", "cynicmusic", "CC0 1.0"),
    ("Music: Hunter-class Lifeform", "opengameart.org", "Tsorthan Grove", "CC0 1.0"),
    ("Music: Tragic ambient main menu", "opengameart.org", "Pompei2", "CC0 1.0"),
    ("Music: Main Menu - Dark", "opengameart.org", "Bubble5297", "CC0 1.0"),
]


def credits():
    lines = ["# Сторонние материалы", "",
             "3D-модели взяты с [poly.pizza](https://poly.pizza) и запечены в формат игры скриптом `tools/gen_models.py`.",
             "Модели с лицензией CC-BY требуют указания автора: этот список показывается и в игре (Настройки → Авторы).", "",
             "| Модель в игре | Название | Автор | Лицензия | Источник |", "|---|---|---|---|---|"]
    rows = []
    for name, cfg in MODELS.items():
        meta = json.load(open(os.path.join(SRC, cfg["src"] + ".json")))
        lines.append("| %s | %s | %s | %s | https://poly.pizza/m/%s |" % (name, meta["Title"], meta["Creator"], meta["Licence"], cfg["src"]))
        rows.append((meta["Title"], meta["Creator"], meta["Licence"]))
    lines += ["", "## Небо, текстуры, шрифты, музыка", "",
              "| Что | Источник | Автор | Лицензия |", "|---|---|---|---|"]
    for what, source, author, licence in OTHER:
        lines.append("| %s | %s | %s | %s |" % (what, source, author, licence))
    with open("docs/CREDITS.md", "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    # The same list for the in-game credits screen.
    seen = []
    for title, creator, licence in rows:
        entry = '"%s" by %s (%s)' % (title, creator, licence)
        if entry not in seen:
            seen.append(entry)
    with open("Assets/Skyforge/Resources/credits.txt", "w", encoding="utf-8") as handle:
        handle.write("3D models from poly.pizza:\n" + "\n".join(seen) + "\n\n" + "\n".join(
            "%s: %s, %s (%s)" % row for row in OTHER) + "\n")


def preview(path, loaded):
    from PIL import Image, ImageDraw
    cell = 260
    names = [n for n in loaded if "--only" not in sys.argv or n in sys.argv[sys.argv.index("--only") + 1].split(",")]
    sheet = Image.new("RGB", (cell * 3, (cell + 16) * len(names)), (70, 80, 90))
    draw = ImageDraw.Draw(sheet)
    light = (0.4, 0.8, 0.45)
    for row, name in enumerate(names):
        tris = [(tri, rgb, part) for part, ts in loaded[name].items() for tri, rgb in ts]
        pts = [p for tri, _, _ in tris for p in tri]
        radius = max(max(abs(p[k]) for p in pts) for k in range(3)) * 1.15
        # top (x right, z up), side (z right, y up), three-quarter from front-right-above
        views = [lambda p: (p[0], p[2], p[1]), lambda p: (p[2], p[1], -p[0]),
                 lambda p: (p[0] * 0.8 - p[2] * 0.6, p[1] * 0.85 + (p[0] * 0.6 + p[2] * 0.8) * -0.35, -(p[0] * 0.6 + p[2] * 0.8) + p[1] * 0.3)]
        for col, view in enumerate(views):
            items = []
            for tri, rgb, part in tris:
                a, b, c = tri
                u = [b[k] - a[k] for k in range(3)]
                v = [c[k] - a[k] for k in range(3)]
                n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
                length = math.sqrt(sum(x * x for x in n)) or 1
                shade = 0.5 + 0.5 * max(0.0, sum(n[k] / length * light[k] for k in range(3)))
                if part != "body":
                    rgb = (1.0, 0.2, 0.9)
                proj = [view(p) for p in tri]
                items.append((sum(p[2] for p in proj), [(col * cell + cell / 2 + p[0] / radius * cell / 2,
                              row * (cell + 16) + cell / 2 - p[1] / radius * cell / 2) for p in proj],
                              tuple(int(min(1.0, ch * shade) * 255) for ch in rgb)))
            items.sort(key=lambda item: item[0])
            for _, poly, colour in items:
                draw.polygon(poly, fill=colour)
        draw.text((4, row * (cell + 16) + cell + 2), "%s  top(+Z up) | side(+Z right) | 3/4   r=%.1f" % (name, radius), fill="white")
    sheet.save(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    loaded = {}
    for name, cfg in MODELS.items():
        loaded[name] = decimate(load(name, cfg), cfg.get("tris", 4200))
        tris, colours, size = write(name, loaded[name])
        print("%-12s %5d tris %3d colours %7d bytes  parts: %s" % (name, tris, colours, size, ", ".join(sorted(loaded[name]))))
    credits()
    if "--preview" in sys.argv:
        preview(sys.argv[sys.argv.index("--preview") + 1], loaded)


if __name__ == "__main__":
    main()
