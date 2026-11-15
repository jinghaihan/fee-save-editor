#!/usr/bin/env python3
"""Import chapter titles and historical Emblem variants in all nine languages."""
import xml.etree.ElementTree as ET
from import_roster_catalog import fetch, load_texts
from catalog_data import write_catalog

DATA_REVISION = "86b8be7b9820e1bb3bce87d2a9a805ead85d92ab"

def main():
    texts = load_texts(["Person", "GameData", "Patch0", "Patch1", "Patch2", "Patch3"])
    def names(key):
        return {lang: values[key] for lang, values in texts.items() if key in values}
    def table(name):
        return ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/" + name + ".xml")).findall(".//Data/Param")
    chapters = []
    for row in table("Chapter"):
        cid, key = row.get("Cid"), row.get("Name", "")
        if not cid or not key:
            continue
        key = key.replace("*", cid[4:])
        title = names(key)
        if not title:
            continue
        if len(title) != len(texts):
            raise ValueError("Missing chapter translations: " + cid)
        chapters.append({"ID": cid, "Names": title, "Prefixes": names(key + "_PREFIX")})
    emblems = []
    for row in table("God"):
        if row.get("Gid") in {"GID_エフラム", "GID_ディミトリ", "GID_クロード", "GID_ルフレ"}:
            title = names(row.get("Mid"))
            if len(title) != len(texts):
                raise ValueError("Missing Emblem variant translations")
            emblems.append({"ID": row.get("Gid")[4:], "Names": title})
    if len(emblems) != 4 or len(chapters) < 53:
        raise ValueError("Incomplete chapter record catalog")
    write_catalog("chapter-records", {"Chapters": chapters, "Emblems": emblems})
    print(f"Imported {len(chapters)} chapters and {len(emblems)} Emblem variants")

if __name__ == "__main__":
    main()
