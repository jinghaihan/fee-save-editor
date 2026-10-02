#!/usr/bin/env python3
"""Build minimal item facts from FE17-DOC; do not bundle original game tables."""

import argparse
import csv
import io
import json
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import load_texts

SOURCE = "https://raw.githubusercontent.com/laqieer/FE17-DOC/"


def fetch(revision: str, relative: str) -> str:
    with urllib.request.urlopen(SOURCE + revision + "/" + relative) as response:
        return response.read().decode("utf-8-sig")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", required=True, help="Pinned FE17-DOC commit")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    book = ET.fromstring(fetch(args.revision, "fe_assets_gamedata/Item.xml"))
    texts = {row["key"]: row for row in csv.DictReader(io.StringIO(fetch(args.revision, "translations/Item.csv")))}
    localized = load_texts(["Item", "Patch0", "Patch1", "Patch2", "Patch3"])
    levels = {}
    group = None
    for record in book.findall("Sheet")[2].findall("./Data/Param"):
        if record.get("Rid"):
            group = record.get("Rid")
            levels[group] = 0
        elif group:
            levels[group] += 1
    items = []
    for record in book.findall("Sheet")[0].findall("./Data/Param"):
        iid = record.get("Iid")
        kind = int(record.get("Kind"))
        flags = int(record.get("Flag"))
        text = texts.get(record.get("Name", "").removeprefix("MIID_"))
        if not text or kind not in range(1, 11) or flags & (8 | 16 | 128 | 512 | 1024):
            continue
        items.append({"Id": iid, "English": text["usen"], "Chinese": text["cnch"],
                      "Names": {language: values[record.get("Name")] for language, values in localized.items()}, "Kind": kind,
                      "MaxUses": int(record.get("Endurance")), "MaxRefine": levels.get("RID_" + iid[4:], 0)})
    if not items or len({item["Id"] for item in items}) != len(items):
        raise ValueError("The source item catalog is incomplete or duplicated.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"SourceRevision": args.revision, "Items": items}, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Imported {len(items)} item definitions into {args.output}")


if __name__ == "__main__":
    main()
