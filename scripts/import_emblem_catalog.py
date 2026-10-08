#!/usr/bin/env python3
"""Import localized Emblem/ring identifiers and the game's bond EXP thresholds."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import STATS, TEXT_REVISION, VANILLA_REVISION, fetch, load_texts

DLC = {
    "GID_エーデルガルト": "MGID_Edelgard", "GID_チキ": "MGID_Tiki",
    "GID_ヘクトル": "MGID_Hector", "GID_セネリオ": "MGID_Senerio",
    "GID_カミラ": "MGID_Camilla", "GID_クロム": "MGID_Chrom",
    "GID_ヴェロニカ": "MGID_Veronica",
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    texts = load_texts(("Person", "BondsRing", "Skill", "Patch0", "Patch1", "Patch2", "Patch3"))

    def names(key: str) -> dict:
        result = {language: values[key].replace(r"\n", "\n") for language, values in texts.items()}
        if any(not value.strip() for value in result.values()):
            raise ValueError(f"Empty translation: {key}")
        return result

    god = ET.fromstring(fetch("laqieer/FE17-DOC", VANILLA_REVISION, "fe_assets_gamedata/God.xml"))
    rings = ET.fromstring(fetch("laqieer/FE17-DOC", VANILLA_REVISION, "fe_assets_gamedata/Ring.xml"))
    skills = ET.fromstring(fetch("laqieer/FE17-DOC", VANILLA_REVISION, "fe_assets_gamedata/Skill.xml"))
    skill_rows = {row.get("Sid"): row for row in skills.findall("./Sheet/Data/Param")}

    def ring_skills(row: ET.Element) -> list[dict]:
        result = []
        for sid in filter(None, row.get("EquipSids", "").split(";")):
            skill = skill_rows[sid]
            result.append({"Id": sid, "Names": names(skill.get("Name")), "Descriptions": names(skill.get("Help"))})
        return result
    rows = god.find("./Sheet[@Name='神将']/Data").findall("Param")
    playable = {"GID_" + name for name in ("マルス", "シグルド", "セリカ", "ミカヤ", "ロイ", "リーフ",
                "ルキナ", "リン", "アイク", "ベレト", "カムイ", "エイリーク", "リュール")}
    emblems = [{"Id": row.get("Gid"), "Names": names(row.get("Mid")),
                "LevelCapVariable": row.get("UnlockLevelCapVarName") or None, "Dlc": False}
               for row in rows if row.get("Gid") in playable]
    emblems.extend({"Id": gid, "Names": names(key), "LevelCapVariable": None, "Dlc": True}
                   for gid, key in DLC.items())
    ring_rows = rings.findall("./Sheet/Data/Param")
    ring_data = [{"Id": row.get("Rnid"), "Names": names(row.get("Name")), "EmblemId": row.get("Gid"),
                  "Rank": int(row.get("Rank")), "SingleRank": row.get("IsSingleRank").lower() == "true",
                  "StatBonuses": [int(row.get("Enhance." + stat, "0")) for stat in STATS], "Skills": ring_skills(row)}
                 for row in ring_rows if row.get("Rnid") and all(row.get("Name") in values for values in texts.values())]
    levels = god.find("./Sheet[@Name='絆レベル']/Data").findall("Param")
    thresholds = [int(row.get("Exp")) for row in levels if 1 <= int(row.get("Level")) <= 20]
    if len(emblems) != 20 or len({row["Id"] for row in emblems}) != 20 or len(thresholds) != 20:
        raise ValueError("Incomplete Emblem or bond-level catalog.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"DataRevision": VANILLA_REVISION, "TextRevision": TEXT_REVISION,
        "Emblems": emblems, "Rings": ring_data, "BondExperience": thresholds}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    print(f"Imported {len(emblems)} Emblems, {len(ring_data)} rings and {len(thresholds)} bond levels.")


if __name__ == "__main__":
    main()
