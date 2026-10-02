"""Verify character recovery with independent record parsing and byte comparisons."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_emblems_cli import sections
from test_roster_equipment_cli import fixture as equipment_fixture

DEAD_MASK = 0x10000A00


def build(base: bytes, pools: dict[bytes, bytes]) -> bytes:
    parts = [tag + struct.pack("<I", len(payload) + 4) + payload for tag, payload in pools.items()]
    positions, offset = [], 260
    for part in parts:
        positions.append(offset)
        offset += len(part)
    positions.append(offset)
    body = base[:132] + struct.pack("<32I", *positions, *([0] * (32 - len(positions)))) + b"".join(parts) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def records(data: bytes) -> list[tuple[int, bytes]]:
    pool = sections(data)[b"TINU"]
    result, offset = [], 32
    while pool[offset] != 255:
        force, count = pool[offset:offset + 2]
        offset += 2
        for _ in range(count):
            length = struct.unpack_from("<I", pool, offset)[0]
            result.append((force, pool[offset:offset + length]))
            offset += length
    assert offset + 1 == len(pool)
    return result


def prepare(base: bytes, force: int = 4, flags: int = DEAD_MASK, sequence: int = 4,
            user_flags: int = 1, duplicate: bool = False, unknown: bool = False) -> bytes:
    pools = sections(base)
    user = bytearray(pools[b"RESU"])
    struct.pack_into("<I", user, 32, user_flags)
    user[36] = sequence
    pools[b"RESU"] = bytes(user)
    units = []
    for _, old in records(base):
        unit = bytearray(old)
        struct.pack_into("<Q", unit, 36, struct.unpack_from("<Q", unit, 36)[0] | flags)
        unit[111] = 0
        units.append(unit)
    if duplicate or unknown:
        units[1][46:50] = struct.pack("<I", 0xDEADBEEF) if unknown else units[0][46:50]
    pools[b"TINU"] = pools[b"TINU"][:32] + bytes((force, len(units))) + b"".join(units) + b"\xff"
    return build(base, pools)


def check_roster_recovery(command: list[str], real_directory: Path | None, base: bytes) -> None:
    fixture = equipment_fixture(base)
    with tempfile.TemporaryDirectory(prefix="fee-roster-recovery-") as temporary:
        root = Path(temporary)
        source, output = root / "source", root / "edited"

        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([*command, "roster", *args], capture_output=True, text=True, encoding="utf-8")

        for force in (3, 4, 5):
            original = prepare(fixture, force=force)
            source.write_bytes(original)
            result = run("restore-character", str(source), str(output), "--character", "1")
            assert result.returncode == 0, result
            changed = output.read_bytes()
            pool = records(changed)
            expected = bytearray(records(original)[1][1])
            struct.pack_into("<Q", expected, 36, struct.unpack_from("<Q", expected, 36)[0] & ~DEAD_MASK)
            listed = run("list", str(output), "--json")
            assert listed.returncode == 0, listed
            characters = json.loads(listed.stdout)
            restored_index = next(row["Index"] for row in characters if row["PersonId"] == "PID_ユナカ")
            assert characters[restored_index]["Force"] == "Absent"
            assert characters[restored_index]["Availability"] == "Available"
            expected[111] = characters[restored_index]["Stats"][0]["Value"]
            assert pool[restored_index] == (3, bytes(expected))
            alear = next(index for index, (_, unit) in enumerate(pool) if unit[46:50] == records(original)[0][1][46:50])
            assert pool[alear][1] == records(original)[0][1]
            assert {tag: value for tag, value in sections(changed).items() if tag != b"TINU"} == {
                tag: value for tag, value in sections(original).items() if tag != b"TINU"}
            assert source.read_bytes() == original
            output.unlink()
            failure = run("restore-character", str(source), str(source), "--character", "1")
            assert failure.returncode == 1 and source.read_bytes() == original

        invalid = [fixture, prepare(fixture, duplicate=True), prepare(fixture, unknown=True), prepare(fixture, user_flags=3)]
        invalid += [prepare(fixture, sequence=value) for value in (0, 2, 3, 5, 7, 8)]
        invalid += [prepare(fixture, flags=DEAD_MASK | value) for value in (8, 0x200000, 0x400000, 0x8000000000, 0x200000000000)]
        for original in invalid:
            source.write_bytes(original)
            failure = run("restore-character", str(source), str(output), "--character", "1")
            assert failure.returncode == 1 and not output.exists(), failure
            assert source.read_bytes() == original
        source.write_bytes(prepare(fixture))
        for options in (["--character", "-1"], ["--character", "2"], ["--character", "1", "--character", "0"],
                        ["--character", "1", "--unknown", "1"], [], ["--character"]):
            failure = run("restore-character", str(source), str(output), *options)
            assert failure.returncode == 1 and not output.exists(), failure

        if real_directory:
            for name in ("Manual0", "Auto"):
                path = real_directory / name
                original = path.read_bytes()
                result = run("list", str(path), "--json")
                assert result.returncode == 0, result
                assert all(row["Availability"] == "Available" for row in json.loads(result.stdout) if row["PersonId"])
                units = records(original)
                pools = sections(original)
                assert units[-1][0] == 3
                dead = bytearray(units[-1][1])
                struct.pack_into("<Q", dead, 36, struct.unpack_from("<Q", dead, 36)[0] | DEAD_MASK)
                dead[111] = 0
                groups = []
                for force in range(7):
                    group = [unit for old_force, unit in units[:-1] if old_force == force]
                    if group:
                        groups.append(bytes((force, len(group))) + b"".join(group))
                pools[b"TINU"] = pools[b"TINU"][:32] + b"".join(groups) + bytes((4, 1)) + dead + b"\xff"
                prepared = build(original, pools)
                source.write_bytes(prepared)
                result = run("restore-character", str(source), str(output), "--character", str(len(units) - 1))
                if name == "Auto":
                    assert result.returncode == 1 and not output.exists(), result
                else:
                    assert result.returncode == 0, result
                    after = sections(output.read_bytes())
                    assert all(after[tag] == payload for tag, payload in pools.items() if tag != b"TINU")
                    assert len(records(output.read_bytes())) == len(units)
                    output.unlink()
                assert path.read_bytes() == original and source.read_bytes() == prepared
    print("Roster recovery CLI: force pools, flags, HP, protected units, contexts, preservation and source protection passed.")
