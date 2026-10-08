#!/usr/bin/env python3
"""Import the base/DLC support matrix and cumulative rank thresholds."""

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

from import_roster_catalog import fetch

DATA_REVISION = "86b8be7b9820e1bb3bce87d2a9a805ead85d92ab"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    book = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Reliance.xml"))
    matrix = book.find("./Sheet[@Name='支援関係']/Data").findall("Param")
    patterns = book.find("./Sheet[@Name='必要経験値']/Data").findall("Param")
    pairs = []
    for index, row in enumerate(matrix):
        for other_index, other in enumerate(matrix[:index]):
            pattern = int(row.get(f"ExpType{other_index}"))
            if not pattern:
                continue
            entry = patterns[pattern]
            thresholds = [int(entry.get(f"Exp{rank}")) for rank in "CBA"]
            if not 0 < thresholds[0] < thresholds[1] < thresholds[2] <= 100:
                raise ValueError("Invalid support thresholds.")
            pairs.append({"FirstPersonId": other.get("Pid"), "SecondPersonId": row.get("Pid"),
                          "Thresholds": thresholds})
    if len(matrix) != 41 or len(pairs) != 231:
        raise ValueError("Incomplete base/DLC support matrix.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "Pairs": pairs},
                                    ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(pairs)} support pairs across {len(matrix)} characters.")


if __name__ == "__main__":
    main()
