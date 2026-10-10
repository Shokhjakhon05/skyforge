#!/usr/bin/env python3
"""Packs CC0 materials from ambientCG into the detail textures the Skyforge/Surface shader reads.

One RGBA picture per material: R and G are the normal's x and y (OpenGL convention), B is the
colour's brightness scaled so its mean is 0.5 (the shader multiplies the palette colour by twice
this), A is roughness. The sources are downloaded into a cache on first use.

Usage: python3 tools/gen_textures.py
"""
import io, os, sys, urllib.request, zipfile
from PIL import Image, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets/Skyforge/Resources/Textures")
CACHE = os.path.expanduser("~/.cache/skyforge-textures")
MATERIALS = {
    "detail_ground": "Ground037",
    "detail_rock": "Rock030",
    "detail_metal": "Metal046B",
    "detail_concrete": "Concrete034",
    "detail_sand": "Ground054",
    "detail_snow": "Snow006",
    "detail_rust": "Rust004",
}


def source(asset, kind):
    path = os.path.join(CACHE, f"{asset}_{kind}.jpg")
    if not os.path.exists(path):
        os.makedirs(CACHE, exist_ok=True)
        url = f"https://ambientcg.com/get?file={asset}_1K-JPG.zip"
        data = urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "skyforge"})).read()
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for wanted in ("Color", "NormalGL", "Roughness"):
                with open(os.path.join(CACHE, f"{asset}_{wanted}.jpg"), "wb") as out:
                    out.write(archive.read(f"{asset}_1K-JPG_{wanted}.jpg"))
    return Image.open(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, asset in MATERIALS.items():
        normal = source(asset, "NormalGL").convert("RGB")
        light = source(asset, "Color").convert("L")
        rough = source(asset, "Roughness").convert("L")
        mean = sum(light.resize((64, 64)).getdata()) / 4096.0
        light = light.point(lambda v: max(0, min(255, int(v / mean * 127.5))))
        r, g, _ = normal.split()
        Image.merge("RGBA", (r, g, light, rough)).save(os.path.join(OUT, name + ".png"))
        print(name, asset, "mean brightness %.0f" % mean)


if __name__ == "__main__":
    main()
