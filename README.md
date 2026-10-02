# Fire Emblem Engage Save Editor

A desktop save editor and CLI for Fire Emblem Engage, under development.
There is no release yet. Main, Items, Roster, Emblems and Support are editable; other gameplay panels
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
  Assign or clear any of the 20 base-game/DLC Emblem engravings. Assigning an
  already-used engraving transfers it from the previous convoy or carried weapon.
  Preview might, weight, hit, critical, avoid and dodge modifiers before applying.
  Item flags and unrelated engravings are preserved; unknown references remain visible.
- Save a verified edited copy; opening or editing never overwrites the input file.
- Roster panel: search characters, edit level, EXP and SP, edit personal stat
  values with live current-class value/cap previews, and replace/add/delete carried items
  or restore their remaining uses. Edit weapon engravings with the same selector
  and transfer rules as the convoy. Item flags are preserved.
  Level limits follow the current class (20 or 40); EXP is 0–99, or 0 at maximum
  level, and SP is 0–9,999. Changing level does not simulate growth rolls.
  Reclass through a class dropdown with character/gender restrictions and weapon
  branches, including DLC classes. Reclassing resets level/EXP and the old class
  skill using the game's rules; switching only a weapon branch retains progress. Engage weapons
  and reserved Engage slots are displayed but not replaced/deleted as normal items.
  Unlock, upgrade or remove inherited skills, equip two slots, or unlock the
  highest tier of every inheritance skill family, including DLC. Toggle learned
  class skills at their required level. Edit weapon proficiencies without removing
  innate/current-class requirements. Edit internal level and current HP, or restore HP.
  Maximize nine base attributes for one character or every existing playable roster
  character. The required personal values are calculated across each character's
  available classes, including exclusive and DLC classes, so reclassing stays capped.
  Each personal value is set to the exact all-class target, replacing higher values
  as well as raising lower ones. Level, movement, equipment and current HP are not
  changed by maximizing attributes. Batch actions ignore search filters.
- Desktop application using SukiUI controls.
- Emblems panel: select a base-game or DLC Emblem and edit character bond levels
  through a dropdown above a linked EXP input. Either input updates the other
  for ordinary bonds. Maximize one bond or every known character bond for the
  selected Emblem, independently of search filters, with a completion count.
  Alear uses support-derived levels and synchronizes existing support records;
  only the existing Pact partner can reach level 21. Purchased inherited skills,
  equipment and unrelated relationships are preserved. Inspect common bond rings
  and edit stock from 0–99; equipped ring instances remain at one. Unknown rings
  remain visible and unchanged. Equipment reassignment and adding/deleting ring
  records are not yet supported.
- Sommie application icon.
- Support panel: search the 231 base-game/DLC character pairings and edit their
  unlocked rank and saved points using vertically linked controls. Rank choices
  map to each pair's game-data thresholds; changing points selects the corresponding
  ordinary rank. Existing saved ranks are not inferred or downgraded on load, even
  when their points are below a threshold. Maximize one pair or all existing known
  pairs independently of search filters. Preserve an existing Pact partner's A+
  rank without creating or replacing the partner. Alear support-rank changes also
  synchronize existing Alear Emblem bonds. Unknown pairs remain visible and unchanged.
