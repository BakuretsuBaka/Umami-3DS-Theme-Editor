#!/usr/bin/env python3
"""Builds the satellite .resx files from tools/i18n/<culture>.txt.

Each .txt file holds `Key=Value` lines (\\n escapes a newline, '#' starts a comment). Keys that are missing
fall back to the English neutral resource at run time, so a translation can be extended gradually.
Run `python3 tools/make_resx.py` and then `python3 tools/gen_resources.py` after changing neutral strings.
"""
import glob
import os
import re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
LOC = os.path.join(HERE, "..", "src", "ThemeEditor.App", "Localization")

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'''


def neutral_keys(name):
    root = ET.parse(os.path.join(LOC, name + ".resx")).getroot()
    return {d.get("name") for d in root.findall("data") if not d.get("type") and not d.get("mimetype")}


def existing(path):
    if not os.path.exists(path):
        return {}
    root = ET.parse(path).getroot()
    return {d.get("name"): (d.find("value").text or "") for d in root.findall("data")}


def load_txt(path):
    out = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            k, _, v = line.partition("=")
            out[k.strip()] = v.replace("\\n", "\n")
    return out


def write(path, items):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(HEADER)
        for k in sorted(items):
            f.write('  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n' % (k, escape(items[k])))
        f.write("</root>\n")


ORIGINAL_CULTURES = {"es", "fr", "it", "pt-BR"}

main_keys = neutral_keys("MainResources")
theme_keys = neutral_keys("ThemeResources")

for txt in sorted(glob.glob(os.path.join(HERE, "i18n", "*.txt"))):
    culture = os.path.splitext(os.path.basename(txt))[0]
    tr = load_txt(txt)
    for res, keys in (("MainResources", main_keys), ("ThemeResources", theme_keys)):
        path = os.path.join(LOC, "%s.%s.resx" % (res, culture))
        merged = existing(path)
        merged = {k: v for k, v in merged.items() if k in keys}
        for k, v in tr.items():
            if k in keys and not k.endswith("_Order"):
                # Hand-made translations that shipped with the original editor are never overwritten
                if culture in ORIGINAL_CULTURES and k in merged:
                    continue
                merged[k] = v
        write(path, merged)
    print(culture, len(tr), "strings")
