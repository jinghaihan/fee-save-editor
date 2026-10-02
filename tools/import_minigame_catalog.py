#!/usr/bin/env python3
"""Import labels for verified, read-only minigame record variables."""

import argparse
import json
import xml.etree.ElementTree as ET

from import_roster_catalog import TEXT_REVISION, fetch

DATA_REVISION = "86b8be7b9820e1bb3bce87d2a9a805ead85d92ab"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    texts = {}
    for language, folder in (("en", "US/USen"), ("zh-Hans", "CN/CNch")):
        texts[language] = dict(line.split("\t", 1) for line in fetch(
            "delvier/Iron19_L10n", TEXT_REVISION, f"{folder}/Hub.txt").splitlines() if "\t" in line)

    def names(label: str) -> dict[str, str]:
        return {language: values[label] for language, values in texts.items()}

    groups = []
    difficulties = [("Normal", {"en": "Normal", "zh-Hans": "普通"}),
                    ("Hard", {"en": "Hard", "zh-Hans": "困难"}),
                    ("Master", {"en": "Expert", "zh-Hans": "专家"}),
                    ("Eternal", names("MID_Hub_MuscleExercises_Muscle"))]
    for activity, suffix, label in (("PushUps", "PushUp", "PushUps"),
                                    ("SitUps", "SitUp", "Ads"),
                                    ("Squats", "Squat", "Squat")):
        groups.append({"Id": activity, "Names": names("MID_Hub_MuscleExercises_" + label),
                       "Records": [{"Key": f"G_Muscle{suffix}Best{difficulty}", "Names": translated}
                                   for difficulty, translated in difficulties]})
    groups.append({"Id": "WyvernRide", "Names": names("MID_Hub_Talk_DragonRide"),
                   "Records": [{"Key": f"G_DragonRide{difficulty}Score", "Names": translated}
                               for difficulty, (_, translated) in zip(("Normal", "Hard", "Expert"), difficulties[:3])]})
    fish = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/FishingFishData.xml"))
    fish_rows = next(sheet for sheet in fish if sheet.get("Name") == "さかな情報").findall("./Data/Param")
    groups.append({"Id": "Fishing", "Names": {"en": "Fishing", "zh-Hans": "钓鱼"},
                   "Records": [{"Key": f"G_Fishing_{row.get('FishName')}_Count", "Names": names(row.get("NameLabel"))}
                               for row in fish_rows]})
    if len(fish_rows) != 20 or sum(len(group["Records"]) for group in groups) != 35:
        raise ValueError("Unexpected minigame records.")
    with open(args.output, "w", encoding="utf-8") as output:
        json.dump({"DataRevision": DATA_REVISION, "TextRevision": TEXT_REVISION, "Groups": groups},
                  output, ensure_ascii=False, indent=2)
        output.write("\n")
    print("Imported 15 high scores and 20 fish catch counts.")


if __name__ == "__main__":
    main()
