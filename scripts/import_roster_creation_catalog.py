#!/usr/bin/env python3
"""Import starting personal offsets and proficiencies for playable unit creation."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import DATA_REVISION, STATS, fetch


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    people = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, "assets/VanillaFiles/person.xml"))
    playable = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, "assets/CharacterData.xml"))
    ids = {row.get("PID") for row in playable.findall("./Sheet/Data/Param")}
    records = []
    for row in people.findall("./Sheet/Data/Param"):
        if row.get("Pid") not in ids:
            continue
        records.append({"Id": row.get("Pid"), "InternalLevel": int(row.get("InternalLevel")),
            "SkillPoints": int(row.get("SkillPoint")), "OriginalProficiencies": int(row.get("Aptitude")),
            "Proficiencies": int(row.get("Aptitude")) | int(row.get("SubAptitude")),
            "PersonalStats": [[int(row.get("Offset" + suffix + "." + stat)) for stat in STATS]
                for suffix in ("N", "H", "L")]})
    if len(records) != 41 or len({row["Id"] for row in records}) != 41:
        raise ValueError("Creation defaults must contain all 41 unique playable characters.")
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "Persons": records}, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()
