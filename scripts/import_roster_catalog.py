#!/usr/bin/env python3
"""Import minimal roster names and limits, without bundling original game tables."""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from functools import lru_cache
import json
import re
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

DATA_REVISION = "8a64328fc9a4df7649852ec2ac8b7d5beaedbc58"
TEXT_REVISION = "810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b"
VANILLA_REVISION = "99677e4cad22b636bee4af5a3052003bed17c443"
LANGUAGES = {
    "en": "US/USen", "zh-Hans": "CN/CNch", "zh-Hant": "TW/TWch",
    "ja": "JP/JPja", "ko": "KR/KRko", "de": "EU/EUde",
    "fr": "US/USfr", "es": "US/USes", "it": "EU/EUit",
}
STATS = ["Hp", "Str", "Tech", "Quick", "Luck", "Def", "Magic", "Mdef", "Phys", "Sight", "Move"]
WEAPONS = ["None", "Sword", "Lance", "Axe", "Bow", "Dagger", "Magic", "Rod", "Fist", "Special"]


@lru_cache(maxsize=None)
def fetch(repo: str, revision: str, path: str) -> str:
    url = f"https://raw.githubusercontent.com/{repo}/{revision}/{path}"
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read().decode("utf-8-sig")


def load_texts(files: list[str] | tuple[str, ...]) -> dict[str, dict[str, str]]:
    jobs = [(language, f"{folder}/{name}.txt") for language, folder in LANGUAGES.items() for name in files]
    with ThreadPoolExecutor(max_workers=12) as pool:
        sources = list(pool.map(lambda job: fetch("delvier/Iron19_L10n", TEXT_REVISION, job[1]), jobs))
    texts = {language: {} for language in LANGUAGES}
    for (language, _), source in zip(jobs, sources):
        for line in source.splitlines():
            if "\t" not in line:
                continue
            key, value = line.split("\t", 1)
            # The French class label ends with a native grammar marker, not visible text.
            texts[language][key] = value.replace(r"\x0E\x0A\x00\x06", "")
    return texts


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    texts = load_texts(["Person", "Job", "Item", "Skill", "Patch0", "Patch1", "Patch2", "Patch3"])
    base = "assets/VanillaFiles/"
    people = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "person.xml"))
    jobs = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "job.xml"))
    vanilla = ET.fromstring(fetch("laqieer/FE17-DOC", VANILLA_REVISION, "fe_assets_gamedata/Job.xml"))
    vanilla_jobs = {row.get("Jid"): row for row in vanilla.findall("./Sheet/Data/Param")}
    items = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, base + "item.xml"))
    playable = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, "assets/CharacterData.xml"))
    skill_data = ET.fromstring(fetch("LordMewtwo73/feEngage-randomizer", DATA_REVISION, "assets/SkillData.xml"))
    vanilla_skills = ET.fromstring(fetch("laqieer/FE17-DOC", VANILLA_REVISION, "fe_assets_gamedata/Skill.xml"))
    skill_rows = {row.get("Sid"): row for row in vanilla_skills.findall("./Sheet/Data/Param")}
    ids = {row.get("PID") for row in playable.findall("./Sheet/Data/Param")}

    def class_flags(row: ET.Element) -> int:
        vanilla_row = vanilla_jobs.get(row.get("Jid"))
        if vanilla_row is not None:
            return int(vanilla_row.get("Flag"))
        if row.get("Jid") in {"JID_エンチャント", "JID_マージカノン"}:
            return 11
        if row.get("Jid") in {"JID_裏邪竜ノ娘", "JID_裏邪竜ノ子", "JID_メリュジーヌ_味方"}:
            return 1
        return 0

    def names(row: ET.Element) -> dict:
        key = row.get("Name")
        result = {language: values[key] for language, values in texts.items() if key in values}
        if len(result) != len(texts):
            raise ValueError(f"Missing localized name: {key}")
        return result

    persons = [{"Id": row.get("Pid"), "Names": names(row), "Gender": int(row.get("Gender")), "BirthClass": row.get("Jid"),
                "LimitModifiers": [int(row.get("Limit." + stat)) for stat in STATS]}
               for row in people.findall("./Sheet/Data/Param") if row.get("Pid") in ids]
    classes = [{"Id": row.get("Jid"), "Names": names(row), "MaxLevel": int(row.get("MaxLevel")),
                "Flags": class_flags(row),
                "Advanced": int(row.get("Rank")) == 1,
                "Promotion": row.get("HighJob1"), "LearningSkill": row.get("LearningSkill"),
                "Weapons": [int(row.get("Weapon" + kind)) for kind in WEAPONS],
                "BaseStats": [int(row.get("Base." + stat)) for stat in STATS],
                "Limits": [int(row.get("Limit." + stat)) for stat in STATS]}
               for row in jobs.findall("./Sheet/Data/Param")
               if row.get("Jid") and row.get("Name") in texts["en"] and row.get("Name") in texts["zh-Hans"]]
    item_names = [{"Id": row.get("Iid"), "Names": names(row), "EngageOnly": bool(int(row.get("Flag")) & 128)}
                  for row in items.findall("./Sheet/Data/Param")
                  if row.get("Iid") and row.get("Name") in texts["en"] and row.get("Name") in texts["zh-Hans"]
                  and all(texts[language][row.get("Name")].strip() for language in texts)]
    item_names.append({"Id": "IID_エンゲージ枠", "Names": {"en": "Engage slot", "zh-Hans": "结合栏位", "zh-Hant": "結合欄位",
        "ja": "エンゲージ枠", "ko": "인게이지 슬롯", "de": "Engage-Platz", "fr": "Emplacement Engage",
        "es": "Espacio Engage", "it": "Slot Engage"}, "EngageOnly": True})
    def normalized(value: str) -> str:
        return re.sub(r"\s+", "", value).casefold()

    skills = []
    for row in skill_data.findall("./Sheet/Data/Param"):
        sid = row.get("SID")
        definition = skill_rows.get(sid)
        key = definition.get("Name") if definition is not None else None
        if not key or key not in texts["en"] or key not in texts["zh-Hans"]:
            matches = [key for key, value in texts["en"].items() if key.startswith("MSID_")
                       and not key.startswith("MSID_H_") and normalized(value) == normalized(row.get("Name"))
                       and key in texts["zh-Hans"]]
            if not matches:
                raise ValueError(f"Missing skill translations: {sid} / {row.get('Name')}")
            key = matches[0]
        suffix = re.search(r"[0-9０-９]+$", sid.removesuffix("_継承用"))
        tier = int(suffix.group()) if suffix else len(sid) - len(sid.rstrip("＋"))
        skills.append({"Id": sid, "Names": {language: values[key] for language, values in texts.items()},
                       "Inheritable": row.get("Type") in {"Inherit", "Sync"} and int(row.get("SP") or "0") > 0,
                       "Family": re.sub(r"[0-9０-９＋]+$", "", sid.removesuffix("_継承用")), "Tier": tier})
    # Nel's saved class-skill hash and job-table ID match, but SkillData.xml omits it.
    sid = "SID_裏邪竜ノ娘_兵種スキル"
    key = "MSID_JobSkill_ShadowPrincessR"
    skills.append({"Id": sid, "Names": {language: values[key] for language, values in texts.items()},
                   "Inheritable": False, "Family": sid, "Tier": 0})
    if len(persons) != 41 or len({row["Id"] for row in persons}) != 41:
        raise ValueError("The roster must include all 41 playable characters, including DLC.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "TextRevision": TEXT_REVISION,
                                      "Persons": persons, "Classes": classes, "ItemNames": item_names, "Skills": skills}, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Imported {len(persons)} characters and {len(classes)} classes.")


if __name__ == "__main__":
    main()