- English UI by default, with live switching to Simplified Chinese, including
  character, class, skill and item names. The character catalog includes all 41 playable
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
dotnet run --project cli/Cli.csproj -- roster personal-stat /path/to/Manual0 /path/to/Manual0-edited --character 0 --stat Strength --value 41
dotnet run --project cli/Cli.csproj -- roster stats-max /path/to/Manual0 /path/to/Manual0-max --all
dotnet run --project cli/Cli.csproj -- roster restore /path/to/Manual0 /path/to/Manual0-restored --character 0
```

The Items CLI also supports `add --item <IID>`, `delete --slot <index>`,
and `set --item <IID> --refine <level>`. Obtain item IDs with `items catalog`;
slot indices from `items list` are zero-based. Every edit writes a new file.

`items engravings --json` lists all 20 engraving IDs, translated names and modifiers.
`items engrave <save> <new-file> --slot <index> --engraving <GID|none>` assigns or
clears an engraving without changing refinement, uses or flags. `items set` and
`items add` also accept `--engraving`. Omitting it preserves an existing engraving.
Assigning an engraving transfers it from any previous player-owned weapon, including
carried weapons; enemy and temporary units are not edited.

Roster commands use the zero-based character index from `roster list` and the
saved item slot from that character's `Items`. `roster item-set` accepts
`--character`, `--slot`, `--item`, `--uses`, `--refine` and `--engraving`;
`roster item-delete` accepts `--character` and `--slot`.
`roster item-engrave <save> <new-file> --character <index> --slot <index>
--engraving <GID|none>` edits only the engraving. Verified DLC weapons missing
from the older item-replacement catalog support engraving-only edits, with their
other fields preserved rather than assigning guessed limits. Staffs and Engage
weapons cannot receive engravings.
Use `--language zh-Hans` to inspect translated character/class/equipment names.
`roster class` accepts `--character`, `--class <JID>` and an optional
`--weapons Sword,Lance` branch. Level, EXP, learned class skill and internal level
follow the same reclassing rules as the GUI.
`roster catalog --json` lists class and skill IDs. `skill-unlock` / `skill-remove`
accept `--character` and `--skill <SID>`; `skills-max` unlocks the highest tier
of each family for one character. `skills-equip` accepts `--first <SID|none>`
and/or `--second <SID|none>`. `class-skill --unlocked true|false` edits the learned
class skill. `proficiencies --weapons Sword,Lance` edits weapon proficiencies;
`condition --internal-level <value> --hp <value>` edits either or both fields.
`personal-stat` edits the signed personal value shown in the GUI; the existing
`stat` command still edits the current-class result. `roster list` includes both
`PersonalValue` and the class-dependent `Value`/`Maximum` for every attribute.
`stats-max --character <index>` maximizes one character; `stats-max --all`
maximizes all existing known playable characters, excluding enemy/temporary units.

`emblems catalog` lists all 20 base/DLC Emblems and 483 named common rings.
`emblems list <save>` includes character bonds, saved EXP, purchased skills and
the character-specific `MaximumLevel`; `emblems rings <save>` includes stock and
equipment ownership. Both accept `--language en|zh-Hans`.
`emblems bond-set <save> <new-file> --instance <id> --person <PID>` accepts
`--level` and/or `--experience`, which must agree with the game thresholds.
`emblems bond-max` uses the same two selectors to maximize one character bond;
`emblems bonds-max --instance <id>` maximizes all known saved character bonds
for that Emblem. Alear edits synchronize existing character supports and cannot
invent or replace a Pact partner. `emblems ring-set` accepts `--instance` and
`--amount` with ownership-aware limits. `emblems rings-fill-s <save> <new-file>`
adds one copy of each missing S-rank Bond Ring, including the three Heroes bonus
rings, without duplicating owned/equipped copies or spending fragments.
`emblems ring-meld <save> <new-file> --instance <id>` melds same-character,
same-rank unequipped copies into one higher-rank ring: 2 C + 100 fragments → B,
3 B + 1,000 → A, or 4 A + 10,000 → S. Equipped copies are never consumed.
`emblems catalog` includes each ring's next rank and material/fragment costs.
Every command writes a new file; Emblem Rings, DLC Bracelets and their equipment
links are not removed or changed by ordinary Bond Ring management.

`supports catalog` lists the legal base/DLC pairs and their C/B/A point thresholds.
`supports list <save> --json` includes the saved rank, points, map score and legal
maximum for each pair; both commands accept `--language en|zh-Hans`.
Use `supports set <save> <new-file> --pair <key> --rank C|B|A|A+|None`
to choose a rank, or `--points <value>` to map points to an ordinary rank.
Providing both edits the two saved fields explicitly, within the game's rank-specific
point limit. `supports max <save> <new-file> --pair <key>` maximizes one pair;
`--all` maximizes every existing verified pair. Already-maxed pairs keep their
saved points. Pact partners cannot be invented, replaced or downgraded. No command
adds missing relationships or edits the separate global conversation gallery.

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
and preserved unchanged unless explicitly deleted or replaced, or its engraving
is edited using the separately verified weapon-eligibility catalog.

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
