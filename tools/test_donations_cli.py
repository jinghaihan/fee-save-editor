"""Exercise donation commands with independent fixtures and immutable local saves."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path


def check_donations(command: list[str], real_directory: Path | None, fixture: bytes) -> None:
    def run(*args: str, success: bool = True) -> str:
        result = subprocess.run([*command, "main", *args], capture_output=True, text=True)
        assert (result.returncode == 0) == success, result.stderr
        return result.stdout

    catalog = json.loads(run("donation-catalog", "--json"))
    assert [row["Name"] for row in catalog] == ["Firene", "Brodia", "Elusia", "Solm"]
    assert all(row["Thresholds"] == [0, 5000, 15000, 40000, 90000] for row in catalog)
    assert all(row["MaximumAmount"] == 9_999_999 for row in catalog)
    assert json.loads(run("donation-catalog", "--language", "zh-Hans"))[0]["Name"] == "费列聂"
    with tempfile.TemporaryDirectory(prefix="fee-donations-cli-") as directory:
        root = Path(directory)
        source, output = root / "source", root / "output"
        source.write_bytes(fixture)
        assert all(row["Amount"] == 0 and row["Level"] == 1 for row in json.loads(run("donations", str(source))))
        run("donations-max", str(source), str(output), "--all")
        inserted = output.read_bytes()
        assert all(row["Amount"] == 90000 and row["Level"] == 5 for row in json.loads(run("donations", str(output))))
        assert source.read_bytes() == fixture
        output.unlink()
        source.write_bytes(inserted)
        for level, threshold in enumerate([0, 5000, 15000, 40000, 90000], 1):
            run("donation-set", str(source), str(output), "--country", "Firene", "--level", str(level))
            values = json.loads(run("donations", str(output)))
            assert values[0]["Level"] == level and values[0]["Amount"] == threshold
            assert all(row["Amount"] == 90000 for row in values[1:])
            output.unlink()
        for amount, level in [(5100, 2), (14999, 2), (40000, 4), (9_999_999, 5)]:
            run("donation-set", str(source), str(output), "--country", catalog[0]["Id"], "--amount", str(amount), "--level", str(level))
            assert json.loads(run("donations", str(output)))[0]["Amount"] == amount
            output.unlink()
        run("donations-max", str(source), str(output), "--country", "firene")
        assert output.read_bytes() == inserted
        output.unlink()
        invalid_options = [[], ["--country", "unknown", "--level", "5"], ["--country", "Firene"],
                           ["--country", "Firene", "--level", "0"], ["--country", "Firene", "--level", "6"],
                           ["--country", "Firene", "--amount", "-1"], ["--country", "Firene", "--amount", "10000000"],
                           ["--country", "Firene", "--amount", "1.5"], ["--country", "Firene", "--level", "abc"],
                           ["--country", "Firene", "--level", "5", "--amount", "5000"],
                           ["--country", "Firene", "--country", "Solm", "--level", "5"],
                           ["--country", "Firene", "--level", "5", "--unknown", "1"], ["--country"]]
        for options in invalid_options:
            run("donation-set", str(source), str(output), *options, success=False)
            assert not output.exists() and source.read_bytes() == inserted
        for options in [[], ["--all", "--country", "Firene"], ["--country", "unknown"], ["--level", "5"]]:
            run("donations-max", str(source), str(output), *options, success=False)
            assert not output.exists()
        for options in [["--json", "--json"], ["--language", "fr"], ["--language"], ["--unknown"]]:
            run("donations", str(source), *options, success=False)
        run("donations-max", str(source), str(source), "--all", success=False)
        output.write_bytes(b"existing")
        run("donations-max", str(source), str(output), "--all", success=False)
        assert output.read_bytes() == b"existing"
        output.unlink()
        if real_directory:
            for name in ["Manual0", "Auto"]:
                original = (real_directory / name).read_bytes()
                source.write_bytes(original)
                values = json.loads(run("donations", str(source)))
                run("donation-set", str(source), str(output), "--country", "Solm", "--level", "2")
                modified = output.read_bytes()
                key = "G_投資_ソルム".encode("utf-16-le")
                offset = original.index(key) + len(key) + 1
                expected = bytearray(original)
                struct.pack_into("<i", expected, offset, 5000)
                struct.pack_into("<I", expected, len(expected) - 4, zlib.crc32(expected[:-4]))
                assert modified == expected
                restored = root / "restored"
                run("donation-set", str(output), str(restored), "--country", "Solm", "--amount", str(values[3]["Amount"]))
                assert restored.read_bytes() == original and (real_directory / name).read_bytes() == original
                output.unlink()
                restored.unlink()
            source.write_bytes((real_directory / "Global").read_bytes())
            run("donations", str(source), success=False)
            run("donations-max", str(source), str(output), "--all", success=False)
            assert not output.exists()
    print("Donation CLI: linked levels/amounts, all scopes, validation and real-save preservation passed.")
