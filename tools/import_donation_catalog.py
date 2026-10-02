#!/usr/bin/env python3
"""Import the four editable donation nations and cumulative level thresholds."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import TEXT_REVISION, fetch, load_texts

DATA_REVISION = "86b8be7b9820e1bb3bce87d2a9a805ead85d92ab"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/HubInvestment.xml"))
    sheets = {sheet.get("Name"): sheet for sheet in source}
    levels = {}
    group = None
    for row in sheets["レベル情報"].findall("./Data/Param"):
        if row.get("Group"):
            group = row.get("Group")
            levels[group] = []
        else:
            previous = levels[group][-1] if levels[group] else 0
            levels[group].append(previous + int(row.get("Cost")))
    texts = load_texts(("GameData", "Hub", "HubCommon"))
    countries = []
    for row in sheets["国データ"].findall("./Data/Param"):
        if row.get("IsNotLevel") != "false":
            continue
        countries.append({"Id": row.get("ID"), "Key": "G_投資_" + row.get("ID")[4:],
                          "Names": {language: values[row.get("Name")] for language, values in texts.items()},
                          "Thresholds": levels[row.get("LevelInfo")]})
    if len(countries) != 4 or any(country["Thresholds"] != [0, 5000, 15000, 40000, 90000] for country in countries):
        raise ValueError("Unexpected donation countries or costs.")
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "TextRevision": TEXT_REVISION, "Countries": countries}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(countries)} donation countries.")


if __name__ == "__main__":
    main()
