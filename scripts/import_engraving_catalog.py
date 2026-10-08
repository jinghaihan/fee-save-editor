#!/usr/bin/env python3
"""Import minimal engraving effects, shared Emblem identities and weapon eligibility."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import fetch
from import_support_catalog import DATA_REVISION


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    emblems = json.loads((Path(__file__).resolve().parents[1] / "core/Data/emblems.json").read_text(encoding="utf-8"))["Emblems"]
    gods = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/God.xml"))
    rows = {row.get("Gid"): row for row in gods.findall("./Sheet[@Name='神将']/Data/Param")}
    engravings = []
    for emblem in emblems:
        row = rows[emblem["Id"]]
        aliases = [gid for gid in row.get("Change", "").split(";")
                   if gid and gid != emblem["Id"] and rows[gid].get("EngraveWord")]
        engravings.append({"Id": emblem["Id"], "Aliases": aliases,
                           **{field: int(row.get("Engrave" + field))
                              for field in ("Power", "Weight", "Hit", "Critical", "Avoid", "Secure")}})
    items = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Item.xml"))
    weapons = [row.get("Iid") for row in items.findall("./Sheet[@Name='アイテム']/Data/Param")
               if int(row.get("Kind")) in (1, 2, 3, 4, 5, 6, 8, 9)
               and not int(row.get("Flag")) & (128 | 134217728)]
    if len(engravings) != 20 or not weapons or len(set(weapons)) != len(weapons):
        raise ValueError("Incomplete or duplicated engraving catalog.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "Engravings": engravings,
                                    "Weapons": weapons}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(engravings)} engravings and {len(weapons)} eligible weapons.")


if __name__ == "__main__":
    main()
