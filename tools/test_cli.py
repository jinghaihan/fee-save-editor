#!/usr/bin/env python3
"""Test the CLI without distributing private save files."""

from __future__ import annotations

import argparse
import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def cli_command(cli: Path) -> list[str]:
    command = [str(cli.resolve())]
    return ["dotnet", *command] if cli.suffix == ".dll" else command


def fixture(global_save: bool = False) -> bytes:
    header = b"" if global_save else struct.pack("<II", 9, 0x130) + bytes(120)
    start = len(header) + 132
    payload = b"synthetic-payload\x00\xff"
    section = (b"LGSU" if global_save else b"RESU") + struct.pack("<I", len(payload) + 4) + payload
    index = b"EDNI" + struct.pack("<32I", start, start + len(section), *([0] * 30))
    body = header + index + section + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def with_checksum(body: bytes) -> bytes:
    return body[:-4] + struct.pack("<I", zlib.crc32(body[:-4]))


def wide(value: str) -> bytes:
    encoded = value.encode("utf-16-le")
    return struct.pack("<I", len(encoded)) + encoded


def main_fixture() -> bytes:
    keys = ("G_所持_IID_てつの晶石", "G_所持_IID_はがねの晶石", "G_所持_IID_ぎんの晶石")
    records = b"".join(wide(key) + b"\x00" + struct.pack("<i", value) for key, value in zip(keys, (12, 8, 3)))
    records += wide("unrelated") + b"\x01" + wide("preserve this text")
    variables = struct.pack("<I", 0) + bytes(28) + struct.pack("<I", 4) + records
    variables = struct.pack("<I", len(variables) + 4) + variables
    user = struct.pack("<I", 20) + bytes(28) + struct.pack("<I5BII", 3, 1, 1, 1, 2, 2, 0x12345678, 7)
    user += variables + struct.pack("<5I", 5000, 1, 2, 3, 4) + wide("M001")
    user += struct.pack("<ii", 1200, 999) + wide("Sommie") + b"unknown-tail!"
    section = b"RESU" + struct.pack("<I", len(user) + 4) + user
    opaque = b"PMAP" + struct.pack("<I", 12) + bytes(range(8))
    header = bytearray(128)
    struct.pack_into("<II", header, 0, 9, 0x130)
    header[12] = header[17] = 2
    header[23] = header[25] = 1
    struct.pack_into("<I", header, 60, zlib.crc32(header[:60]))
    index = b"EDNI" + struct.pack("<32I", 260, 260 + len(section), 260 + len(section) + len(opaque), *([0] * 29))
    body = header + index + section + opaque + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def main_offsets(data: bytes) -> dict[str, int]:
    """Locate editable scalar fields independently of the C# implementation."""
    user = struct.unpack_from("<I", data, 132)[0]
    position = user + 57
    end = position + struct.unpack_from("<I", data, position)[0]
    position += 36
    count = struct.unpack_from("<I", data, position)[0]
    position += 4
    result = {"Mode": user + 45, "Difficulty": user + 46}
    material = {"G_所持_IID_てつの晶石": "IronIngots", "G_所持_IID_はがねの晶石": "SteelIngots",
                "G_所持_IID_ぎんの晶石": "SilverIngots"}
    for _ in range(count):
        length = struct.unpack_from("<I", data, position)[0]
        position += 4
        key = data[position:position + length].decode("utf-16-le")
        position += length
        kind = data[position]
        position += 1
        if key in material:
            result[material[key]] = position
        if kind == 0:
            position += 4
        elif kind == 1:
            length = struct.unpack_from("<I", data, position)[0]
            position += 4 + length
        else:
            raise AssertionError("Unexpected variable kind in fixture")
    assert position == end
    result["Money"] = position
    position += 20
    length = struct.unpack_from("<I", data, position)[0]
    position += 4 + length
    result["BondFragments"] = position
    result["SommieName"] = position + 8
    return result


