#!/usr/bin/env python3
"""Turns the translation tables in this folder into C# files next to Loc.cs.

Run from the repository root after editing a table:  python3 tools/l10n/build.py
"""
import importlib
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
LANGUAGES = {"uz": ("LocUz", "Uzbek (Latin)")}


def quote(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


for code, (name, title) in LANGUAGES.items():
    table = importlib.import_module(code).T
    lines = [
        "using System.Collections.Generic;", "", "namespace Skyforge", "{",
        "    /// <summary>%s. Written by tools/l10n/build.py from tools/l10n/%s.py; edit that file, not this one.</summary>" % (title, code),
        "    public static class %s" % name, "    {",
        "        public static readonly Dictionary<string, string> Table = new Dictionary<string, string>", "        {",
    ]
    lines += ["            { %s, %s }," % (quote(key), quote(value)) for key, value in table.items()]
    lines += ["        };", "    }", "}", ""]
    with open("Assets/Skyforge/Core/%s.cs" % name, "w") as handle:
        handle.write("\n".join(lines))
    print(name, len(table))
