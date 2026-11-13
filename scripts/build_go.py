#!/usr/bin/env python3
"""Build the Go GUI and CLI for one supported desktop platform."""
import argparse
import os
import shutil
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]
TARGETS={"win-x64":("windows","amd64"),"osx-arm64":("darwin","arm64"),"osx-x64":("darwin","amd64"),"linux-x64":("linux","amd64")}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid",choices=TARGETS)
    args=parser.parse_args()
    goos,goarch=TARGETS[args.rid]
    env={**os.environ,"GOOS":goos,"GOARCH":goarch,"CGO_ENABLED":"0"}
    output=ROOT/"dist/gui"/args.rid
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True,exist_ok=True)
    version=(ROOT/"VERSION").read_text().strip()
    for name,package in (("Gui","fee-editor"),("Cli","fee-cli")):
        suffix=".exe" if goos=="windows" else ""
        flags=f"-s -w -X github.com/jinghaihan/fee-save-editor/internal/app.Version={version} -X github.com/jinghaihan/fee-save-editor/internal/cli.Version={version}"
        if goos=="windows" and name=="Gui":flags+=" -H windowsgui"
        subprocess.run(["go","build","-trimpath","-ldflags",flags,"-o",str(output/f"FeeEditor.{name}{suffix}"),f"./cmd/{package}"],cwd=ROOT,env=env,check=True)
    print(output)

if __name__=="__main__":main()
