#!/usr/bin/env python3
"""Verify quantity items independently of the convoy, without source writes."""

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def variables(data):
    start = struct.unpack_from("<I", data, 132)[0] + 57
    end = start + struct.unpack_from("<I", data, start)[0]
    count = struct.unpack_from("<I", data, start + 36)[0]
    position = start + 40
    rows = {}
    for _ in range(count):
        length = struct.unpack_from("<I", data, position)[0]
        position += 4
        key = data[position:position + length].decode("utf-16-le")
        position += length
        kind = data[position]
        position += 1
        if kind == 0:
            rows[key] = (struct.unpack_from("<i", data, position)[0], position)
            position += 4
        else:
            length = struct.unpack_from("<I", data, position)[0]
            position += 4 + length
    assert position == end
    return rows


def check_quantities(command, real_directory, fixture):
    def query(*arguments):
        return json.loads(subprocess.check_output([*command, "items", *arguments], text=True))

    catalog = query("quantity-catalog", "--json")
    assert len(catalog) == 105
    seals = [row for row in catalog if row["Category"] == "ReclassItems"]
    assert len(seals) == 4 and all(row["Maximum"] == 999 for row in seals)
    for code in ("en", "zh-Hans", "zh-Hant", "ja", "ko", "de", "fr", "es", "it"):
        localized = query("quantity-catalog", "--language", code, "--json")
        assert len(localized) == 105 and all(row["Name"] for row in localized)
        assert [row["Id"] for row in localized] == [row["Id"] for row in catalog]
    cases = [("synthetic", fixture)]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Manual0", "Auto")]
    with tempfile.TemporaryDirectory(prefix="fee-quantity-test-") as directory:
        root = Path(directory)
        source, output, restored = (root / name for name in ("source", "edited", "restored"))
        for name, original in cases:
            source.write_bytes(original)
            current = query("quantities", str(source), "--json")
            raw = variables(original)
            assert len(current) == 105
            assert all(row["Amount"] == raw.get("G_所持_" + row["Id"], (0, 0))[0] for row in current)
            if name != "synthetic":
                assert all(row["Amount"] == 999 for row in current if row["Category"] == "ReclassItems")
            for item_id in ("IID_マスタープルフ", "IID_エンチャント専用プルフ", "IID_マージカノン専用プルフ", "IID_牛肉", "IID_馬糞", "IID_てつの晶石"):
                subprocess.run([*command, "items", "quantity-set", str(source), str(output), "--item", item_id, "--amount", "998"],
                               check=True, capture_output=True)
                edited = output.read_bytes()
                assert zlib.crc32(edited[:-4]) == struct.unpack_from("<I", edited, len(edited) - 4)[0]
                assert variables(edited)["G_所持_" + item_id][0] == 998
                if "G_所持_" + item_id in raw:
                    offset = raw["G_所持_" + item_id][1]
                    assert len(edited) == len(original)
                    changed = {index for index, pair in enumerate(zip(original[:-4], edited[:-4])) if pair[0] != pair[1]}
                    assert changed <= set(range(offset, offset + 4))
                    subprocess.run([*command, "items", "quantity-set", str(output), str(restored), "--item", item_id,
                                    "--amount", str(raw["G_所持_" + item_id][0])], check=True, capture_output=True)
                    assert restored.read_bytes() == original
                    restored.unlink()
                assert source.read_bytes() == original
                output.unlink()
            for item_id, amount in (("IID_マスタープルフ", "1000"), ("IID_牛肉", "-1"), ("IID_ライブ", "1"),
                                   ("IID_てつの晶石", "10000"), ("IID_unknown", "1"), ("IID_牛肉", "1.5")):
                assert subprocess.run([*command, "items", "quantity-set", str(source), str(output), "--item", item_id,
                                       "--amount", amount], capture_output=True).returncode != 0
                assert not output.exists() and source.read_bytes() == original
            for category in ("ReclassItems", "Materials", "Ingredients", "Gifts"):
                subprocess.run([*command, "items", "quantity-fill", str(source), str(output), "--category", category],
                               check=True, capture_output=True)
                edited = output.read_bytes()
                assert zlib.crc32(edited[:-4]) == struct.unpack_from("<I", edited, len(edited) - 4)[0]
                actual = variables(edited)
                expected = {"G_所持_" + row["Id"]: row["Maximum"] for row in catalog if row["Category"] == category}
                assert all(actual[key][0] == value for key, value in expected.items())
                assert all(actual[key][0] == value[0] for key, value in raw.items() if key not in expected)
                assert all(actual.get("G_所持_" + row["Id"], (0, 0))[0] == row["Amount"]
                           for row in current if row["Category"] != category)
                assert source.read_bytes() == original
                output.unlink()
            for category in ("KeyItems", "Unknown", "99"):
                assert subprocess.run([*command, "items", "quantity-fill", str(source), str(output), "--category", category],
                                      capture_output=True).returncode != 0
                assert not output.exists() and source.read_bytes() == original
    print("Quantity CLI: 105 definitions, all languages, DLC counts, target-only writes, insertion and limits passed.")
