"""Check achievement CLI scopes, malformed inputs and reward preservation."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def check_achievements(command: list[str], real_directory: Path | None, fixture: bytes) -> None:
    def run(*args: str, success: bool = True) -> str:
        result = subprocess.run([*command, "achievements", *args], capture_output=True, text=True, encoding="utf-8")
        assert (result.returncode == 0) == success, result.stderr
        return result.stdout

    catalog = json.loads(run("catalog", "--json"))
    assert len(catalog) == 765 and len({row["Id"] for row in catalog}) == 765
    assert catalog[0]["Name"] == "Complete a support conversation with Vander."
    chinese = json.loads(run("catalog", "--language", "zh-Hans"))
    assert "凡德雷" in chinese[0]["Name"]
    for options in [("--language", "xx"), ("--json", "--json"), ("--language",), ("--other",)]:
        run("catalog", *options, success=False)

    def wide(text: str) -> bytes:
        encoded = text.encode("utf-16-le")
        return struct.pack("<I", len(encoded)) + encoded

    def with_statuses(values: list[int], duplicate: bool = False, wrong_type: bool = False) -> bytes:
        keys = ["G_実績_" + row["Id"][4:] for row in catalog[:4]]
        records = []
        for index, (key, value) in enumerate(zip(keys, values)):
            records.append(wide(key) + (b"\x01" + wide("bad") if index == 0 and wrong_type else b"\x00" + struct.pack("<i", value)))
        if duplicate:
            records.append(records[0])
        records.append(wide("G_実績_Pクラスチェンジ") + b"\x00" + struct.pack("<i", 123))
        extra = b"".join(records)
        start = struct.unpack_from("<I", fixture, 132)[0] + 57
        end = start + struct.unpack_from("<I", fixture, start)[0]
        data = bytearray(fixture[:end] + extra + fixture[end:])
        for offset in (136, 140, 264, start):
            struct.pack_into("<I", data, offset, struct.unpack_from("<I", fixture, offset)[0] + len(extra))
        struct.pack_into("<I", data, start + 36, struct.unpack_from("<I", fixture, start + 36)[0] + len(records))
        struct.pack_into("<I", data, len(data) - 4, zlib.crc32(data[:-4]))
        return bytes(data)

    with tempfile.TemporaryDirectory(prefix="fee-achievements-cli-") as directory:
        root = Path(directory)
        source, output = root / "source", root / "output"
        source.write_bytes(fixture)
        assert all(not row["Achieved"] for row in json.loads(run("list", str(source))))
        run("unlock", str(source), str(output), "--achievement", catalog[0]["Id"])
        single = json.loads(run("list", str(output)))
        assert single[0]["RewardAvailable"] and not single[0]["RewardClaimed"]
        assert sum(row["Achieved"] for row in single) == 1
        assert source.read_bytes() == fixture
        output.unlink()
        original = with_statuses([0, 1, 2, 3])
        source.write_bytes(original)
        run("unlock", str(source), str(output), "--all")
        batch = json.loads(run("list", str(output)))
        assert all(row["Achieved"] for row in batch)
        assert [row["Status"] for row in batch[:4]] == ["Cleared", "Cleared", "Showed", "Completed"]
        assert batch[3]["RewardClaimed"] and not batch[3]["RewardAvailable"]
        maximum = output.read_bytes()
        assert struct.unpack_from("<i", maximum, maximum.index("G_実績_Pクラスチェンジ".encode("utf-16-le")) + len("G_実績_Pクラスチェンジ".encode("utf-16-le")) + 1)[0] == 123
        output.unlink()
        source.write_bytes(maximum)
        run("unlock", str(source), str(output), "--all")
        assert output.read_bytes() == maximum
        output.unlink()
        for options in [[], ["--all", "--all"], ["--all", "--achievement", catalog[0]["Id"]],
                        ["--achievement"], ["--achievement", "AID_unknown"], ["--achievement", "AID_Pクラスチェンジ"],
                        ["--all", "--unknown"]]:
            run("unlock", str(source), str(output), *options, success=False)
            assert not output.exists()
        run("unlock", str(source), str(source), "--all", success=False)
        assert source.read_bytes() == maximum
        for malformed in [with_statuses([-1, 1, 2, 3]), with_statuses([4, 1, 2, 3]),
                          with_statuses([0, 1, 2, 3], duplicate=True), with_statuses([0, 1, 2, 3], wrong_type=True)]:
            source.write_bytes(malformed)
            run("unlock", str(source), str(output), "--all", success=False)
            assert not output.exists() and source.read_bytes() == malformed
        if real_directory:
            for name in ("Manual0", "Auto"):
                path = real_directory / name
                original = path.read_bytes()
                before = json.loads(run("list", str(path)))
                run("unlock", str(path), str(output), "--all")
                after = json.loads(run("list", str(output)))
                assert all(row["Achieved"] for row in after)
                assert all(old["Status"] == new["Status"] for old, new in zip(before, after) if old["Achieved"])
                offsets = set()
                for row in before:
                    if not row["Achieved"]:
                        text = ("G_実績_" + row["Id"][4:]).encode("utf-16-le")
                        offset = original.index(text) + len(text) + 1
                        offsets.update(range(offset, offset + 4))
                edited = output.read_bytes()
                assert len(edited) == len(original)
                assert all(left == right or index in offsets for index, (left, right) in enumerate(zip(original[:-4], edited[:-4])))
                assert path.read_bytes() == original
                output.unlink()
    print("Achievement CLI: scopes, rewards, hidden counters, bounds and source preservation passed.")
