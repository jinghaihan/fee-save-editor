"""Independent bond/ring fixtures, range checks and byte-preservation tests."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_roster_cli import game_hash


def wide(value: str) -> bytes:
    data = value.encode("utf-16-le")
    return struct.pack("<I", len(data)) + data


def sections(data: bytes) -> dict[bytes, bytes]:
    assert zlib.crc32(data[:-4]) == struct.unpack_from("<I", data, len(data) - 4)[0]
    offsets = [value for value in struct.unpack_from("<32I", data, 132) if value]
    return {data[start:start + 4]: data[start + 8:end] for start, end in zip(offsets, offsets[1:])}


def fixture(base: bytes, ring_rows: list[tuple[int, str, int]] | None = None) -> bytes:
    base = bytes(base)
    header = lambda version: struct.pack("<II", version, 0xcdcdcdcd) + bytes(24)
    bonds = header(2) + struct.pack("<I", 3)
    for instance, gid in ((1, "GID_マルス"), (2, "GID_チキ"), (3, "GID_リュール")):
        bonds += struct.pack("<I", instance) + wide(gid) + bytes((instance == 3,))
        if instance == 3:
            bonds += struct.pack("<I", 0) + wide("PID_ユナカ")
        bonds += struct.pack("<H", 2)
        for pid in ("PID_リュール", "PID_ユナカ"):
            pact = instance == 3 and pid == "PID_ユナカ"
            bonds += wide(pid) + struct.pack("<IBHIIII B", 3, 21 if pact else 1, 209 if pact else 0,
                2, 2, 0x12345678, 0x87654321, 46 if pact else 32)
    ring_rows = ring_rows if ring_rows is not None else [(10, "RNID_紋章_シーダ_C", 7), (11, "RNID_紋章_シーダ_S", 1)]
    rings = header(3) + struct.pack("<I", len(ring_rows))
    for instance, rnid, stock in ring_rows:
        rings += struct.pack("<IHI B", instance, 0xefcd, game_hash(rnid), stock)
    records = sections(base)
    records[b"DBDG"], records[b"GNIR"] = bonds, rings
    records[b"LERU"] = header(1) + struct.pack("<I", 1) + wide("PID_リュールPID_ユナカ") + struct.pack("<IBbb", 1, 1, 7, 2)
    parts = [tag + struct.pack("<I", len(payload) + 4) + payload for tag, payload in records.items()]
    offsets, position = [], 260
    for part in parts:
        offsets.append(position)
        position += len(part)
    offsets.append(position)
    body = base[:132] + struct.pack("<32I", *offsets, *([0] * (32 - len(offsets)))) + b"".join(parts) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def bond_records(data: bytes) -> list[dict]:
    blob = sections(data)[b"DBDG"]
    position = 32

    def number(fmt: str) -> int:
        nonlocal position
        value = struct.unpack_from(fmt, blob, position)[0]
        position += struct.calcsize(fmt)
        return value

    def string() -> str | None:
        nonlocal position
        length = number("<I")
        if length == 0xffffffff:
            return None
        value = blob[position:position + length].decode("utf-16-le")
        position += length
        return value

    result = []
    for _ in range(number("<I")):
        instance, gid, special = number("<I"), string(), number("<B")
        if special:
            assert number("<I") == 0
            string()
        for _ in range(number("<H")):
            pid = string()
            assert number("<I") == 3
            offset = position
            level, exp = number("<B"), number("<H")
            assert number("<I") == 2
            skills = [number("<I") for _ in range(number("<I"))]
            flags_offset, flags = position, number("<B")
            result.append(dict(instance=instance, gid=gid, pid=pid, offset=offset, level=level,
                exp=exp, skills=skills, flags_offset=flags_offset, flags=flags))
    assert position == len(blob)
    return result


def ring_records(data: bytes) -> list[tuple[int, int, int]]:
    blob = sections(data)[b"GNIR"]
    count = struct.unpack_from("<I", blob, 32)[0]
    assert len(blob) == 36 + count * 11
    result = []
    for index in range(count):
        instance, marker, ring_hash, stock = struct.unpack_from("<IHIB", blob, 36 + index * 11)
        assert marker == 0xefcd and 1 <= instance <= 750
        result.append((instance, ring_hash, stock))
    assert len({ring[0] for ring in result}) == count
    return result


def check_emblems(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, valid: bool = True) -> bytes:
        completed = subprocess.run([*command, "emblems", *args], capture_output=True)
        assert (completed.returncode == 0) == valid, (args, completed.stderr.decode("utf-8", errors="replace"))
        return completed.stdout

    catalog = json.loads(run("catalog", "--json", "--language", "zh-Hans"))
    assert len(catalog["Emblems"]) == 20 and len(catalog["Rings"]) == 483
    assert catalog["Emblems"][0]["Name"] == "马尔斯"
    for language in ("en", "zh-Hans", "zh-Hant", "ja", "ko", "de", "fr", "es", "it"):
        translated = json.loads(run("catalog", "--json", "--language", language))
        assert len([ring for ring in translated["Rings"] if ring["Skills"]]) == 28
        olwen = next(ring for ring in translated["Rings"] if ring["Id"] == "RNID_トラキア_オルエン_S")
        assert {row["Stat"]: row["Value"] for row in olwen["StatBonuses"]} == {"Speed": 2, "Luck": 1, "Magic": 1}
        assert all(skill["Name"] and skill["Description"] and r"\n" not in skill["Description"]
                   for ring in translated["Rings"] for skill in ring["Skills"])
        if language == "en":
            assert olwen["Skills"][0]["Name"] == "Dire Thunder" and "Excludes Elthunder" in olwen["Skills"][0]["Description"]
    for options in (("--language", "xx"), ("--json", "--json"), ("--language",)):
        run("catalog", *options, valid=False)
    cases = [("synthetic", fixture(base))]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Manual0", "Auto")]
    with tempfile.TemporaryDirectory(prefix="fee-emblems-cli-") as directory:
        root = Path(directory)
        for name, original in cases:
            source = root / (name + "-source")
            source.write_bytes(original)
            data = json.loads(run("list", str(source), "--json"))
            rings = json.loads(run("rings", str(source), "--json"))
            assert len(data) == (3 if name == "synthetic" else 20)
            assert len(rings) == (2 if name == "synthetic" else 374)
            assert all(row["Id"] is not None for row in rings)
            filled = root / f"{name}-s-rings"
            run("rings-fill-s", str(source), str(filled))
            filled_bytes = filled.read_bytes()
            original_rings, full_rings = ring_records(original), ring_records(filled_bytes)
            s_hashes = {game_hash(row["Id"]) for row in catalog["Rings"] if row["Rank"] == "S"}
            assert len(s_hashes) == 123
            assert s_hashes <= {ring_hash for _, ring_hash, stock in full_rings if stock > 0}
            original_positive = [ring for ring in original_rings if ring[2] > 0]
            assert all(ring in full_rings for ring in original_positive)
            for tag, payload in sections(original).items():
                if tag not in (b"GNIR", b"RESU"):
                    assert sections(filled_bytes)[tag] == payload
            again = root / f"{name}-s-rings-again"
            run("rings-fill-s", str(filled), str(again))
            assert again.read_bytes() == filled_bytes
            run("rings-fill-s", str(source), str(source), valid=False)
            for options in (("--all",), ("--instance", "10"), ("--amount", "1")):
                invalid_fill = root / f"{name}-invalid-fill"
                run("rings-fill-s", str(source), str(invalid_fill), *options, valid=False)
                assert not invalid_fill.exists()
            before = bond_records(original)
            for instance in (1, 2):
                target = next(row for row in before if row["instance"] == instance and row["pid"] == "PID_リュール")
                output = root / f"{name}-{instance}-bond"
                run("bond-set", str(source), str(output), "--instance", str(instance), "--person", target["pid"], "--experience", "108")
                edited = output.read_bytes()
                after = bond_records(edited)
                current = next(row for row in after if row["instance"] == instance and row["pid"] == target["pid"])
                assert current["level"] == 10 and current["exp"] == 108 and current["skills"] == target["skills"]
                old_sections, new_sections = sections(original), sections(edited)
                for tag, payload in old_sections.items():
                    if tag != b"DBDG":
                        assert new_sections[tag] == payload, (name, tag)
                changed = {i for i, (old, new) in enumerate(zip(old_sections[b"DBDG"], new_sections[b"DBDG"])) if old != new}
                assert changed <= {target["offset"], target["offset"] + 1, target["offset"] + 2, target["flags_offset"]}
                assert original[:260] == edited[:260]
                maximum = root / f"{name}-{instance}-max"
                run("bond-max", str(output), str(maximum), "--instance", str(instance), "--person", target["pid"])
                last = next(row for row in bond_records(maximum.read_bytes()) if row["instance"] == instance and row["pid"] == target["pid"])
                assert last["level"] == 20 and last["exp"] == 208
                bulk = root / f"{name}-{instance}-bulk"
                run("bonds-max", str(source), str(bulk), "--instance", str(instance))
                bulk_bonds = bond_records(bulk.read_bytes())
                assert all(row["level"] == 20 and row["exp"] == 208 for row in bulk_bonds if row["instance"] == instance)
                assert [row for row in bulk_bonds if row["instance"] != instance] == [row for row in before if row["instance"] != instance]
                for options in (("--level", "10", "--experience", "109"), ("--level", "21"), ("--experience", "209"),
                                ("--experience", "-1"), ("--experience", "abc"), ("--level", "1", "--level", "2")):
                    invalid = root / "invalid-bond"
                    run("bond-set", str(source), str(invalid), "--instance", str(instance), "--person", target["pid"], *options, valid=False)
                    assert not invalid.exists()
            ring = next(row for row in rings if row["OwnerIndex"] is None)
            stock_path = root / (name + "-stock")
            run("ring-set", str(source), str(stock_path), "--instance", str(ring["InstanceId"]), "--amount", str(ring["MaximumStock"]))
            stocks = json.loads(run("rings", str(stock_path)))
            assert next(row for row in stocks if row["InstanceId"] == ring["InstanceId"])["StockCount"] == ring["MaximumStock"]
            for value in ("100", "-1", "1.5"):
                invalid = root / "invalid-stock"
                run("ring-set", str(source), str(invalid), "--instance", str(ring["InstanceId"]), "--amount", value, valid=False)
                assert not invalid.exists()
            run("ring-set", str(source), str(source), "--instance", str(ring["InstanceId"]), "--amount", "1", valid=False)
            assert source.read_bytes() == original
        stacked_source = root / "stacked-source"
        stacked_source.write_bytes(fixture(base, [(10, "RNID_紋章_シーダ_C", 40), (11, "RNID_紋章_シーダ_C", 50)]))
        assert json.loads(run("rings", str(stacked_source)))[0]["MaximumStock"] == 49
        invalid_stock = root / "invalid-combined-stock"
        run("ring-set", str(stacked_source), str(invalid_stock), "--instance", "10", "--amount", "50", valid=False)
        assert not invalid_stock.exists()
        combined_stock = root / "combined-stock"
        run("ring-set", str(stacked_source), str(combined_stock), "--instance", "10", "--amount", "49")
        assert [row[2] for row in ring_records(combined_stock.read_bytes())] == [49, 50]
        pact_source = root / "synthetic-source"
        for rank, target, required, cost in (("C", "B", 2, 100), ("B", "A", 3, 1000), ("A", "S", 4, 10000)):
            ring_source = root / f"meld-{rank}-source"
            ring_source.write_bytes(fixture(base, [(10, f"RNID_紋章_シーダ_{rank}", required + 1)]))
            # Prepare resources through the public CLI, not a dependent save parser.
            prepared = root / f"meld-{rank}-prepared"
            done = subprocess.run([*command, "main", "set", str(ring_source), str(prepared), "--bond-fragments", "20000"], capture_output=True)
            assert done.returncode == 0, done.stderr.decode()
            output = root / f"meld-{rank}-output"
            run("ring-meld", str(prepared), str(output), "--instance", "10")
            actual = ring_records(output.read_bytes())
            assert (10, game_hash(f"RNID_紋章_シーダ_{rank}"), 1) in actual
            assert (1, game_hash(f"RNID_紋章_シーダ_{target}"), 1) in actual
            summary = subprocess.run([*command, "main", "show", str(output), "--json"], capture_output=True)
            assert summary.returncode == 0, summary.stderr.decode()
            assert json.loads(summary.stdout)["BondFragments"] == 20000 - cost
            for tag, payload in sections(prepared.read_bytes()).items():
                if tag not in (b"GNIR", b"RESU"):
                    assert sections(output.read_bytes())[tag] == payload
            for options in (("--instance", "999"), ("--instance", "-1"), ("--instance", "10", "--amount", "1"),
                            ("--instance", "10", "--instance", "10"), ()):
                invalid = root / f"meld-{rank}-invalid"
                run("ring-meld", str(prepared), str(invalid), *options, valid=False)
                assert not invalid.exists()
            run("ring-meld", str(prepared), str(prepared), "--instance", "10", valid=False)
            assert ring_source.read_bytes() == fixture(base, [(10, f"RNID_紋章_シーダ_{rank}", required + 1)])
        no_material = root / "meld-no-material"
        no_material.write_bytes(fixture(base, [(10, "RNID_紋章_シーダ_C", 1)]))
        run("ring-meld", str(no_material), str(root / "meld-refused"), "--instance", "10", valid=False)
        assert not (root / "meld-refused").exists()
        pact_max = root / "pact-max"
        run("bond-max", str(pact_source), str(pact_max), "--instance", "3", "--person", "PID_ユナカ")
        pact = next(row for row in bond_records(pact_max.read_bytes()) if row["instance"] == 3 and row["pid"] == "PID_ユナカ")
        assert pact["level"] == 21 and pact["exp"] == 209
        supports = sections(pact_max.read_bytes())[b"LERU"]
        rank_offset = 36 + 4 + struct.unpack_from("<I", supports, 36)[0] + 4
        assert supports[rank_offset:rank_offset+3] == bytes((4, 99, 2))
        run("bond-set", str(pact_source), str(root / "pact-overwrite"), "--instance", "3", "--person", "PID_ユナカ", "--level", "20", valid=False)
        bulk_pact = root / "pact-bulk"
        run("bonds-max", str(pact_source), str(bulk_pact), "--instance", "3")
        assert [(row["level"], row["exp"]) for row in bond_records(bulk_pact.read_bytes()) if row["instance"] == 3] == [(20, 208), (21, 209)]
        if real_directory:
            for name, original in cases[1:]:
                assert (real_directory / name).read_bytes() == original
    print("Emblem CLI: catalogs, bonds, S-ring fill, three melding ranks/costs, byte preservation and protected saves passed.")