def check_main(command: list[str], real_directory: Path | None) -> None:
    cases = [("synthetic", main_fixture())]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Auto", "Manual0")]
    with tempfile.TemporaryDirectory(prefix="fee-main-test-") as directory:
        root = Path(directory)
        source, output, restored = (root / name for name in ("source", "edited", "restored"))
        for name, original in cases:
            source.write_bytes(original)
            current = json.loads(subprocess.check_output([*command, "main", "show", str(source), "--json"], text=True))
            offsets = main_offsets(original)
            for key in ("Money", "BondFragments", "IronIngots", "SteelIngots", "SilverIngots"):
                assert current[key] == struct.unpack_from("<i", original, offsets[key])[0]
            target_name = "S" * len(current["SommieName"])
            options = ["--money", "123", "--bond-fragments", "456", "--iron", "7", "--steel", "9", "--silver", "11",
                       "--difficulty", "normal", "--mode", "casual", "--sommie-name", target_name]
            subprocess.run([*command, "main", "set", str(source), str(output), *options], check=True, capture_output=True)
            edited = output.read_bytes()
            assert len(edited) == len(original)
            assert zlib.crc32(edited[:-4]) == struct.unpack_from("<I", edited, len(edited) - 4)[0]
            allowed = {23, 25, offsets["Mode"], offsets["Difficulty"]}
            for offset in offsets.values():
                allowed.update(range(offset, offset + 4))
            allowed.update(range(offsets["SommieName"], offsets["SommieName"] + 4 + len(target_name.encode("utf-16-le"))))
            checksums = [offset for offset in range(26, 125) if zlib.crc32(original[:offset]) == struct.unpack_from("<I", original, offset)[0]]
            assert len(checksums) == 1
            allowed.update(range(checksums[0], checksums[0] + 4))
            allowed.update(range(len(original) - 4, len(original)))
            changed = {index for index, (before, after) in enumerate(zip(original, edited)) if before != after}
            assert changed <= allowed, (name, changed - allowed)
            reverted_options = []
            for option, key in (("money", "Money"), ("bond-fragments", "BondFragments"), ("iron", "IronIngots"),
                                ("steel", "SteelIngots"), ("silver", "SilverIngots"), ("difficulty", "Difficulty"),
                                ("mode", "GameMode"), ("sommie-name", "SommieName")):
                reverted_options += [f"--{option}", str(current[key])]
            subprocess.run([*command, "main", "set", str(output), str(restored), *reverted_options], check=True, capture_output=True)
            assert restored.read_bytes() == original
            assert source.read_bytes() == original
            output.unlink()
            restored.unlink()
            # Rebuild with a different-length Unicode name and verify every downstream section.
            subprocess.run([*command, "main", "set", str(source), str(output), "--sommie-name", "索拉 round-trip 🐾"],
                           check=True, capture_output=True)
            inspected = json.loads(subprocess.check_output([*command, "inspect", str(output), "--json"], text=True))
            original_sections = json.loads(subprocess.check_output([*command, "inspect", str(source), "--json"], text=True))["Sections"]
            resized = output.read_bytes()
            for before, after in zip(original_sections[1:], inspected["Sections"][1:]):
                assert original[before["Offset"]:before["Offset"] + before["Length"] + 8] == resized[after["Offset"]:after["Offset"] + after["Length"] + 8]
            subprocess.run([*command, "main", "set", str(output), str(restored), "--sommie-name", current["SommieName"]],
                           check=True, capture_output=True)
            assert restored.read_bytes() == original
            output.unlink()
            restored.unlink()
            print(f"{name}: Main edits, independent byte-diff check and Unicode resizing passed.")

        source.write_bytes(main_fixture())
        limits = (("money", "Money", 9_999_999), ("bond-fragments", "BondFragments", 9_999_999),
                  ("iron", "IronIngots", 9_999), ("steel", "SteelIngots", 9_999), ("silver", "SilverIngots", 9_999))
        for option, key, maximum in limits:
            for valid in (0, maximum):
                subprocess.run([*command, "main", "set", str(source), str(output), f"--{option}", str(valid)],
                               check=True, capture_output=True)
                actual = json.loads(subprocess.check_output([*command, "main", "show", str(output), "--json"], text=True))
                assert actual[key] == valid
                output.unlink()
            for invalid in (-1, maximum + 1, 2_147_483_647):
                failure = subprocess.run([*command, "main", "set", str(source), str(output), f"--{option}", str(invalid)],
                                         capture_output=True, text=True)
                assert failure.returncode == 1 and "Unhandled exception" not in failure.stderr, failure
                assert not output.exists()
                assert source.read_bytes() == main_fixture()
        above_limit = bytearray(main_fixture())
        struct.pack_into("<i", above_limit, main_offsets(above_limit)["IronIngots"], 10_000)
        source.write_bytes(with_checksum(above_limit))
        assert json.loads(subprocess.check_output([*command, "main", "show", str(source), "--json"], text=True))["IronIngots"] == 10_000
        subprocess.run([*command, "copy", str(source), str(output)], check=True, capture_output=True)
        assert output.read_bytes() == source.read_bytes()
        output.unlink()
        assert subprocess.run([*command, "main", "set", str(source), str(output), "--money", "10"], capture_output=True).returncode == 1
        assert not output.exists()
        source.write_bytes(main_fixture())
        for options in ([], ["--money"], ["--money", "-1"], ["--money", "1.5"], ["--money", "2147483648"],
                        ["--money", "1", "--money", "2"], ["--unknown", "1"], ["--difficulty", "3"],
                        ["--mode", "invalid"], ["--sommie-name", ""], ["--sommie-name", "bad\nname"]):
            failure = subprocess.run([*command, "main", "set", str(source), str(output), *options], capture_output=True, text=True)
            assert failure.returncode == 1 and "Unhandled exception" not in failure.stderr, failure
            assert not output.exists()
        for unsupported in (fixture(), fixture(True)):
            source.write_bytes(unsupported)
            assert subprocess.run([*command, "main", "show", str(source)], capture_output=True).returncode == 1
            assert subprocess.run([*command, "main", "set", str(source), str(output), "--money", "10"], capture_output=True).returncode == 1
            assert not output.exists()
    if real_directory:
        for name, original in cases[1:]:
            assert (real_directory / name).read_bytes() == original


