#!/usr/bin/env python3
"""Import named achievements and resolve the game's localized message parameters."""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from import_donation_catalog import DATA_REVISION
from import_roster_catalog import TEXT_REVISION, fetch, load_texts

TEXT_FILES = ("Achieve", "GameData", "Hub", "HubCommon", "Person", "Network", "ResidentMenu", "Patch0", "Patch1", "Patch2", "Patch3")


def korean_particles(message: str) -> str:
    pattern = r"\\x0E\\x0A(?:\\x[0-9A-Fa-f]{2}){3}(과|이|｣을|으로)\\x[0-9A-Fa-f]{2}(와|가|｣를|로)"

    def choose(match: re.Match) -> str:
        prefix = message[:match.start()].rstrip(" ｣」\"'")
        final = (ord(prefix[-1]) - 0xAC00) % 28 if prefix and "가" <= prefix[-1] <= "힣" else 0
        consonant = final != 0 and not (match[1] == "으로" and final == 8)
        return match[1] if consonant else match[2]

    return re.sub(pattern, choose, message)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Achieve.xml"))
    people = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Person.xml"))
    person_names = {row.get("Pid"): row.get("Name") for row in people.findall("./Sheet/Data/Param")}
    texts = load_texts(TEXT_FILES)

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
        if language == "ko":
            message = korean_particles(message)
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
