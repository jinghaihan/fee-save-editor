#!/usr/bin/env python3
"""Update localized names while preserving verified catalog facts and identifiers."""

import importlib
import json
import sys
import tempfile
from pathlib import Path

from import_roster_catalog import LANGUAGES

ROOT = Path(__file__).resolve().parents[1]


def merge_names(existing, generated, key="Id"):
    lookup = {row[key]: row for row in generated}
    for row in existing:
        names = lookup[row[key]]["Names"]
        if set(names) != set(LANGUAGES) or any(not name.strip() for name in names.values()):
            raise ValueError(f"Incomplete translations: {row[key]}")
        for language in ("en", "zh-Hans"):
            if row["Names"][language] != names[language]:
                raise ValueError(f"Existing name changed: {row[key]} / {language}")
        row["Names"] = names


def main():
    outputs = {}
    with tempfile.TemporaryDirectory(prefix="fee-languages-") as directory:
        for catalog, collections in {
            "roster": ("Persons", "Classes", "ItemNames", "Skills"),
            "emblem": ("Emblems", "Rings"),
            "donation": ("Countries",),
            "achievement": ("Achievements",),
            "minigame": ("Groups",),
        }.items():
            filename = {"roster": "roster", "emblem": "emblems", "donation": "donations",
                        "achievement": "achievements", "minigame": "minigames"}[catalog] + ".json"
            generated_path = Path(directory) / filename
            sys.argv = ["import", "--output", str(generated_path)]
            importlib.import_module(f"import_{catalog}_catalog").main()
            generated = json.loads(generated_path.read_text(encoding="utf-8"))
            path = ROOT / "core/Data" / filename
            existing = json.loads(path.read_text(encoding="utf-8"))
            for collection in collections:
                merge_names(existing[collection], generated[collection])
                if collection == "Groups":
                    groups = {row["Id"]: row for row in generated[collection]}
                    for group in existing[collection]:
                        merge_names(group["Records"], groups[group["Id"]]["Records"], "Key")
            outputs[path] = existing
            print(f"Validated {filename}: all nine languages; existing names and facts preserved.", flush=True)

        item_path = ROOT / "core/Data/items.json"
        items = json.loads(item_path.read_text(encoding="utf-8"))
        roster = outputs[ROOT / "core/Data/roster.json"]
        lookup = {row["Id"]: row["Names"] for row in roster["ItemNames"]}
        for item in items["Items"]:
            names = lookup[item["Id"]]
            if names["en"] != item["English"] or names["zh-Hans"] != item["Chinese"]:
                raise ValueError(f"Convoy item name changed: {item['Id']}")
            item["Names"] = names
        outputs[item_path] = items

    # Write only after every catalog has passed validation.
    for path, data in outputs.items():
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Updated localized game names without changing limits, IDs or save bytes.")


if __name__ == "__main__":
    main()
