"""Independent support fixtures, legal-rank checks and byte-preservation tests."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_emblems_cli import fixture as emblem_fixture, sections, wide

PAIRS = [
    ("PID_リュールPID_ヴァンドレ", (5, 20, 45)),
    ("PID_ヴァンドレPID_クラン", (5, 10, 25)),
    ("PID_リュールPID_エル", (3, 15, 30)),
    ("PID_リュールPID_ユナカ", (5, 20, 45)),
]


def fixture(base: bytes) -> bytes:
    source = emblem_fixture(base)
    records = sections(source)
    payload = struct.pack("<II", 1, 0xcdcdcdcd) + bytes(24) + struct.pack("<I", 5)
    for key, _ in PAIRS:
        payload += wide(key) + struct.pack("<IBbb", 1, 0, 0, 7)
    payload += wide("PID_unknownPID_other") + struct.pack("<IBbb", 1, 1, 2, 3)
    records[b"LERU"] = payload
    parts = [tag + struct.pack("<I", len(data) + 4) + data for tag, data in records.items()]
    offsets, offset = [], 260
    for part in parts:
        offsets.append(offset)
        offset += len(part)
    offsets.append(offset)
    body = source[:132] + struct.pack("<32I", *offsets, *([0] * (32 - len(offsets)))) + b"".join(parts) + b"LVRC"
    return body + struct.pack("<I", zlib.crc32(body))


def parse(data: bytes) -> list[tuple]:
    payload = sections(data)[b"LERU"]
    count = struct.unpack_from("<I", payload, 32)[0]
    result, offset = [], 36
    for _ in range(count):
        length = struct.unpack_from("<I", payload, offset)[0]
        key = payload[offset + 4:offset + 4 + length].decode("utf-16-le")
        offset += 4 + length
        version, rank, points, score = struct.unpack_from("<IBbb", payload, offset)
        assert version == 1
        result.append((key, rank, points, score))
        offset += 7
    assert offset == len(payload)
    return result


def check_supports(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, ok: bool = True) -> bytes:
        result = subprocess.run([*command, "supports", *args], capture_output=True)
        assert (result.returncode == 0) == ok, (args, result.stderr.decode())
        return result.stdout

    catalog = json.loads(run("catalog", "--json"))
    assert len(catalog) == 231
    for key, expected in PAIRS:
        assert next(row for row in catalog if row["Key"] == key)["Thresholds"] == list(expected)
    with tempfile.TemporaryDirectory(prefix="fee-support-cli-") as directory:
        folder = Path(directory)
        source, output = folder / "Manual0", folder / "edited"
        original = fixture(base)
        source.write_bytes(original)
        listed = json.loads(run("list", str(source), "--json"))
        assert len(listed) == 5 and listed[0]["FirstName"] == "Alear"
        assert listed[3]["MaximumRank"] == "A+" and listed[4]["MaximumRank"] is None
        translated = json.loads(run("list", str(source), "--language", "zh-Hans"))
        assert translated[0]["FirstName"] == "琉尔"
        for index, (key, thresholds) in enumerate(PAIRS[:3]):
            for rank, points in zip(("None", "C", "B", "A"), (0, *thresholds)):
                target = folder / f"rank-{index}-{rank}"
                run("set", str(source), str(target), "--pair", key, "--rank", rank)
                changed = target.read_bytes()
                result = parse(changed)
                assert result[index][1:3] == (("None", "C", "B", "A").index(rank), points)
                assert result[index][3] == 7 and all(a == b for i, (a, b) in enumerate(zip(result, parse(original))) if i != index)
                for tag, data in sections(original).items():
                    assert tag == b"LERU" or sections(changed)[tag] == data
            target = folder / f"max-{index}"
            run("max", str(source), str(target), "--pair", key)
            assert parse(target.read_bytes())[index][1:3] == (3, thresholds[2])
        run("max", str(source), str(output), "--all")
        rows = parse(output.read_bytes())
        assert [row[1] for row in rows] == [3, 3, 3, 4, 1]
        assert rows[3][2] == 99 and rows[-1] == parse(original)[-1]
        run("set", str(output), str(folder / "downgrade"), "--pair", PAIRS[3][0], "--rank", "A", ok=False)
        for bad in ("-1", "100", "1.5", "abc", "2147483648"):
            run("set", str(source), str(folder / "invalid"), "--pair", PAIRS[0][0], "--points", bad, ok=False)
            assert not (folder / "invalid").exists()
        run("set", str(source), str(folder / "illegal"), "--pair", PAIRS[0][0], "--rank", "A+", ok=False)
        run("set", str(source), str(folder / "unknown"), "--pair", "PID_unknownPID_other", "--rank", "A", ok=False)
        run("max", str(source), str(source), "--all", ok=False)
        run("max", str(source), str(output), "--all", ok=False)
        assert source.read_bytes() == original
        if real_directory:
            for name in ("Manual0", "Auto"):
                path = real_directory / name
                before = path.read_bytes()
                real = json.loads(run("list", str(path), "--json"))
                assert len(real) == 231 and all(row["Rank"] == "A" for row in real)
                destination = folder / f"{name}-max"
                run("max", str(path), str(destination), "--all")
                assert destination.read_bytes() == before and path.read_bytes() == before
    print("Support CLI: thresholds, single/batch maximum, Pact protections and exact preservation passed.")