def check_saves(command: list[str]) -> None:
    with tempfile.TemporaryDirectory(prefix="fee-cli-test-") as directory:
        root = Path(directory)
        source = root / "Manual0"
        output = root / "copy"
        for global_save in (False, True):
            original = fixture(global_save)
            source.write_bytes(original)
            inspected = json.loads(subprocess.check_output([*command, "inspect", str(source), "--json"], text=True))
            assert inspected["Kind"] == ("Global" if global_save else "Game")
            assert inspected["Length"] == len(original)
            assert inspected["Sections"][0]["Name"] == ("USGL" if global_save else "USER")
            subprocess.run([*command, "copy", str(source), str(output)], check=True, capture_output=True)
            assert output.read_bytes() == original
            assert source.read_bytes() == original
            assert subprocess.run([*command, "copy", str(source), str(output)], capture_output=True).returncode != 0
            assert subprocess.run([*command, "copy", str(source), str(source)], capture_output=True).returncode != 0
            output.unlink()

        valid = fixture()
        bad_crc = bytearray(valid)
        bad_crc[-1] ^= 1
        overlap = bytearray(valid)
        struct.pack_into("<I", overlap, 128 + 8, 261)
        oversized = bytearray(valid)
        struct.pack_into("<I", oversized, 264, 0xffffffff)
        missing_index = bytearray(valid)
        missing_index[128:132] = b"FAIL"
        gap = bytearray(valid)
        struct.pack_into("<I", gap, 128 + 4 + 3 * 4, 260)
        for invalid in (b"", valid[:-1], bytes(bad_crc), with_checksum(overlap),
                        with_checksum(oversized), with_checksum(missing_index), with_checksum(gap)):
            source.write_bytes(invalid)
            failure = subprocess.run([*command, "inspect", str(source)], capture_output=True, text=True)
            assert failure.returncode == 1, failure
            assert "Unhandled exception" not in failure.stderr
            assert subprocess.run([*command, "copy", str(source), str(output)], capture_output=True).returncode == 1
            assert not output.exists()


