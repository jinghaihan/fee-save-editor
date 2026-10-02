# Fire Emblem Engage Save Editor

A desktop save editor and CLI for Fire Emblem Engage, under development.
There is no release yet. Gameplay editing is not implemented yet.

## Features

- Desktop application using the same SukiUI controls as [FETH Save Editor](https://github.com/jinghaihan/feth-save-editor).
- English UI by default, with live switching to Simplified Chinese.
- Two-panel save inspector with section search and file information.
- CLI with a shared save-processing core.
- Read game saves (`Manual0`, `Auto`) and global saves (`Global`), validate
  CRC32 and section boundaries, and inspect their sections.
- Create a verified, byte-identical copy without overwriting existing files.

```sh
dotnet run --project cli/Cli.csproj -- inspect /path/to/Manual0 --json
dotnet run --project cli/Cli.csproj -- copy /path/to/Manual0 /path/to/Manual0-copy
```

The container reader has been checked against game-format version 9 saves.
This does not yet establish support for editing all gameplay fields or for
loading modified saves in the game. Test fixtures contain synthetic data only.
See [save format notes](docs/save-format.md) for the verified container layout
and the limits of the current implementation.

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
