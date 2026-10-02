"""Test minigame edits without distributing private saves."""

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
                assert all(not group["ReadOnly"] for group in groups)
                assert groups[3]["Records"][0]["Ranks"] == ["None", "SSS", "SS", "S", "A", "B", "C", "D", "E", "F"]
                assert groups[4]["Records"][0]["Ranks"] == ["Tiny", "Small", "Middle", "Large", "Big", "Giant"]
                assert groups[4]["Records"][0]["Name"] == ("Charwhal" if language == "en" else "独角红点鲑")
                if save == source:
                    assert all(record["Value"] == 0 for group in groups for record in group["Records"])
                assert save.read_bytes() == original
            catalog = groups
            for group_index in (0, 3, 4):
                record = catalog[group_index]["Records"][0]
                output = Path(directory) / f"{save.name}-{group_index}-edited"
                edits = ["--record", record["Key"], "--value", "100"]
                if group_index == 3:
                    edits += ["--rank", "SSS"]
                if group_index == 4:
                    edits += ["--rank", "Giant", "--best-size", "15"]
                subprocess.run([*command, "main", "minigame-set", str(save), str(output), *edits], check=True, capture_output=True)
                updated = json.loads(subprocess.check_output([*command, "main", "minigames", str(output)], text=True))
                actual = updated[group_index]["Records"][0]
                assert actual["Value"] == 100
                if group_index == 3:
                    assert actual["Rank"] == 1
                if group_index == 4:
                    assert actual["Rank"] == 5 and actual["BestSize"] == 15
                for index, group in enumerate(catalog):
                    for other in group["Records"]:
                        if other["Key"] != record["Key"]:
                            assert other == next(row for row in updated[index]["Records"] if row["Key"] == other["Key"])
                untouched = output.read_bytes()
                refused = subprocess.run([*command, "main", "minigame-set", str(save), str(output), *edits], capture_output=True)
                assert refused.returncode != 0 and output.read_bytes() == untouched
                assert save.read_bytes() == original
            record = catalog[3]["Records"][0]
            output = Path(directory) / f"{save.name}-rank-only"
            subprocess.run([*command, "main", "minigame-set", str(save), str(output), "--record", record["Key"],
                            "--rank", "F"], check=True, capture_output=True)
            updated = json.loads(subprocess.check_output([*command, "main", "minigames", str(output)], text=True))
            assert updated[3]["Records"][0]["Rank"] == 9 and updated[3]["Records"][0]["Value"] == record["Value"]
        for options in (["--language", "ja"], ["--json", "--json"], ["--language"], ["--score", "999"]):
            result = subprocess.run([*command, "main", "minigames", str(source), *options], capture_output=True, text=True)
            assert result.returncode != 0
        training, wyvern, fish = (catalog[index]["Records"][0]["Key"] for index in (0, 3, 4))
        for edits in (["--record", "unknown", "--value", "10"], ["--record", training, "--value", "-1"],
                      ["--record", training, "--value", "2147483648"], ["--record", training, "--value", "1.5"],
                      ["--record", training, "--rank", "SSS"], ["--record", wyvern, "--rank", "10"],
                      ["--record", fish, "--rank", "6"], ["--record", fish, "--best-size", "-1"],
                      ["--record", wyvern, "--best-size", "1"], ["--record", training],
                      ["--record", fish, "--value", "1", "--value", "2"], ["--record", fish, "--value"]):
            output = Path(directory) / "invalid"
            result = subprocess.run([*command, "main", "minigame-set", str(source), str(output), *edits], capture_output=True)
            assert result.returncode != 0 and not output.exists()
        result = subprocess.run([*command, "main", "minigame-set", str(source), str(source),
                                 "--record", training, "--value", "100"], capture_output=True)
        assert result.returncode != 0 and source.read_bytes() == synthetic
    print("Minigame CLI: localized records, score/count/size/rank edits, omitted-field preservation and invalid-output safety passed.")