def inventory_fixture(capacity: int = 4) -> bytes:
    original = main_fixture()
    offsets = struct.unpack_from("<3I", original, 132)
    user, opaque = original[offsets[0]:offsets[1]], original[offsets[1]:offsets[2]]

    def item(item_hash: int, uses: int, refine: int, flags: int, engraving: int | None = None) -> bytes:
        reference = struct.pack("<H", 0xccdb) if engraving is None else struct.pack("<HI", 0xefcd, engraving)
        return struct.pack("<IIBHI", 1, 5, 1, 0xefcd, item_hash) + struct.pack("<BBI", uses, refine, flags) + reference

    empty = struct.pack("<IIB", 1, 5, 0)
    entries = [item(0x4e134981, 4, 0, 0xdeadbeef), item(0xe9d2a0e9, 255, 3, 0x55, 0x123abc),
               item(0x12345678, 17, 8, 0xffffffff), empty]
    entries = (entries + [empty] * capacity)[:capacity]
    payload = struct.pack("<I", 1) + bytes(range(1, 29)) + struct.pack("<H", capacity) + b"".join(entries)
    transport = b"NART" + struct.pack("<I", len(payload) + 4) + payload
    index = struct.pack("<32I", 260, 260 + len(user), 260 + len(user) + len(transport),
                        260 + len(user) + len(transport) + len(opaque), *([0] * 28))
    body = original[:132] + index + user + transport + opaque + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def inventory_entries(data: bytes) -> list[dict]:
    """Decode the convoy independently to check CLI output and byte preservation."""
    offsets = [offset for offset in struct.unpack_from("<32I", data, 132) if offset]
    transport = next(offset for offset in offsets if data[offset:offset + 4] == b"NART")
    end = transport + 4 + struct.unpack_from("<I", data, transport + 4)[0]
    count = struct.unpack_from("<H", data, transport + 40)[0]
    position = transport + 42
    entries = []
    for slot in range(count):
        entry_version, item_version, present = struct.unpack_from("<IIB", data, position)
        assert (entry_version, item_version) == (1, 5) and present in (0, 1)
        position += 9
        if not present:
            continue
        marker, item_hash = struct.unpack_from("<HI", data, position)
        assert marker == 0xefcd
        uses_offset = position + 6
        uses, refine, flags, marker = struct.unpack_from("<BBIH", data, uses_offset)
        position += 14
        engraving = None
        if marker == 0xefcd:
            engraving = struct.unpack_from("<I", data, position)[0]
            position += 4
        else:
            assert marker == 0xccdb
        entries.append(dict(Slot=slot, ItemHash=item_hash, Uses=uses, RefineLevel=refine, Flags=flags,
                            EngravingHash=engraving, UsesOffset=uses_offset))
    assert position == end
    return entries


