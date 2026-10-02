# Fire Emblem Engage Save Editor

A desktop save editor and CLI for Fire Emblem Engage, under development.
There is no release yet. Main, Items and Roster are editable; other gameplay panels
are still under development.

## Features

- Main panel: edit money, bond fragments, iron/steel/silver ingots, difficulty,
  game mode and Sommie's name. Difficulty and mode update both the save summary
  and the actual gameplay fields.
- Validated resource ranges: money and bond fragments up to 9,999,999;
  each ingot type up to 9,999. GUI and CLI edits share the same validation.
- Items panel: search the convoy, replace/add/delete individual items, edit
  remaining staff/item uses and weapon refinement, or restore all remaining uses.
  Weapons have unlimited uses, not FETH-style durability. Ranges follow each
  item's data; additions use empty slots within the saved convoy capacity.
  Existing engravings and flags are preserved. Changing engravings is not yet
  supported; unknown item references remain visible and are never discarded.
- Save a verified edited copy; opening or editing never overwrites the input file.
- Roster panel: search characters, edit level, EXP and SP, edit unenhanced stats
  within current-class and personal caps, and replace/add/delete carried items
  or restore their remaining uses. Item flags and engravings are preserved.
  Level limits follow the current class (20 or 40); EXP is 0–99, or 0 at maximum
  level, and SP is 0–9,999. Changing level does not simulate growth rolls.
  Reclass through a class dropdown with character/gender restrictions and weapon
  branches, including DLC classes. Reclassing resets level/EXP and the old class
  skill using the game's rules; switching only a weapon branch retains progress. Engage weapons
  and reserved Engage slots are displayed but not replaced/deleted as normal items.
- Desktop application using SukiUI controls.
- Sommie application icon.
- English UI by default, with live switching to Simplified Chinese, including
  character, class and item names. The character catalog includes all 41 playable
  characters, including DLC. Names fall back to English, or a visible hash for
  unrecognized records, rather than showing blank entries.
- Save inspector under Tools, with section search and file information.
- CLI with a shared save-processing core.
- Read game saves (`Manual0`, `Auto`) and global saves (`Global`), validate
  CRC32 and section boundaries, and inspect their sections.
- Create a verified, byte-identical copy without overwriting existing files.

```sh
dotnet run --project cli/Cli.csproj -- inspect /path/to/Manual0 --json
dotnet run --project cli/Cli.csproj -- copy /path/to/Manual0 /path/to/Manual0-copy
dotnet run --project cli/Cli.csproj -- main show /path/to/Manual0 --json
dotnet run --project cli/Cli.csproj -- main set /path/to/Manual0 /path/to/Manual0-edited --money 5000 --iron 100
dotnet run --project cli/Cli.csproj -- items list /path/to/Manual0 --json
dotnet run --project cli/Cli.csproj -- items catalog --json
dotnet run --project cli/Cli.csproj -- items set /path/to/Manual0 /path/to/Manual0-edited --slot 0 --uses 1
dotnet run --project cli/Cli.csproj -- items restore /path/to/Manual0 /path/to/Manual0-restored --all
dotnet run --project cli/Cli.csproj -- roster list /path/to/Manual0 --json --language en
dotnet run --project cli/Cli.csproj -- roster set /path/to/Manual0 /path/to/Manual0-edited --character 0 --sp 9999
dotnet run --project cli/Cli.csproj -- roster stat /path/to/Manual0 /path/to/Manual0-edited --character 0 --stat Strength --value 30
dotnet run --project cli/Cli.csproj -- roster restore /path/to/Manual0 /path/to/Manual0-restored --character 0
```

The Items CLI also supports `add --item <IID>`, `delete --slot <index>`,
and `set --item <IID> --refine <level>`. Obtain item IDs with `items catalog`;
slot indices from `items list` are zero-based. Every edit writes a new file.

Roster commands use the zero-based character index from `roster list` and the
saved item slot from that character's `Items`. `roster item-set` accepts
`--character`, `--slot`, `--item`, `--uses` and `--refine`;
`roster item-delete` accepts `--character` and `--slot`.
Use `--language zh-Hans` to inspect translated character/class/equipment names.
`roster class` accepts `--character`, `--class <JID>` and an optional
`--weapons Sword,Lance` branch. Level, EXP, learned class skill and internal level
follow the same reclassing rules as the GUI.

The container reader has been checked against game-format version 9 saves.
Main, convoy and roster edits have been checked through serialization and exact restoration on
manual and automatic saves. Modified saves have not yet been verified by loading
them in the game. Test fixtures committed to the repository contain synthetic data only.
See [save format notes](docs/save-format.md) for the verified container layout
and the limits of the current implementation.

The minimal item catalog is generated from a pinned
[FE17-DOC revision](https://github.com/laqieer/FE17-DOC/tree/99677e4cad22b636bee4af5a3052003bed17c443),
not a bundled game resource dump. This catalog is not claimed to cover every DLC
item or future game-data revision. An unrecognized item is shown with its hash
and preserved unchanged unless explicitly deleted or replaced.

## Development

Install the .NET 10 SDK, then run:

```sh
dotnet run --project gui/FeeEditor.Gui.csproj
dotnet run --project cli/Cli.csproj -- --help
```

Run the checks:

```sh
dotnet build FeeEditor.slnx -c Release
dotnet run --project test/Gui.Smoke.csproj -c Release --no-build
python3 tools/test_cli.py --cli cli/bin/Release/net10.0/FeeEditor.Cli.dll
```

## Release

The release process follows FETH Save Editor. Do not create tags or upload
release files manually.

```sh
python3 tools/release.py minor --check
python3 tools/release.py minor
```

The script runs tests, updates `VERSION`, creates `chore: release vX.Y.Z`, and
pushes the commit and tag. GitHub Actions builds Windows, macOS (Apple Silicon
and Intel), and Linux downloads, generates release notes with changelogithub,
and uploads verified archives. The GUI and CLI share one bundled runtime.

## License

[MIT](LICENSE) for original contributions by Jing Haihan. Third-party libraries
and game assets retain their respective rights. No Sommie Editor executable,
decompiled implementation, or private save files are distributed in this repository.
