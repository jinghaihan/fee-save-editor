"""Verify Emblem additions with an independent decoder and temporary save copies."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from itertools import groupby
from pathlib import Path

from test_emblems_cli import bond_records, sections
from test_roster_cli import game_hash
from test_roster_equipment_cli import fixture


def rebuild(original: bytes, pools: dict[bytes, bytes]) -> bytes:
    parts = [tag + struct.pack("<I", len(payload) + 4) + payload for tag, payload in pools.items()]
    offsets, position = [], 260
    for part in parts:
        offsets.append(position)
        position += len(part)
    offsets.append(position)
    body = original[:132] + struct.pack("<32I", *offsets, *([0] * (32 - len(offsets)))) + b"".join(parts) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def god_records(data: bytes) -> list[dict]:
    blob = sections(data)[b" DOG"]
    assert struct.unpack_from("<II", blob)[0] == 8
    count = struct.unpack_from("<I", blob, 32)[0]
    position, result = 36, []

    def string() -> str | None:
        nonlocal position
        length = struct.unpack_from("<I", blob, position)[0]
        position += 4
        if length == 0xffffffff:
            return None
        text = blob[position:position + length].decode("utf-16-le")
        position += length
        return text

    for _ in range(count):
        start = position
        instance, marker, god_hash, holder, dark, reserved, escaping, dirty, synchro = struct.unpack_from("<IHII5B", blob, position)
        assert marker == 0xefcd
        position += 19
        for _ in range(synchro):
            assert string() is not None
            position += 2
        weapons = {}
        weapon_count = blob[position]
        position += 1
        for _ in range(weapon_count):
            iid = string()
            assert iid and iid not in weapons
            levels = blob[position:position + 10]
            position += 10
            weapons[iid] = (levels, string())
        result.append(dict(instance=instance, hash=god_hash, holder=holder, flags=(dark, reserved, escaping, dirty),
                           synchro=synchro, weapons=weapons, raw=blob[start:position]))
    assert position == len(blob)
    return result


def without_tiki(data: bytes, remove_holder: bool = False) -> bytes:
    pools = sections(data)
    gods = [row for row in god_records(data) if row["hash"] != game_hash("GID_チキ")]
    pools[b" DOG"] = pools[b" DOG"][:32] + struct.pack("<I", len(gods)) + b"".join(row["raw"] for row in gods)
    if remove_holder:
        blob = pools[b"DBDG"]
        groups = [(gid, list(rows)) for gid, rows in groupby(bond_records(data), key=lambda row: row["gid"])]
        assert len(groups) == struct.unpack_from("<I", blob, 32)[0], "This fixture must have nonempty holders."
        cursor, kept = 36, []
        for gid, rows in groups:
            end = rows[-1]["flags_offset"] + 1
            if gid != "GID_チキ":
                kept.append(blob[cursor:end])
            cursor = end
        assert cursor == len(blob) and len(kept) == len(groups) - 1
        pools[b"DBDG"] = blob[:32] + struct.pack("<I", len(kept)) + b"".join(kept)
    return rebuild(data, pools)


def check_emblem_creation(command: list[str], real_directory: Path | None, base: bytes) -> None:
    catalog = json.loads((Path(__file__).resolve().parents[1] / "core/Data/emblem-creation.json").read_text(encoding="utf-8"))
    expected_weapons = {row["Id"]: row["Weapons"] for row in catalog["Emblems"]}
    original = fixture(base)
    cases = [("synthetic-reuse", without_tiki(original), False, False),
             ("synthetic-fresh", without_tiki(original, True), True, False)]
    if real_directory:
        known_weapons = {game_hash(gid): weapons for gid, weapons in expected_weapons.items()}
        for name in ("Manual0", "Auto"):
            data = (real_directory / name).read_bytes()
            for row in god_records(data):
                if row["hash"] in known_weapons:
                    assert set(row["weapons"]) == set(known_weapons[row["hash"]]), (name, row["hash"])
            cases.extend(((f"{name}-reuse", data, False, True), (f"{name}-fresh", data, True, True)))
    with tempfile.TemporaryDirectory(prefix="fee-emblem-add-cli-") as directory:
        root = Path(directory)

        def run(*args: str, valid: bool = True) -> str:
            result = subprocess.run([*command, "emblems", *args], capture_output=True, text=True, encoding="utf-8")
            assert (result.returncode == 0) == valid, (args, result.stderr)
            return result.stdout

        for name, source_data, fresh, real in cases:
            source, output, repeated = (root / f"{name}-{suffix}" for suffix in ("source", "output", "repeat"))
            if real:
                pristine = root / f"{name}-pristine"
                pristine.write_bytes(source_data)
                inspection = subprocess.run([*command, "roster", "equipment", str(pristine), "--character", "0", "--json"],
                                            capture_output=True, text=True, encoding="utf-8")
                assert inspection.returncode == 0, inspection.stderr
                tiki = next(row for row in json.loads(inspection.stdout)["Options"] if row["EmblemId"] == "GID_チキ")
                if tiki["OwnerIndex"] is not None:
                    unequipped = root / f"{name}-unequipped"
                    result = subprocess.run([*command, "roster", "equipment-set", str(pristine), str(unequipped),
                                             "--character", str(tiki["OwnerIndex"]), "--none", "true"], capture_output=True, text=True, encoding="utf-8")
                    assert result.returncode == 0, result.stderr
                    source_data = unequipped.read_bytes()
                source_data = without_tiki(source_data, remove_holder=fresh)
            source.write_bytes(source_data)
            missing = json.loads(run("missing", str(source), "--json"))
            assert any(row["Id"] == "GID_チキ" and row["Name"] == "Tiki" for row in missing)
            chinese = json.loads(run("missing", str(source), "--language", "zh-Hans"))
            assert any(row["Id"] == "GID_チキ" and row["Name"] == "琪姬" for row in chinese)
            run("add", str(source), str(output), "--emblem", "GID_チキ")
            edited = output.read_bytes()
            new_god = next(row for row in god_records(edited) if row["hash"] == game_hash("GID_チキ"))
            assert new_god["flags"] == (0, 0, 0, 0) and new_god["synchro"] == 0
            assert list(new_god["weapons"]) == expected_weapons["GID_チキ"]
            assert all(levels == bytes((0, *([1] * 9))) and skill is None for levels, skill in new_god["weapons"].values())
            bonds = [row for row in bond_records(edited) if row["gid"] == "GID_チキ"]
            assert bonds and all(row["instance"] == new_god["holder"] for row in bonds)
            if fresh:
                assert len(bonds) == (41 if real else 2)
                assert all(row["level"] == 1 and row["exp"] == 0 and row["flags"] == 0 and not row["skills"] for row in bonds)
            for tag, payload in sections(source_data).items():
                if tag != b" DOG" and not (fresh and tag == b"DBDG"):
                    assert sections(edited)[tag] == payload, (name, tag)
            old_gods = {row["instance"]: row["raw"] for row in god_records(source_data)}
            assert all(row["raw"] == old_gods[row["instance"]] for row in god_records(edited) if row["instance"] in old_gods)
            assert all(row["Id"] != "GID_チキ" for row in json.loads(run("missing", str(output))))
            run("add", str(output), str(repeated), "--emblem", "GID_チキ")
            assert repeated.read_bytes() == edited
            for invalid in ("GID_unknown", "GID_リュール", "GID_マルス_敵"):
                rejected = root / "rejected"
                run("add", str(source), str(rejected), "--emblem", invalid, valid=False)
                assert not rejected.exists()
            assert source.read_bytes() == source_data
        source = root / "all-source"
        source.write_bytes(original)
        for index, (gid, weapons) in enumerate(expected_weapons.items()):
            target = root / f"all-{index}"
            run("add", str(source), str(target), "--emblem", gid)
            if gid not in ("GID_マルス", "GID_チキ"):
                row = next(row for row in god_records(target.read_bytes()) if row["hash"] == game_hash(gid))
                assert list(row["weapons"]) == weapons, gid
            source = target
        assert json.loads(run("missing", str(source))) == []
    print("Emblem creation CLI: all 19 normal/DLC rings, weapon initialization, fresh/reused bonds, no duplicates and byte preservation passed.")
