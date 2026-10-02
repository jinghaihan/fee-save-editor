"""Test read-only minigame records without distributing private saves."""

import json
import subprocess
import tempfile
from pathlib import Path


def check_minigames(command, save_directory, synthetic):
    with tempfile.TemporaryDirectory(prefix="fee-minigames-cli-") as directory:
        source = Path(directory) / "source"
        source.write_bytes(synthetic)
        sources = [source]
        if save_directory:
            sources += [save_directory / "Manual0", save_directory / "Auto"]
        for save in sources:
            original = save.read_bytes()
            for language in ("en", "zh-Hans", "en"):
                result = subprocess.run([*command, "main", "minigames", str(save), "--json", "--language", language],
                                        capture_output=True, text=True, check=True)
                groups = json.loads(result.stdout)
                assert [group["Id"] for group in groups] == ["PushUps", "SitUps", "Squats", "WyvernRide", "Fishing"]
                assert [len(group["Records"]) for group in groups] == [4, 4, 4, 3, 20]
                assert all(group["ReadOnly"] for group in groups)
                assert groups[4]["Records"][0]["Name"] == ("Charwhal" if language == "en" else "独角红点鲑")
                if save == source:
                    assert all(record["Value"] == 0 for group in groups for record in group["Records"])
                assert save.read_bytes() == original
        for options in (["--language", "ja"], ["--json", "--json"], ["--language"], ["--score", "999"]):
            result = subprocess.run([*command, "main", "minigames", str(source), *options], capture_output=True, text=True)
            assert result.returncode != 0
    print("Minigame CLI: 35 localized read-only records, strict options and source preservation passed.")
