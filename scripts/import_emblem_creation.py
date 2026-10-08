#!/usr/bin/env python3
"""Import normal Emblem creation data, including all style-specific Engage weapons."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import fetch

REVISION = "86b8be7b9820e1bb3bce87d2a9a805ead85d92ab"
WEAPON_FIELDS = ("EngageItems", "EngageCooperations", "EngageHorses", "EngageCoverts",
                 "EngageHeavys", "EngageFlys", "EngageMagics", "EngagePranas", "EngageDragons")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    catalog = json.loads((Path(__file__).resolve().parents[1] / "core/Data/emblems.json").read_text(encoding="utf-8"))
    normal = {row["Id"] for row in catalog["Emblems"] if row["Id"] != "GID_リュール"}
    god = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", REVISION, "data/xml/God.xml"))
    groups = {}
    group = None
    for row in god.findall("./Sheet[@Name='成長表']/Data/Param"):
        if row.get("Ggid"):
            group = row.get("Ggid")
            groups[group] = []
        for field in WEAPON_FIELDS:
            for iid in (row.get(field) or "").split(";"):
                if iid and iid not in groups[group]:
                    groups[group].append(iid)
    entries = []
    for row in god.findall("./Sheet[@Name='神将']/Data/Param"):
        if row.get("Gid") not in normal:
            continue
        weapons = groups[row.get("GrowTable")]
        if row.get("Link") or row.get("Level") != "1" or not 1 <= len(weapons) <= 255:
            raise ValueError("Unsupported normal Emblem creation data.")
        entries.append({"Id": row.get("Gid"), "Weapons": weapons})
    if len(entries) != 19 or {row["Id"] for row in entries} != normal:
        raise ValueError("Incomplete normal Emblem creation catalog.")
    args.output.write_text(json.dumps({"DataRevision": REVISION, "Emblems": entries},
                                     ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(entries)} normal Emblems with Engage weapon initialization data.")


if __name__ == "__main__":
    main()
