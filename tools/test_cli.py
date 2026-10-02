#!/usr/bin/env python3
"""Test the CLI without distributing private save files."""

import argparse
import subprocess
from pathlib import Path


def cli_command(cli: Path) -> list[str]:
    command = [str(cli.resolve())]
    return ["dotnet", *command] if cli.suffix == ".dll" else command


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
    print("CLI tests passed.")


if __name__ == "__main__":
    main()
