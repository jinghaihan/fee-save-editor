#!/usr/bin/env python3
"""Extract a platform package and verify its launchers and CLI."""

import argparse
import json
import plistlib
import subprocess
import tempfile
import zipfile
from pathlib import Path

from test_package_smoke import check_gui_startup, check_package_cli


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=("win-x64", "osx-arm64", "osx-x64", "linux-x64"))
    parser.add_argument("--archive", type=Path, help="Check a specific local archive instead of dist")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    version = (root / "VERSION").read_text(encoding="utf-8").strip()
    archive = args.archive or root / "dist" / f"fee-save-editor-v{version}-{args.rid}.zip"
    with tempfile.TemporaryDirectory(prefix="fee-package-test-") as directory:
        target = Path(directory)
        if args.rid.startswith("osx-"):
            subprocess.run(["ditto", "-x", "-k", str(archive), str(target)], check=True)
            app = target / "FEE Save Editor.app"
            with (app / "Contents/Info.plist").open("rb") as source:
                info = plistlib.load(source)
            assert info["CFBundleShortVersionString"] == version
            assert (app / "Contents/MacOS" / info["CFBundleExecutable"]).is_file()
            gui = app / "Contents/MacOS" / info["CFBundleExecutable"]
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
        if list(target.rglob("*.dll")) or list(target.rglob("*.runtimeconfig.json")):
            raise ValueError("The package contains obsolete runtime files")
        assert (target / "licenses/MyGo.txt").is_file()
        assert not list(target.rglob("*.pdb"))
        check_package_cli(cli, version)
        check_gui_startup(gui)
    print("Release package tests passed.")


if __name__ == "__main__":
    main()
