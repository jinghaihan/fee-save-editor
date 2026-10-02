#!/usr/bin/env python3
"""Import named achievements and resolve their English and Chinese messages."""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from import_donation_catalog import DATA_REVISION
from import_roster_catalog import TEXT_REVISION, fetch

TEXT_FILES = ("Achieve", "GameData", "Hub", "HubCommon", "Person", "Network", "ResidentMenu", "Patch0", "Patch1", "Patch2", "Patch3")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Achieve.xml"))
    people = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Person.xml"))
    person_names = {row.get("Pid"): row.get("Name") for row in people.findall("./Sheet/Data/Param")}
    jobs = [(language, f"{folder}/{name}.txt") for language, folder in (("en", "US/USen"), ("zh-Hans", "CN/CNch"))
            for name in TEXT_FILES]
    with ThreadPoolExecutor(max_workers=8) as pool:
        contents = list(pool.map(lambda job: fetch("delvier/Iron19_L10n", TEXT_REVISION, job[1]), jobs))
    texts = {"en": {}, "zh-Hans": {}}
    for (language, _), content in zip(jobs, contents):
        texts[language].update(line.split("\t", 1) for line in content.splitlines() if "\t" in line)

    def name(row: ET.Element, language: str) -> str:
        values = texts[language]
        slots = {}
        argument = row.get("Arg")
        if argument.startswith("PID_"):
            slots[0] = values[person_names[argument]]
        elif argument.startswith("MID_"):
            slots[0] = values[argument]
        elif argument.startswith("CID_"):
            message = "MCID_" + argument[4:]
            slots[0], slots[1] = values[message + "_PREFIX"], values[message]
        elif argument:
            raise ValueError(f"Unknown achievement argument: {argument}")
        if int(row.get("Count")) >= 1:
            slots[len(slots)] = str(int(row.get("Count")))
        message = values[row.get("Name")]
        token = r"\\x0E\\x01\\x([0-9A-Fa-f]{2})\\x00"

        def parameter(match: re.Match) -> str:
            slot = int(match[1], 16)
            if slot in (8, 10, 11):
                return ""
            return slots[slot]

        message = re.sub(token, parameter, message)
        message = message.replace(r"\x0E\x08\x02\x1A\x18RelianceRing", "S")
        message = message.replace(r"\n", " ").strip()
        if not message or r"\x" in message:
            raise ValueError(f"Unresolved achievement name: {row.get('Aid')} / {language}: {message}")
        return message

    rows = []
    for row in root[0].findall("./Data/Param"):
        if not row.get("Name") or int(row.get("Category")) == 5:
            continue
        rows.append({"Id": row.get("Aid"), "Category": int(row.get("Category")),
                     "Names": {language: name(row, language) for language in texts},
                     "Reward": int(row.get("KizunaReward")), "Chapter": row.get("Chapter")})
    if len(rows) != 765 or len({row["Id"] for row in rows}) != len(rows):
        raise ValueError("Unexpected achievement catalog size or duplicate IDs.")
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "TextRevision": TEXT_REVISION,
                                      "Achievements": rows}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(rows)} named achievements; internal play-report counters excluded.")


if __name__ == "__main__":
    main()
