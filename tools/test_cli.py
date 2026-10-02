#!/usr/bin/env python3
"""Test the CLI without distributing private save files."""

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


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", required=True, type=Path)
    args = parser.parse_args()
    command = cli_command(args.cli)
    version = (Path(__file__).resolve().parents[1] / "VERSION").read_text().strip()
    actual = subprocess.check_output([*command, "--version"], text=True).strip()
    assert actual == version, (actual, version)
    subprocess.run([*command, "--help"], check=True, capture_output=True)
    assert subprocess.run([*command, "unknown"], capture_output=True).returncode != 0
    check_saves(command)
    print("CLI tests passed.")


if __name__ == "__main__":
    main()
