#!/usr/bin/env python3
"""Extract a platform package and verify its launchers and CLI."""

import argparse
import plistlib
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=("win-x64", "osx-arm64", "osx-x64", "linux-x64"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    version = (root / "VERSION").read_text(encoding="utf-8").strip()
    archive = root / "dist" / f"fee-save-editor-v{version}-{args.rid}.zip"
    with tempfile.TemporaryDirectory(prefix="fee-package-test-") as directory:
        target = Path(directory)
        if args.rid.startswith("osx-"):
            subprocess.run(["ditto", "-x", "-k", str(archive), str(target)], check=True)
            app = target / "FEE Save Editor.app"
            with (app / "Contents/Info.plist").open("rb") as source:
                info = plistlib.load(source)
            assert info["CFBundleShortVersionString"] == version
            assert (app / "Contents/MacOS" / info["CFBundleExecutable"]).is_file()
            assert info["CFBundleIconFile"] == "AppIcon.icns"
            assert (app / "Contents/Resources/AppIcon.icns").stat().st_size > 0
            subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)], check=True)
            cli = target / "Cli/FeeEditor.Cli"
        else:
            with zipfile.ZipFile(archive) as source:
                source.extractall(target)
                for member in source.infolist():
                    path = target / member.filename
                    if path.is_file() and args.rid != "win-x64":
                        path.chmod(member.external_attr >> 16)
            suffix = ".exe" if args.rid == "win-x64" else ""
            gui = target / ("FEE Save Editor.exe" if suffix else "FeeEditor.Gui")
            assert gui.is_file()
            cli = target / f"FeeEditor.Cli{suffix}"
        assert not list(target.rglob("*.pdb"))
        subprocess.run([sys.executable, str(root / "tools/test_cli.py"), "--cli", str(cli)], check=True)
    print("Release package tests passed.")


if __name__ == "__main__":
    main()
