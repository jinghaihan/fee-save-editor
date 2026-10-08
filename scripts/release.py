#!/usr/bin/env python3
"""Create a SemVer release commit and tag; GitHub Actions publishes the files."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VERSION_PATH = ROOT / "VERSION"
REMOTE = "origin"


def command(*args: str, capture: bool = False) -> str:
    result = subprocess.run(args, cwd=ROOT, check=True, text=True, capture_output=capture)
    return result.stdout.strip() if capture else ""


def output(*args: str) -> str:
    return command(*args, capture=True)


def check_repository() -> None:
    if Path(output("git", "rev-parse", "--show-toplevel")).resolve() != ROOT:
        raise ValueError("run from this repository")
    if output("git", "branch", "--show-current") != "main":
        raise ValueError("releases must come from main")
    if output("git", "status", "--porcelain"):
        raise ValueError("commit working-tree changes first")
    command("git", "fetch", REMOTE, "main", "--tags")
    if output("git", "rev-parse", "HEAD") != output("git", "rev-parse", f"{REMOTE}/main"):
        raise ValueError("local main is not synchronized with origin/main")


def read_version() -> tuple[int, int, int]:
    raw = VERSION_PATH.read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", raw):
        raise ValueError(f"VERSION is not a stable SemVer value: {raw}")
    return tuple(map(int, raw.split(".")))


def format_version(version: tuple[int, int, int]) -> str:
    return ".".join(map(str, version))


def bump_version(version: tuple[int, int, int], bump: str) -> tuple[int, int, int]:
    major, minor, patch = version
    return {
        "patch": (major, minor, patch + 1),
        "minor": (major, minor + 1, 0),
        "major": (major + 1, 0, 0),
    }[bump]


def select_bump(version: tuple[int, int, int]) -> str | None:
    print(f"\nCurrent version: v{format_version(version)}\n")
    for index, bump in enumerate(("patch", "minor", "major"), 1):
        print(f"  {index}) {bump}  v{format_version(bump_version(version, bump))}")
    print("  q) cancel\n")
    choice = input("Select release type [1-3/q]: ").strip().lower()
    selected = {"1": "patch", "2": "minor", "3": "major", "q": None, "": None}.get(choice, choice)
    if selected not in ("patch", "minor", "major", None):
        raise ValueError("unknown release type")
    return selected


def check_new_tag(tag: str) -> None:
    if output("git", "tag", "--list", tag):
        raise ValueError(f"tag already exists: {tag}")


def check_build(head: str) -> None:
    builds = json.loads(output(
        "gh", "run", "list", "--workflow", "build.yml", "--commit", head,
        "--json", "conclusion,status", "--limit", "20",
    ))
    if not any(build["status"] == "completed" and build["conclusion"] == "success" for build in builds):
        raise ValueError("wait for a successful build of this commit before tagging")


def run_host_tests() -> None:
    command("dotnet", "build", "FeeEditor.slnx", "-c", "Release", "--nologo")
    command("dotnet", "run", "--project", "test/Gui.Smoke.csproj", "-c", "Release", "--no-build")
    command(sys.executable, "scripts/test_cli.py", "--cli", "cli/bin/Release/net10.0/FeeEditor.Cli.dll")


def push_tag(tag: str) -> None:
    command("git", "tag", "-a", tag, "-m", tag)
    command("git", "push", REMOTE, tag)
    print(f"Pushed {tag}. The release workflow will build and publish its downloads.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bump", nargs="?", choices=("patch", "minor", "major"))
    parser.add_argument("--current", action="store_true", help="tag an already committed VERSION (migration only)")
    parser.add_argument("--check", action="store_true", help="validate and show the release plan without publishing")
    parser.add_argument("--yes", action="store_true", help="skip the confirmation prompt")
    args = parser.parse_args()
    if args.current and args.bump:
        parser.error("--current cannot be combined with a version bump")

    check_repository()
    current = read_version()
    if args.current:
        tag = f"v{format_version(current)}"
        check_new_tag(tag)
        check_build(output("git", "rev-parse", "HEAD"))
        print(f"Ready to tag already committed {tag} from {output('git', 'rev-parse', '--short', 'HEAD')}")
    else:
        bump = args.bump or select_bump(current)
        if bump is None:
            print("Release cancelled.")
            return
        next_version = bump_version(current, bump)
        tag = f"v{format_version(next_version)}"
        check_new_tag(tag)
        print(f"\nRelease plan: v{format_version(current)} -> {tag}")
        print(f"Commit: chore: release {tag}")
        print(f"Push: {REMOTE}/main and {tag}")

    if args.check:
        return
    if not args.yes and input("Continue? [y/N]: ").strip().lower() not in ("y", "yes"):
        print("Release cancelled.")
        return

    if not args.current:
        print("Running host tests...", flush=True)
        run_host_tests()
        committed = False
        try:
            VERSION_PATH.write_text(f"{format_version(next_version)}\n", encoding="utf-8")
            command("git", "add", "VERSION")
            if output("git", "diff", "--cached", "--name-only") != "VERSION":
                raise ValueError("the release commit must contain only VERSION")
            command("git", "commit", "-m", f"chore: release {tag}")
            committed = True
            command("git", "push", REMOTE, "main")
        except BaseException:
            if not committed:
                command("git", "restore", "--staged", "VERSION")
                VERSION_PATH.write_text(f"{format_version(current)}\n", encoding="utf-8")
            raise

    push_tag(tag)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, subprocess.CalledProcessError, KeyboardInterrupt) as error:
        print(f"Release failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
