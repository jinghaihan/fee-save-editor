#!/usr/bin/env python3
"""Convert existing Sommie artwork into platform icons without distorting it."""

import argparse
from pathlib import Path

from PIL import Image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "gui/Assets")
    args = parser.parse_args()
    with Image.open(args.source) as source:
        artwork = source.convert("RGBA")
        artwork.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
        icon = Image.new("RGBA", (1024, 1024))
        icon.alpha_composite(artwork, ((1024 - artwork.width) // 2, (1024 - artwork.height) // 2))
        args.output.mkdir(parents=True, exist_ok=True)
        icon.save(args.output / "app-icon.png")
        icon.save(args.output / "app-icon.ico", sizes=[(size, size) for size in (16, 24, 32, 48, 64, 128, 256)])
    print(f"App icons: {args.output}")


if __name__ == "__main__":
    main()
