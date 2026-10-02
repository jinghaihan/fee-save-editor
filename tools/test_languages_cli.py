"""Verify every supported CLI language against the pinned localized game catalogs."""

import json
import subprocess
from pathlib import Path


def check_languages(command):
    root = Path(__file__).resolve().parents[1]
    catalog = lambda name: json.loads((root / "core/Data" / f"{name}.json").read_text(encoding="utf-8"))
    countries = catalog("donations")["Countries"]
    emblems = catalog("emblems")["Emblems"]
    achievements = catalog("achievements")["Achievements"]
    classes = [row for row in catalog("roster")["Classes"] if row["Flags"] & 1]
    for language in ("en", "zh-Hans", "zh-Hant", "ja", "ko", "de", "fr", "es", "it"):
        def query(*args):
            return json.loads(subprocess.check_output([*command, *args, "--language", language],
                                                      text=True, encoding="utf-8"))
        actual = query("main", "donation-catalog")
        assert [row["Name"] for row in actual] == [row["Names"][language] for row in countries]
        actual = query("emblems", "catalog")
        assert [row["Name"] for row in actual["Emblems"]] == [row["Names"][language] for row in emblems]
        actual = query("achievements", "catalog")
        assert [row["Name"] for row in actual] == [row["Names"][language] for row in achievements]
        actual = query("roster", "catalog")
        assert [row["Name"] for row in actual["Classes"]] == [row["Names"][language] for row in classes]
    print("All nine CLI languages match the game catalogs.")
