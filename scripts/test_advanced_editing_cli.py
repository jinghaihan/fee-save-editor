"""Check advanced editing with independent pool parsing and untouched source files."""

from __future__ import annotations

import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_emblems_cli import sections
from test_roster_cli import game_hash
from test_roster_equipment_cli import fixture as equipment_fixture
from test_roster_recovery_cli import build, prepare, records


def check_advanced_editing(command: list[str], real_directory: Path | None, base: bytes) -> None:
    with tempfile.TemporaryDirectory(prefix="fee-advanced-cli-") as directory:
        root = Path(directory)
        source, output = root / "source", root / "edited"

        def run(*args: str, valid: bool = True) -> str:
            result = subprocess.run([*command, *args], capture_output=True, text=True, encoding="utf-8")
            assert (result.returncode == 0) == valid, (args, result.stderr)
            return result.stdout

        source.write_bytes(base)
        key = "G_所持_IID_てつの晶石"
        listed = json.loads(run("variables", "list", str(source), "--json"))
        assert len(listed) == 3 and all(row["Key"] != "unrelated" for row in listed)
        for index, value in enumerate((-2147483648, -1, 0, 2147483647)):
            edited = root / f"variable-{index}"
            restored = root / f"variable-restored-{index}"
            run("variables", "set", str(source), str(edited), "--key", key, "--value", str(value))
            assert next(row for row in json.loads(run("variables", "list", str(edited))) if row["Key"] == key)["Value"] == value
            run("variables", "set", str(edited), str(restored), "--key", key, "--value", "12")
            assert restored.read_bytes() == base
        for key, value in (("missing", "0"), ("unrelated", "0"), (key, "2147483648"), (key, "-2147483649")):
            run("variables", "set", str(source), str(output), "--key", key, "--value", value, valid=False)
            assert not output.exists()
        original = prepare(equipment_fixture(base), force=3, flags=0)
        pools = sections(original)
        units = []
        for _, record in records(original):
            unit = bytearray(record)
            struct.pack_into("<I", unit, len(unit) - 6, 0)
            units.append(bytes(unit))
        pools[b"TINU"] = pools[b"TINU"][:32] + bytes((3, len(units))) + b"".join(units) + b"\xff"
        original = build(original, pools)
        source.write_bytes(original)
        missing = json.loads(run("roster", "missing", str(source), "--json"))
        assert len(missing) == 39 and all(row["Id"] != "PID_リュール" for row in missing)
        run("roster", "add", str(source), str(output), "--person", "PID_フラン")
        added = output.read_bytes()
        new_record = records(added)[-1][1]
        assert len(records(added)) == 3 and struct.unpack_from("<I", new_record, 46)[0] == game_hash("PID_フラン")
        assert records(added)[:2] == records(original)
        assert zlib.crc32(added[:-4]) == struct.unpack_from("<I", added, len(added) - 4)[0]
        moved, deleted = root / "moved", root / "deleted"
        run("roster", "move", str(output), str(moved), "--character", "2", "--force", "Lost")
        assert records(moved.read_bytes())[-1] == (5, new_record)
        run("roster", "delete", str(moved), str(deleted), "--character", "2")
        assert sections(deleted.read_bytes())[b"TINU"] == sections(original)[b"TINU"]
        assert sections(deleted.read_bytes())[b"DBDG"] == sections(added)[b"DBDG"]
        for verb, options in (("delete", ["--character", "0"]), ("delete", ["--character", "1"]),
                              ("add", ["--person", "PID_リュール"]), ("add", ["--person", "PID_ユナカ"]),
                              ("move", ["--character", "1", "--force", "Player"]),
                              ("move", ["--character", "1", "--force", "3"])):
            rejected = root / f"rejected-{verb}"
            run("roster", verb, str(source), str(rejected), *options, valid=False)
            assert not rejected.exists()
        removed = root / "removed-emblem"
        run("emblems", "remove", str(source), str(removed), "--instance", "1")
        after = sections(removed.read_bytes())
        assert struct.unpack_from("<I", after[b" DOG"], 32)[0] == 2
        assert after[b"DBDG"] == sections(original)[b"DBDG"]
        readded = root / "readded-emblem"
        run("emblems", "add", str(removed), str(readded), "--emblem", "GID_マルス")
        assert sections(readded.read_bytes())[b"DBDG"] == sections(original)[b"DBDG"]
        invalid_pools = sections(original)
        invalid_unit = bytearray(invalid_pools[b"TINU"])
        offset = 34 + len(records(original)[0][1]) + 52
        struct.pack_into("<I", invalid_unit, offset, 0xDEADBEEF)
        invalid_pools[b"TINU"] = bytes(invalid_unit)
        broken = root / "broken-class"
        broken.write_bytes(build(original, invalid_pools))
        fixed = root / "repaired-class"
        run("roster", "classes-repair", str(broken), str(fixed), "--all")
        assert struct.unpack_from("<I", records(fixed.read_bytes())[1][1], 52)[0] == game_hash("JID_シーフ")
        assert source.read_bytes() == original
        if real_directory:
            for name in ("Manual0", "Auto"):
                actual = real_directory / name
                before = actual.read_bytes()
                variables = json.loads(run("variables", "list", str(actual), "--json"))
                assert len(variables) > 100
                copy = root / f"{name}-variable-copy"
                variable = variables[0]
                run("variables", "set", str(actual), str(copy), "--key", variable["Key"], "--value", str(variable["Value"]))
                assert copy.read_bytes() == before and actual.read_bytes() == before
    print("Advanced CLI: signed variables, character creation/movement/deletion, repair, Emblem removal and preserved source files passed.")
