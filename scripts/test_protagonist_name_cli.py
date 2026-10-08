"""Verify protagonist-name edits on copies, without changing private saves."""

from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path

from test_emblems_cli import sections


def check_protagonist_name(command: list[str], real_directory: Path | None, base: bytes) -> None:
    def run(*args: str, valid: bool = True) -> str:
        result = subprocess.run([*command, *args], capture_output=True, text=True, encoding="utf-8")
        assert (result.returncode == 0) == valid, (args, result.stderr)
        return result.stdout

    with tempfile.TemporaryDirectory(prefix="fee-name-cli-") as directory:
        root = Path(directory)
        missing = root / "missing-roster"
        missing.write_bytes(base)
        rejected = root / "rejected"
        run("main", "name-set", str(missing), str(rejected), "--name", "Alear", valid=False)
        assert not rejected.exists() and missing.read_bytes() == base
        if not real_directory:
            return
        for filename in ("Manual0", "Auto"):
            original = (real_directory / filename).read_bytes()
            source, output, restored = [root / f"{filename}-{suffix}" for suffix in ("source", "edited", "restored")]
            source.write_bytes(original)
            name = json.loads(run("main", "name", str(source)))["PlayerName"]
            assert isinstance(name, str) and name
            for replacement in ("琉尔", "Renamed Alear", "Aléar", "A😀"):
                run("main", "name-set", str(source), str(output), "--name", replacement)
                assert json.loads(run("main", "name", str(output)))["PlayerName"] == replacement
                before, after = sections(original), sections(output.read_bytes())
                assert all(after[tag] == payload for tag, payload in before.items() if tag != b"TINU")
                run("main", "name-set", str(output), str(restored), "--name", name)
                assert restored.read_bytes() == original
                output.unlink()
                restored.unlink()
            for invalid in ("", " ", "a\n", "x" * 2049):
                run("main", "name-set", str(source), str(output), "--name", invalid, valid=False)
                assert not output.exists()
            run("main", "name-set", str(source), str(source), "--name", "Alear", valid=False)
            assert source.read_bytes() == original and (real_directory / filename).read_bytes() == original
    print("Protagonist-name CLI: Unicode, byte-exact restoration, unrelated sections and rejection passed.")
