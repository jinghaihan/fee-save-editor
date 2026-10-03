"""Check native activity counters and physical Emblem dirtiness without modifying private saves."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_roster_equipment_cli import fixture as equipment_fixture
from test_emblems_cli import sections
from test_cli import main_offsets


def check_activities(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, valid: bool = True) -> str:
        result = subprocess.run([*command, *args], capture_output=True, text=True, encoding="utf-8")
        assert (result.returncode == 0) == valid, (args, result.stderr)
        return result.stdout

    synthetic = bytearray(equipment_fixture(base))
    money = main_offsets(synthetic)["Money"]
    struct.pack_into("<ii", synthetic, money + 8, 1, 3)
    struct.pack_into("<I", synthetic, len(synthetic) - 4, zlib.crc32(synthetic[:-4]))
    cases = [("synthetic", bytes(synthetic))]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Manual0", "Auto")]
    with tempfile.TemporaryDirectory(prefix="fee-activities-cli-") as directory:
        root = Path(directory)
        for name, original in cases:
            source, output, restored = [root / f"{name}-{suffix}" for suffix in ("source", "edited", "restored")]
            source.write_bytes(original)
            current = json.loads(run("main", "activities", str(source), "--json"))
            offset = main_offsets(original)["Money"] + 8
            used_training, used_arena = struct.unpack_from("<ii", original, offset)
            assert current == {"TrainingRemaining": 1 - used_training, "ArenaRemaining": 3 - used_arena,
                               "MaximumTraining": 1, "MaximumArena": 3}
            for training, arena in ((0, 0), (1, 3), (0, 2)):
                run("main", "activities-set", str(source), str(output), "--training-remaining", str(training), "--arena-remaining", str(arena))
                expected = bytearray(original)
                struct.pack_into("<ii", expected, offset, 1 - training, 3 - arena)
                struct.pack_into("<I", expected, len(expected) - 4, zlib.crc32(expected[:-4]))
                assert output.read_bytes() == expected
                run("main", "activities-set", str(output), str(restored), "--training-remaining", str(current["TrainingRemaining"]),
                    "--arena-remaining", str(current["ArenaRemaining"]))
                assert restored.read_bytes() == original
                output.unlink()
                restored.unlink()
            run("main", "activities-restore", str(source), str(output))
            assert json.loads(run("main", "activities", str(output)))["ArenaRemaining"] == 3
            output.unlink()
            for flags in ((), ("--training-remaining", "2"), ("--arena-remaining", "4"), ("--arena-remaining", "-1"),
                          ("--arena-remaining", "1.5"), ("--arena-remaining", "1", "--arena-remaining", "2"), ("--bad", "1")):
                run("main", "activities-set", str(source), str(output), *flags, valid=False)
                assert not output.exists()

            conditions = json.loads(run("emblems", "conditions", str(source), "--json"))
            assert len(conditions) == (2 if name == "synthetic" else 19)
            assert all(row["MaximumDirtiness"] == 255 and row["EmblemId"] != "GID_リュール" for row in conditions)
            assert json.loads(run("emblems", "conditions", str(source), "--language", "ja"))[0]["Name"] == "マルス"
            for row in conditions:
                run("emblems", "dirt-set", str(source), str(output), "--instance", str(row["InstanceId"]), "--value", "255")
                actual = json.loads(run("emblems", "conditions", str(output)))
                assert next(item for item in actual if item["InstanceId"] == row["InstanceId"])["Dirtiness"] == 255
                assert {key: value for key, value in sections(original).items() if key != b" DOG"} == {
                    key: value for key, value in sections(output.read_bytes()).items() if key != b" DOG"}
                differences = [i for i, (before, after) in enumerate(zip(original, output.read_bytes())) if before != after]
                assert len([i for i in differences if i < len(original) - 4]) <= 1
                run("emblems", "dirt-set", str(output), str(restored), "--instance", str(row["InstanceId"]), "--value", str(row["Dirtiness"]))
                assert restored.read_bytes() == original
                restored.unlink()
                run("emblems", "clean", str(output), str(restored), "--instance", str(row["InstanceId"]))
                assert next(item for item in json.loads(run("emblems", "conditions", str(restored)))
                            if item["InstanceId"] == row["InstanceId"])["Dirtiness"] == 0
                restored.unlink()
                run("emblems", "clean-all", str(output), str(restored))
                assert all(item["Dirtiness"] == 0 for item in json.loads(run("emblems", "conditions", str(restored))))
                assert {key: value for key, value in sections(original).items() if key != b" DOG"} == {
                    key: value for key, value in sections(restored.read_bytes()).items() if key != b" DOG"}
                output.unlink()
                restored.unlink()
            for value in ("-1", "256", "abc", "1.5", "2147483648"):
                run("emblems", "dirt-set", str(source), str(output), "--instance", "1", "--value", value, valid=False)
                assert not output.exists()
            run("emblems", "clean", str(source), str(output), "--instance", "999", valid=False)
            run("emblems", "clean", str(source), str(source), "--instance", "1", valid=False)
            run("emblems", "clean-all", str(source), str(source), valid=False)
            run("emblems", "clean-all", str(source), str(output), "--instance", "1", valid=False)
            run("main", "activities-restore", str(source), str(source), valid=False)
            output.write_bytes(b"preserve existing file")
            run("main", "activities-restore", str(source), str(output), valid=False)
            run("emblems", "clean", str(source), str(output), "--instance", "1", valid=False)
            assert output.read_bytes() == b"preserve existing file" and source.read_bytes() == original
            output.unlink()
            if name != "synthetic":
                assert (real_directory / name).read_bytes() == original
        if real_directory:
            source = real_directory / "Global"
            output = root / "global-edited"
            run("main", "activities", str(source), valid=False)
            run("emblems", "conditions", str(source), valid=False)
            assert not output.exists()
    print("Activities/ring-care CLI: independent binary checks, limits, native/DLC rings and real-save preservation passed.")
