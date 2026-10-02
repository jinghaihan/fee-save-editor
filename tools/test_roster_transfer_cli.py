"""Exercise character transfers using independent fixtures and private read-only saves."""

from __future__ import annotations

import copy
import json
import struct
import subprocess
import tempfile
import zlib
from pathlib import Path

from test_roster_cli import fixture, records


def check_roster_transfers(command: list[str], real_directory: Path | None, base: bytes) -> None:
    cases = [("synthetic", fixture(base))]
    if real_directory:
        cases += [(name, (real_directory / name).read_bytes()) for name in ("Auto", "Manual0")]
    with tempfile.TemporaryDirectory(prefix="fee-roster-transfer-") as directory:
        root = Path(directory)
        source, snapshot, edited, restored = [root / name for name in ("source", "character.json", "edited", "restored")]

        def run(*args: str) -> subprocess.CompletedProcess:
            return subprocess.run([*command, "roster", *args], capture_output=True, text=True)

        for name, original in cases:
            source.write_bytes(original)
            exported = run("export", str(source), str(snapshot), "--character", "0")
            assert exported.returncode == 0, (name, exported.stderr)
            saved = snapshot.read_bytes()
            data = json.loads(saved)
            assert data["Format"] == "FeeEditor.RosterCharacter" and data["Version"] == 1
            assert data["GameVersion"] == struct.unpack_from("<I", original, 4)[0]
            positions = records(original)
            sp_offset = positions[0] + struct.unpack_from("<I", original, positions[0])[0] - 8
            assert data["Values"]["SkillPoints"] == struct.unpack_from("<h", original, sp_offset)[0]
            assert data["PersonalStats"] == list(struct.unpack_from("<11b", original, positions[0] + 60))
            duplicate = run("export", str(source), str(snapshot), "--character", "0")
            assert duplicate.returncode != 0 and snapshot.read_bytes() == saved
            no_op = run("import", str(source), str(edited), "--character", "0", "--file", str(snapshot))
            assert no_op.returncode == 0, no_op.stderr
            assert edited.read_bytes() == original
            edited.unlink()
            data["Values"]["SkillPoints"] = 9998 if data["Values"]["SkillPoints"] == 9999 else 9999
            snapshot.write_text(json.dumps(data), encoding="utf-8")
            result = run("import", str(source), str(edited), "--character", "0", "--file", str(snapshot))
            assert result.returncode == 0, result.stderr
            changed = edited.read_bytes()
            allowed = {sp_offset, sp_offset + 1, *range(len(original) - 4, len(original))}
            assert len(changed) == len(original)
            assert {i for i, pair in enumerate(zip(original, changed)) if pair[0] != pair[1]} <= allowed
            assert zlib.crc32(changed[:-4]) == struct.unpack_from("<I", changed, len(changed) - 4)[0]
            snapshot.write_bytes(saved)
            result = run("import", str(edited), str(restored), "--character", "0", "--file", str(snapshot))
            assert result.returncode == 0 and restored.read_bytes() == original
            edited.unlink()
            restored.unlink()
            wrong = run("import", str(source), str(edited), "--character", "1", "--file", str(snapshot))
            assert wrong.returncode != 0 and not edited.exists()
            overwrite = run("import", str(source), str(source), "--character", "0", "--file", str(snapshot))
            assert overwrite.returncode != 0 and source.read_bytes() == original
            for field, value in (("PersonHash", 0), ("Version", 2), ("PersonalStats", []), ("CurrentHP", 256),
                                 ("InternalLevel", 101), ("Items", None), ("Unknown", 1)):
                invalid = copy.deepcopy(json.loads(saved))
                invalid[field] = value
                snapshot.write_text(json.dumps(invalid), encoding="utf-8")
                result = run("import", str(source), str(edited), "--character", "0", "--file", str(snapshot))
                assert result.returncode != 0 and not edited.exists(), (field, result.stderr)
            for invalid in (b"{", b"null", b"{}", b" " * 1_048_577):
                snapshot.write_bytes(invalid)
                result = run("import", str(source), str(edited), "--character", "0", "--file", str(snapshot))
                assert result.returncode != 0 and not edited.exists()
            assert source.read_bytes() == original
            snapshot.unlink()
        assert not list(root.glob(".fee-character-*.tmp"))
    print("Roster transfer CLI: exact copies/restoration, isolated byte diffs, identity/bounds, malformed files and overwrite protection passed.")
