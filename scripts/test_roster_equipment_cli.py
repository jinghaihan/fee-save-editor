"""Check equipment ownership and conserved ring stock with independent binary fixtures."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from collections import Counter
from pathlib import Path

from test_emblems_cli import fixture as emblem_fixture, ring_records, sections
from test_roster_cli import game_hash, records, unit


def fixture(base: bytes) -> bytes:
    base = emblem_fixture(base)
    pools = sections(base)
    units = struct.pack("<II", 0, 0xcdcdcdcd) + bytes(24) + bytes((3, 2))
    for index, (person, job) in enumerate((("PID_リュール", "JID_神竜ノ子"), ("PID_ユナカ", "JID_シーフ"))):
        old = unit(person, job, False)
        trailer = struct.pack("<HBBHBHIIIIBI", 0, 4, 0, 0, 0, 0, 1 if index == 0 else 0, 0, 0, 0, 0, 0)
        record = old[:-58] + trailer + old[-10:]
        units += struct.pack("<I", len(record)) + record[4:]
    pools[b"TINU"] = units + b"\xff"
    gods = struct.pack("<II", 8, 0xcdcdcdcd) + bytes(24) + struct.pack("<I", 3)
    for instance, god in ((1, "GID_マルス"), (2, "GID_チキ"), (3, "GID_リュール")):
        gods += struct.pack("<IHII", instance, 0xefcd, game_hash(god), instance) + bytes(6)
    pools[b" DOG"] = gods
    parts = [tag + struct.pack("<I", len(payload) + 4) + payload for tag, payload in pools.items()]
    offsets, position = [], 260
    for part in parts:
        offsets.append(position)
        position += len(part)
    offsets.append(position)
    body = base[:132] + struct.pack("<32I", *offsets, *([0] * (32 - len(offsets)))) + b"".join(parts) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def totals(data: bytes) -> Counter:
    result = Counter()
    for _, ring_hash, stock in ring_records(data):
        result[ring_hash] += stock
    return result


def check_roster_equipment(command: list[str], real_directory: Path | None, base: bytes) -> None:
    cases = [("synthetic", fixture(base))]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Manual0", "Auto")]
    with tempfile.TemporaryDirectory(prefix="fee-equipment-cli-") as directory:
        root = Path(directory)

        def run(*args: str, valid: bool = True) -> str:
            result = subprocess.run([*command, "roster", *args], capture_output=True, text=True, encoding="utf-8")
            assert (result.returncode == 0) == valid, (args, result.stderr)
            return result.stdout

        def equipment(path: Path, index: int = 0, language: str = "en") -> dict:
            return json.loads(run("equipment", str(path), "--character", str(index), "--json", "--language", language))

        for name, original in cases:
            source, output, transferred, returned = [root / f"{name}-{suffix}" for suffix in ("source", "edited", "transferred", "returned")]
            source.write_bytes(original)
            choices = equipment(source)
            assert choices["Current"] == {"Kind": "Emblem", "InstanceId": 1}
            assert any(option["Name"] == "马尔斯" for option in equipment(source, language="zh-Hans")["Options"])
            ring = next(option for option in choices["Options"] if option["Selection"]["Kind"] == "BondRing" and option["StockCount"] > 1)
            tiki = next(option for option in choices["Options"] if option["EmblemId"] == "GID_チキ")
            for option, flag in ((tiki, "--emblem"), (ring, "--ring")):
                run("equipment-set", str(source), str(output), "--character", "0", flag, str(option["Selection"]["InstanceId"]))
                edited = output.read_bytes()
                current = equipment(output)
                assert current["Current"]["Kind"] == option["Selection"]["Kind"]
                worn_id = current["Current"]["InstanceId"]
                worn = next(row for row in current["Options"] if row["Selection"] == current["Current"])
                assert worn["OwnerIndex"] == 0
                if flag == "--ring":
                    assert worn["RingHash"] == ring["RingHash"] and worn["StockCount"] == 1
                    assert next(row for row in ring_records(edited) if row[0] == ring["Selection"]["InstanceId"])[2] == ring["StockCount"] - 1
                assert totals(edited) == totals(original)
                for tag, payload in sections(original).items():
                    if tag not in (b"TINU", b"GNIR"):
                        assert sections(edited)[tag] == payload, (name, tag)
                if name == "synthetic":
                    unit_offset = next(offset for offset in struct.unpack_from("<32I", original, 132)
                                       if offset and original[offset:offset + 4] == b"TINU") + 8
                    allowed = set()
                    for start in records(original):
                        end = start + struct.unpack_from("<I", original, start)[0]
                        links = end - 31
                        for first, last in ((start + 36, start + 44), (end - 40, end - 38),
                                            (links, links + 4), (links + 8, links + 12)):
                            allowed.update(range(first - unit_offset, last - unit_offset))
                    assert {i for i, pair in enumerate(zip(sections(original)[b"TINU"], sections(edited)[b"TINU"]))
                            if pair[0] != pair[1]} <= allowed
                run("equipment-set", str(output), str(transferred), "--character", "1", flag, str(worn_id))
                assert equipment(transferred)["Current"]["Kind"] == "None"
                assert equipment(transferred, 1)["Current"]["InstanceId"] == worn_id
                run("equipment-set", str(transferred), str(returned), "--character", "1", "--none", "true")
                assert equipment(returned, 1)["Current"]["Kind"] == "None"
                assert totals(returned.read_bytes()) == totals(original)
                for path in (output, transferred, returned):
                    path.unlink()
            run("equipment-set", str(source), str(output), "--character", "0", "--emblem", "1")
            assert output.read_bytes() == original
            output.unlink()
            invalid = (("--emblem", "99999"), ("--ring", "0"), ("--ring", "-1"), ("--ring", "1.5"),
                       ("--none", "false"), ("--emblem", "1", "--ring", "10"), (),
                       ("--ring", "10", "--ring", "10"), ("--unknown", "1"))
            for options in invalid:
                run("equipment-set", str(source), str(output), "--character", "0", *options, valid=False)
                assert not output.exists()
            run("equipment-set", str(source), str(source), "--character", "0", "--none", "true", valid=False)
            assert source.read_bytes() == original
    print("Roster equipment CLI: independent fixtures, base/DLC transfers, ring conservation, isolated sections and overwrite protection passed.")