def check_inventory(command: list[str], real_directory: Path | None) -> None:
    catalog = json.loads(subprocess.check_output([*command, "items", "catalog", "--json"], text=True))
    recover = next(item for item in catalog if item["Id"] == "IID_リカバー")
    assert recover["MaxUses"] == 10 and recover["MaxRefine"] == 0
    cases = [("synthetic", inventory_fixture())]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Auto", "Manual0")]
    with tempfile.TemporaryDirectory(prefix="fee-items-test-") as directory:
        root = Path(directory)
        source, output, restored = (root / name for name in ("source", "edited", "restored"))
        for name, original in cases:
            source.write_bytes(original)
            result = json.loads(subprocess.check_output([*command, "items", "list", str(source), "--json"], text=True))
            entries = inventory_entries(original)
            assert result["Occupied"] == len(entries)
            for actual, decoded in zip(result["Items"], entries):
                assert all(actual[key] == value for key, value in decoded.items() if key != "UsesOffset")
            staff = next(item for item in result["Items"] if item["Id"] == "IID_リカバー")
            subprocess.run([*command, "items", "set", str(source), str(output), "--slot", str(staff["Slot"]),
                            "--uses", "1"], check=True, capture_output=True)
            edited = output.read_bytes()
            offset = next(entry["UsesOffset"] for entry in entries if entry["Slot"] == staff["Slot"])
            allowed = {offset, *range(len(original) - 4, len(original))}
            assert {i for i, pair in enumerate(zip(original, edited)) if pair[0] != pair[1]} <= allowed
            assert len(edited) == len(original) and zlib.crc32(edited[:-4]) == struct.unpack_from("<I", edited, len(edited) - 4)[0]
            subprocess.run([*command, "items", "set", str(output), str(restored), "--slot", str(staff["Slot"]),
                            "--uses", str(staff["Uses"])], check=True, capture_output=True)
            assert restored.read_bytes() == original
            output.unlink()
            restored.unlink()
            subprocess.run([*command, "items", "add", str(source), str(output), "--item", "IID_リカバー"],
                           check=True, capture_output=True)
            original_slots = {entry["Slot"] for entry in entries}
            new_slot = next(entry["Slot"] for entry in inventory_entries(output.read_bytes()) if entry["Slot"] not in original_slots)
            subprocess.run([*command, "items", "delete", str(output), str(restored), "--slot", str(new_slot)],
                           check=True, capture_output=True)
            assert restored.read_bytes() == original and source.read_bytes() == original
            output.unlink()
            restored.unlink()
            print(f"{name}: convoy independent decoding, byte-diff and add/delete reversal passed.")

        original = inventory_fixture()
        source.write_bytes(original)
        subprocess.run([*command, "items", "restore", str(source), str(output), "--all"], check=True, capture_output=True)
        restored_entries = inventory_entries(output.read_bytes())
        expected = inventory_entries(original)
        expected[0]["Uses"] = 10
        assert restored_entries == expected
        output.unlink()
        for options in (["--slot", "0", "--uses", "0"], ["--slot", "0", "--uses", "11"],
                        ["--slot", "0", "--refine", "1"], ["--slot", "1", "--refine", "6"],
                        ["--slot", "1", "--uses", "254"], ["--slot", "4", "--uses", "1"],
                        ["--slot", "-1"], ["--slot", "0", "--uses", "1.5"],
                        ["--slot", "0", "--item", "IID_missing"], ["--slot", "1", "--item", "IID_リカバー"],
                        ["--slot", "0", "--slot", "1"], ["--slot", "0", "--unknown", "1"], [], ["--slot"]):
            failure = subprocess.run([*command, "items", "set", str(source), str(output), *options], capture_output=True, text=True)
            assert failure.returncode == 1 and "Unhandled exception" not in failure.stderr, failure
            assert not output.exists() and source.read_bytes() == original
        for slot in ("2", "3", "4", "-1"):
            failure = subprocess.run([*command, "items", "restore", str(source), str(output), "--slot", slot], capture_output=True)
            assert failure.returncode == 1 and not output.exists()
        source.write_bytes(inventory_fixture(capacity=1))
        assert subprocess.run([*command, "items", "add", str(source), str(output), "--item", "IID_リカバー"],
                              capture_output=True).returncode == 1
        assert not output.exists()
    if real_directory:
        for name, original in cases[1:]:
            assert (real_directory / name).read_bytes() == original


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", required=True, type=Path)
    parser.add_argument("--save-directory", type=Path, help="Optional local game saves, never modified")
    args = parser.parse_args()
    command = cli_command(args.cli)
    version = (Path(__file__).resolve().parents[1] / "VERSION").read_text(encoding="utf-8").strip()
    actual = subprocess.check_output([*command, "--version"], text=True).strip()
    assert actual == version, (actual, version)
    subprocess.run([*command, "--help"], check=True, capture_output=True)
    assert subprocess.run([*command, "unknown"], capture_output=True).returncode != 0
    check_saves(command)
    check_main(command, args.save_directory)
    from test_donations_cli import check_donations
    check_donations(command, args.save_directory, main_fixture())
    from test_achievements_cli import check_achievements
    check_achievements(command, args.save_directory, main_fixture())
    check_inventory(command, args.save_directory)
    from test_roster_cli import check_roster
    check_roster(command, args.save_directory, main_fixture())
    from test_emblems_cli import check_emblems
    check_emblems(command, args.save_directory, main_fixture())
    from test_supports_cli import check_supports
    check_supports(command, args.save_directory, main_fixture())
    from test_engravings_cli import check_engravings
    check_engravings(command, args.save_directory, main_fixture())
    print("CLI tests passed.")


if __name__ == "__main__":
    main()
