"""Independent engraving decoding, transfers and exact-restoration checks."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_cli import inventory_entries, inventory_fixture
from test_emblems_cli import sections
from test_roster_cli import fixture as roster_fixture, game_hash, records

MARTH = "GID_マルス"
WEAPON = "IID_フェンサリル"


def fixture(base: bytes) -> bytes:
    source = bytes(roster_fixture(base))
    parts = sections(source)
    parts[b"NART"] = sections(bytes(inventory_fixture()))[b"NART"]
    chunks = [tag + struct.pack("<I", len(payload) + 4) + payload for tag, payload in parts.items()]
    offsets, offset = [], 260
    for chunk in chunks:
        offsets.append(offset)
        offset += len(chunk)
    offsets.append(offset)
    body = source[:132] + struct.pack("<32I", *offsets, *([0] * (32 - len(offsets)))) + b"".join(chunks) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def carried_items(data: bytes) -> list[list[dict | None]]:
    result = []
    for start in records(data):
        position = start + 121 + 6 * data[start + 120]
        _, version, count = struct.unpack_from("<IIB", data, position)
        assert version == 2 and count == 8
        position += 9
        items = []
        for _ in range(count):
            version, present = struct.unpack_from("<IB", data, position)
            assert version == 5 and present in (0, 1)
            position += 5
            item = None
            if present:
                marker, item_hash, uses, refine, flags, engraving_marker = struct.unpack_from("<HIBBIH", data, position)
                assert marker == 0xefcd and engraving_marker in (0xefcd, 0xccdb)
                position += 14
                engraving = None
                if engraving_marker == 0xefcd:
                    engraving = struct.unpack_from("<I", data, position)[0]
                    position += 4
                item = dict(ItemHash=item_hash, Uses=uses, RefineLevel=refine, Flags=flags, EngravingHash=engraving)
            items.append(item)
        result.append(items)
    return result


def check_engravings(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, ok: bool = True) -> bytes:
        result = subprocess.run([*command, *args], capture_output=True)
        assert (result.returncode == 0) == ok, (args, result.stderr.decode())
        return result.stdout

    catalog = json.loads(run("items", "engravings", "--json"))
    assert len(catalog) == 20 and catalog[0]["English"] == "Marth" and catalog[0]["Chinese"] != "Marth"
    assert [catalog[0][key] for key in ("Power", "Weight", "Hit", "Critical", "Avoid", "Secure")] == [1, 0, 10, 10, 5, 5]
    original = fixture(base)
    with tempfile.TemporaryDirectory(prefix="fee-engraving-cli-") as directory:
        folder = Path(directory)
        source = folder / "source"
        source.write_bytes(original)
        sequence = 0

        def edit(group: str, verb: str, path: Path, *options: str, ok: bool = True) -> Path:
            nonlocal sequence
            sequence += 1
            destination = folder / str(sequence)
            run(group, verb, str(path), str(destination), *options, ok=ok)
            if ok:
                changed = sections(destination.read_bytes())
                for tag, payload in sections(path.read_bytes()).items():
                    assert tag in (b"NART", b"TINU") or changed[tag] == payload
            else:
                assert not destination.exists()
            return destination

        for engraving in catalog:
            changed = edit("items", "engrave", source, "--slot", "1", "--engraving", engraving["Id"])
            first, second = inventory_entries(original)[1], inventory_entries(changed.read_bytes())[1]
            assert {key: value for key, value in second.items() if key not in ("UsesOffset", "EngravingHash")} == {
                key: value for key, value in first.items() if key not in ("UsesOffset", "EngravingHash")}
            assert second["EngravingHash"] == game_hash(engraving["Id"])
            assert carried_items(changed.read_bytes()) == carried_items(original)
        first = edit("items", "engrave", source, "--slot", "1", "--engraving", MARTH)
        second = edit("roster", "item-set", first, "--character", "0", "--slot", "1", "--item", WEAPON, "--engraving", MARTH)
        assert inventory_entries(second.read_bytes())[1]["EngravingHash"] is None
        assert carried_items(second.read_bytes())[0][1] == dict(ItemHash=game_hash(WEAPON), Uses=255, RefineLevel=0,
                                                             Flags=0, EngravingHash=game_hash(MARTH))
        third = edit("roster", "item-set", second, "--character", "1", "--slot", "1", "--item", WEAPON)
        fourth = edit("roster", "item-engrave", third, "--character", "1", "--slot", "1", "--engraving", MARTH)
        assert carried_items(fourth.read_bytes())[0][1]["EngravingHash"] is None
        fifth = edit("items", "add", fourth, "--item", WEAPON, "--refine", "5", "--engraving", MARTH)
        assert carried_items(fifth.read_bytes())[1][1]["EngravingHash"] is None
        assert inventory_entries(fifth.read_bytes())[-1]["EngravingHash"] == game_hash(MARTH)
        cleared = edit("items", "set", fifth, "--slot", "3", "--engraving", "none")
        assert inventory_entries(cleared.read_bytes())[-1]["EngravingHash"] is None
        staff = edit("items", "set", first, "--slot", "1", "--item", "IID_リカバー", "--engraving", "none")
        assert inventory_entries(staff.read_bytes())[1]["Flags"] == 0x55
        for slot, engraving in (("0", MARTH), ("2", MARTH), ("3", MARTH), ("-1", MARTH), ("1", "GID_unknown")):
            edit("items", "engrave", source, "--slot", slot, "--engraving", engraving, ok=False)
        edit("items", "engrave", source, "--slot", "1", ok=False)
        edit("items", "engrave", source, "--slot", "1", "--engraving", MARTH, "--engraving", MARTH, ok=False)
        edit("items", "set", first, "--slot", "1", "--item", "IID_リカバー", ok=False)
        edit("roster", "item-engrave", source, "--character", "0", "--slot", "0", "--engraving", MARTH, ok=False)
        edit("roster", "item-engrave", source, "--character", "0", "--slot", "1", "--engraving", MARTH, ok=False)
        assert source.read_bytes() == original
        if real_directory:
            for name in ("Auto", "Manual0"):
                path = real_directory / name
                before = path.read_bytes()
                rows = json.loads(run("roster", "list", str(path), "--json"))
                independent = carried_items(before)
                checked = 0
                for index, character in enumerate(rows):
                    for slot in character["Items"]:
                        engraving = slot["EngravingId"]
                        if engraving is None:
                            continue
                        assert slot["Item"]["EngravingHash"] == independent[index][slot["Slot"]]["EngravingHash"]
                        erased = edit("roster", "item-engrave", path, "--character", str(index), "--slot", str(slot["Slot"]), "--engraving", "none")
                        restored = edit("roster", "item-engrave", erased, "--character", str(index), "--slot", str(slot["Slot"]), "--engraving", engraving)
                        assert restored.read_bytes() == before
                        checked += 1
                assert checked > 0 and path.read_bytes() == before
                print(f"{name}: {checked} real engraved weapons independently decoded and restored exactly.")
    print("Engraving CLI: all 20 choices, transfers, clearing, invalid inputs and source preservation passed.")
