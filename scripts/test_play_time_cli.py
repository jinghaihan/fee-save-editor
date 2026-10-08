"""Verify play-time editing independently of the native save implementation."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def check_play_time(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, valid: bool = True) -> str:
        result = subprocess.run([*command, *args], capture_output=True, text=True, encoding="utf-8")
        assert (result.returncode == 0) == valid, (args, result.stderr)
        return result.stdout

    original = bytearray(base[:-8])
    offsets = [offset for offset in struct.unpack_from("<32I", original, 132) if offset]
    time_offset = len(original)
    original += b"EMIT" + struct.pack("<I", 44) + bytes(32) + struct.pack("<ff", 632628.25, 123.5)
    offsets[-1:] = [time_offset, len(original)]
    struct.pack_into("<32I", original, 132, *offsets, *([0] * (32 - len(offsets))))
    struct.pack_into("<f", original, 40, 632628.25)
    struct.pack_into("<I", original, 60, zlib.crc32(original[:60]))
    original += b"LVRC"
    original += struct.pack("<I", zlib.crc32(original))
    cases = [("synthetic", bytes(original))]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Auto", "Manual0")]

    with tempfile.TemporaryDirectory(prefix="fee-play-time-cli-") as directory:
        root = Path(directory)
        for name, original in cases:
            source, output = root / f"{name}-source", root / f"{name}-edited"
            source.write_bytes(original)
            time_offset = next(offset for offset in struct.unpack_from("<32I", original, 132)
                               if offset and original[offset:offset + 4] == b"EMIT")
            current = json.loads(run("main", "show", str(source), "--json"))
            assert current["PlayTimeSeconds"] == struct.unpack_from("<f", original, time_offset + 40)[0]
            for value, seconds in (("0:00:00", 0), ("0:00:01.5", 1.5), ("240:00:01", 864001),
                                   ("268:37:24", 967044), ("999:59:59.5", 3599999.5)):
                run("main", "set", str(source), str(output), "--play-time", value)
                expected = bytearray(original)
                struct.pack_into("<f", expected, time_offset + 40, seconds)
                struct.pack_into("<f", expected, 40, seconds)
                checksums = [index for index in range(44, 125)
                             if struct.unpack_from("<I", original, index)[0] == zlib.crc32(original[:index])]
                assert len(checksums) == 1
                struct.pack_into("<I", expected, checksums[0], zlib.crc32(expected[:checksums[0]]))
                struct.pack_into("<I", expected, len(expected) - 4, zlib.crc32(expected[:-4]))
                assert output.read_bytes() == expected
                actual = json.loads(run("main", "show", str(output), "--json"))
                assert actual["PlayTimeSeconds"] == seconds
                assert actual["PlayTime"] == value
                assert {k: v for k, v in current.items() if not k.startswith("PlayTime")} == {
                    k: v for k, v in actual.items() if not k.startswith("PlayTime")}
                output.unlink()
            for value in ("", "240", "-1:00:00", "1:60:00", "1:00:60", "1:00:-1", "1:00:NaN",
                          "1000:00:00", "999:59:59.6", "999:59:59.5000001", "1:00:Infinity", "1:0:1:0"):
                run("main", "set", str(source), str(output), "--play-time", value, valid=False)
                assert not output.exists()
            run("main", "set", str(source), str(output), "--play-time", "1:00:00", "--play-time", "2:00:00", valid=False)
            run("main", "set", str(source), str(source), "--play-time", "268:37:24", valid=False)
            assert source.read_bytes() == original
            print(f"{name}: CLI play time, boundary validation and independent exact byte diff passed.")

        source.write_bytes(base)
        run("main", "set", str(source), str(output), "--play-time", "268:37:24", valid=False)
        assert not output.exists()
