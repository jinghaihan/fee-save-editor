#!/usr/bin/env python3
"""Import minimal roster names and limits, without bundling original game tables."""

import argparse
import json
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

DATA_REVISION = "8a64328fc9a4df7649852ec2ac8b7d5beaedbc58"
TEXT_REVISION = "810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b"
STATS = ["Hp", "Str", "Tech", "Quick", "Luck", "Def", "Magic", "Mdef", "Phys", "Sight", "Move"]


def fetch(repo: str, revision: str, path: str) -> str:
    url = f"https://raw.githubusercontent.com/{repo}/{revision}/{path}"
    with urllib.request.urlopen(url) as response:
        return response.read().decode("utf-8-sig")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    texts = {}
    for language, folder in [("en", "US/USen"), ("zh-Hans", "CN/CNch")]:
        values = {}
        for name in ["Person", "Job", "Item", "Patch0", "Patch1", "Patch2", "Patch3"]:
            source = fetch("delvier/Iron19_L10n", TEXT_REVISION, f"{folder}/{name}.txt")
            values.update(line.split("\t", 1) for line in source.splitlines() if "\t" in line)
        texts[language] = values
    base = "assets/VanillaFiles/"
    people = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "person.xml"))
    jobs = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "job.xml"))
    items = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "item.xml"))
    playable = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, "assets/CharacterData.xml"))
    ids = {row.get("PID") for row in playable.findall("./Sheet/Data/Param")}

    def names(row: ET.Element) -> dict:
        key = row.get("Name")
        result = {language: values[key] for language, values in texts.items() if key in values}
        if len(result) != len(texts):
            raise ValueError(f"Missing localized name: {key}")
        return result

    persons = [{"Id": row.get("Pid"), "Names": names(row),
                "LimitModifiers": [int(row.get("Limit." + stat)) for stat in STATS]}
               for row in people.findall("./Sheet/Data/Param") if row.get("Pid") in ids]
    classes = [{"Id": row.get("Jid"), "Names": names(row), "MaxLevel": int(row.get("MaxLevel")),
                "BaseStats": [int(row.get("Base." + stat)) for stat in STATS],
                "Limits": [int(row.get("Limit." + stat)) for stat in STATS]}
               for row in jobs.findall("./Sheet/Data/Param")
               if row.get("Jid") and row.get("Name") in texts["en"] and row.get("Name") in texts["zh-Hans"]]
    item_names = [{"Id": row.get("Iid"), "Names": names(row), "EngageOnly": bool(int(row.get("Flag")) & 128)}
                  for row in items.findall("./Sheet/Data/Param")
                  if row.get("Iid") and row.get("Name") in texts["en"] and row.get("Name") in texts["zh-Hans"]
                  and all(texts[language][row.get("Name")].strip() for language in texts)]
    item_names.append({"Id": "IID_エンゲージ枠", "Names": {"en": "Engage slot", "zh-Hans": "结合栏位"}, "EngageOnly": True})
    if len(persons) != 41 or len({row["Id"] for row in persons}) != 41:
        raise ValueError("The roster must include all 41 playable characters, including DLC.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "TextRevision": TEXT_REVISION,
                                      "Persons": persons, "Classes": classes, "ItemNames": item_names}, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Imported {len(persons)} characters and {len(classes)} classes.")


if __name__ == "__main__":
    main()
