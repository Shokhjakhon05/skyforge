#!/usr/bin/env python3
"""Create missing Unity .meta files under Assets/.

Nobody opens this project in the Unity editor, so the .meta files that the editor would
normally write are created here. The GUID is derived from the path, which keeps it stable
between CI runs and lets the Library cache stay valid.

Run from the repository root after adding files:  python3 tools/gen_meta.py
With --check it only reports missing files and exits with 1 (used in CI).
"""
import hashlib
import os
import sys

ROOT = "Assets"
SKIP = {"Assets/Generated"}


def meta_text(path: str, is_dir: bool) -> str:
    guid = hashlib.md5(path.encode("utf-8")).hexdigest()
    text = f"fileFormatVersion: 2\nguid: {guid}\n"
    if is_dir:
        text += "folderAsset: yes\n"
    return text


def main() -> int:
    check = "--check" in sys.argv
    missing = []
    for current, dirs, files in os.walk(ROOT):
        dirs[:] = sorted(d for d in dirs if os.path.join(current, d).replace(os.sep, "/") not in SKIP)
        entries = [(d, True) for d in dirs] + [(f, False) for f in sorted(files) if not f.endswith(".meta")]
        for name, is_dir in entries:
            path = os.path.join(current, name).replace(os.sep, "/")
            if os.path.exists(path + ".meta"):
                continue
            missing.append(path)
            if not check:
                with open(path + ".meta", "w", encoding="utf-8") as handle:
                    handle.write(meta_text(path, is_dir))
    for path in missing:
        print(("missing: " if check else "created: ") + path + ".meta")
    return 1 if check and missing else 0


if __name__ == "__main__":
    sys.exit(main())
