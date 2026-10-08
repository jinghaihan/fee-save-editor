#!/usr/bin/env python3
"""Import ordinary bond-ring melding costs from the complete game parameters."""

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
    source = ET.fromstring(fetch("Xzonn/FireEmblemEngageData", DATA_REVISION, "data/xml/Params.xml"))
    values = {row.get("Name"): row.get("Value") for row in source.findall("./Sheet/Data/Param")}
    rules = [{"SourceRank": rank, "RequiredRings": int(values["指輪合成指輪コスト" + target]),
              "BondFragments": int(values["指輪合成絆のかけらコスト" + target])}
             for rank, target in enumerate(("B", "A", "S"))]
    args.output.write_text(json.dumps({"DataRevision": DATA_REVISION, "Rules": rules}, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(rules)} bond-ring melding rules.")


if __name__ == "__main__":
    main()
