#!/usr/bin/env python3
"""Build separate convoy and quantity catalogs from the complete base/DLC tables."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import fetch, load_texts, TEXT_REVISION
from import_support_catalog import DATA_REVISION


def quantity_category(record):
    kind, use, flags = (int(record.get(field)) for field in ("Kind", "UseType", "Flag"))
    if kind in (14, 15, 16):
        return "Materials"
    if kind == 13 or (kind == 10 and use == 35):
        return "KeyItems"
    if kind == 10 and not flags & 4:
        return {23: "ReclassItems", 24: "ReclassItems", 40: "ReclassItems", 41: "ReclassItems",
                32: "Ingredients", 33: "Gifts", 34: "Materials"}.get(use)
    return None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--quantity-output", required=True, type=Path)
    args = parser.parse_args()
    book = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Item.xml"))
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
    quantities = []
    for record in book.findall("Sheet")[0].findall("./Data/Param"):
        iid = record.get("Iid")
        kind = int(record.get("Kind"))
        flags = int(record.get("Flag"))
        key = record.get("Name")
        if flags & (8 | 16 | 128 | 512 | 1024) or not all(localized[language].get(key, "").strip() for language in localized):
            continue
        names = {language: values[key] for language, values in localized.items()}
        category = quantity_category(record)
        if category:
            quantities.append({"Id": iid, "Names": names, "Category": category,
                               "Maximum": 9999 if kind in (14, 15, 16) else 999})
            continue
        if kind not in range(1, 11) or (kind == 10 and not flags & 4):
            continue
        items.append({"Id": iid, "English": names["en"], "Chinese": names["zh-Hans"], "Kind": kind,
                      "MaxUses": int(record.get("Endurance")), "MaxRefine": levels.get("RID_" + iid[4:], 0), "Names": names})
    if not items or len({item["Id"] for item in items}) != len(items):
        raise ValueError("The source item catalog is incomplete or duplicated.")
    if len(quantities) != 105 or len({row["Id"] for row in quantities}) != len(quantities):
        raise ValueError("The quantity catalog must contain all 105 base/DLC entries.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.quantity_output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"SourceRevision": DATA_REVISION, "Items": items}, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    args.quantity_output.write_text(json.dumps({"SourceRevision": DATA_REVISION, "TextRevision": TEXT_REVISION,
        "Items": quantities}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(items)} item definitions into {args.output}")
    print(f"Imported {len(quantities)} quantity definitions into {args.quantity_output}")


if __name__ == "__main__":
    main()
