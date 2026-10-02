# CLI guide

Run commands with the .NET 10 SDK:

```sh
dotnet run --project cli/Cli.csproj -c Release -- --help
```

In the examples below, replace `FeeEditor.Cli` with that command or the CLI
executable. Every editing command writes a new file and rejects an existing
destination, including the input file. Reading and editing never overwrite it.
Use `--json` for structured output. Catalog and relationship commands also
accept `--language en|zh-Hans` where shown by `--help`.

## Main

```sh
FeeEditor.Cli main show Manual0 --json
FeeEditor.Cli main set Manual0 Manual0-edited --money 5000 --bond-fragments 10000 --iron 100 --steel 50 --silver 20
FeeEditor.Cli main set Manual0 Manual0-edited --difficulty maddening --mode classic --sommie-name Sommie
FeeEditor.Cli main donation-catalog --json
FeeEditor.Cli main donations Manual0 --json
FeeEditor.Cli main donation-set Manual0 Manual0-edited --country Firene --level 5
FeeEditor.Cli main donations-max Manual0 Manual0-edited --all
```

Money and bond fragments accept 0–9,999,999; each ingot type accepts 0–9,999.
Donation levels 1–5 map to cumulative amounts 0, 5,000, 15,000, 40,000 and
90,000. `donation-set` also accepts `--amount` from 0–9,999,999; when both
level and amount are supplied they must agree. `donations-max` accepts
`--country Firene|Brodia|Elusia|Solm` instead of `--all` to change one country.
Donation edits do not spend money or replay rewards.

## Items

```sh
FeeEditor.Cli items catalog --json
FeeEditor.Cli items list Manual0 --json
FeeEditor.Cli items set Manual0 Manual0-edited --slot 0 --item IID_てつの剣 --refine 5
FeeEditor.Cli items add Manual0 Manual0-edited --item IID_てつの剣
FeeEditor.Cli items delete Manual0 Manual0-edited --slot 0
FeeEditor.Cli items restore Manual0 Manual0-edited --all
FeeEditor.Cli items engravings --json
FeeEditor.Cli items engrave Manual0 Manual0-edited --slot 0 --engraving GID_マルス
```

Obtain item IDs from `items catalog` and zero-based slot indices from `items list`.
`set` and `add` also accept `--uses` and `--engraving <GID|none>`.
`restore --slot <index>` changes one item. Uses and refinement follow each item's
limits; weapons have unlimited uses. Engravings transfer from their previous
player-owned weapon. Omitting an engraving preserves the existing one.

## Roster

```sh
FeeEditor.Cli roster list Manual0 --json --language en
FeeEditor.Cli roster catalog --json
FeeEditor.Cli roster set Manual0 Manual0-edited --character 0 --level 10 --experience 50 --sp 9999
FeeEditor.Cli roster personal-stat Manual0 Manual0-edited --character 0 --stat Strength --value 41
FeeEditor.Cli roster stats-max Manual0 Manual0-edited --all
FeeEditor.Cli roster class Manual0 Manual0-edited --character 0 --class JID_パラディン --weapons Sword
FeeEditor.Cli roster condition Manual0 Manual0-edited --character 0 --internal-level 20 --hp 30
FeeEditor.Cli roster restore Manual0 Manual0-edited --character 0
```

Use character indices from `roster list` and IDs from `roster catalog`.
`personal-stat` edits the stored personal value; `stat` edits the current-class
result. `stats-max --character <index>` targets one character; `--all` targets
existing playable characters. Targets are calculated across each character's
available classes and replace values above or below that target. Level, movement,
equipment and HP are not changed. Reclassing follows character/gender restrictions
and resets class progress; changing only a weapon branch retains it.

- Carried items: `item-set --character <index> --slot <index>` accepts `--item`,
  `--uses`, `--refine` and `--engraving`; `item-delete` removes the selected normal
  item. `item-engrave` changes only its engraving. Engage slots cannot be replaced
  or deleted as ordinary items.
- Skills: `skill-unlock` / `skill-remove` accept `--character` and `--skill <SID>`.
  `skills-max --character <index>` unlocks the highest tier of each family.
  `skills-equip` accepts `--first <SID|none>` and/or `--second <SID|none>`.
- Class skill: `class-skill --character <index> --unlocked true|false`.
- Proficiencies: `proficiencies --character <index> --weapons Sword,Lance`.

## Emblems and Bond Rings

```sh
FeeEditor.Cli emblems catalog --json
FeeEditor.Cli emblems list Manual0 --json
FeeEditor.Cli emblems rings Manual0 --json
FeeEditor.Cli emblems bond-set Manual0 Manual0-edited --instance 1 --person PID_ヴァンドレ --level 20
FeeEditor.Cli emblems bond-max Manual0 Manual0-edited --instance 1 --person PID_ヴァンドレ
FeeEditor.Cli emblems bonds-max Manual0 Manual0-edited --instance 1
FeeEditor.Cli emblems ring-set Manual0 Manual0-edited --instance 100 --amount 5
FeeEditor.Cli emblems rings-fill-s Manual0 Manual0-edited
FeeEditor.Cli emblems ring-meld Manual0 Manual0-edited --instance 100
```

Use saved instance IDs from the lists; the example numbers are placeholders.
Bond levels map to the game's EXP thresholds. `bond-set` also accepts
`--experience`; both fields must agree when supplied together. Alear bonds
synchronize existing supports and do not create or replace a Pact partner.

Ring stock accepts 0–99, but equipped instances stay at one. `rings-fill-s`
adds one missing copy of each of the 123 S-rank rings without duplicating owned
or equipped copies. Melding consumes only unequipped duplicates of the same
character and rank: 2 C + 100 fragments → B, 3 B + 1,000 → A, or 4 A + 10,000 → S.
Emblem Rings and DLC Bracelets are not removed by Bond Ring management.

## Support

```sh
FeeEditor.Cli supports catalog --json
FeeEditor.Cli supports list Manual0 --json
FeeEditor.Cli supports set Manual0 Manual0-edited --pair <key> --rank A
FeeEditor.Cli supports set Manual0 Manual0-edited --pair <key> --points 50
FeeEditor.Cli supports max Manual0 Manual0-edited --pair <key>
FeeEditor.Cli supports max Manual0 Manual0-edited --all
```

Use pair keys from the catalog or list. Rank choices are None, C, B and A;
an existing Pact partner also supports A+. Points map to each pair's ordinary
rank thresholds; providing both fields edits them explicitly within the game's
rank-specific limit. Maximum actions keep already-maxed pairs unchanged and
do not add missing relationships or edit the global conversation gallery.

## Achievements

```sh
FeeEditor.Cli achievements catalog --json --language en
FeeEditor.Cli achievements list Manual0 --json
FeeEditor.Cli achievements unlock Manual0 Manual0-edited --achievement <AID>
FeeEditor.Cli achievements unlock Manual0 Manual0-edited --all
```

The catalog contains 765 named achievements, excluding internal play-report
statistics. The list exposes saved status, reward availability and claimed state.
Unlocking changes only unfulfilled achievements to `Cleared` (reward available).
Existing `Cleared`, `Showed` and `Completed` states remain unchanged. Collect the
rewards in-game; the editor does not award fragments, reset claimed rewards,
change activity counters or advance the story. `--all` includes all named
achievements, including chapter-gated ones, not just a GUI search/category filter.

## Inspect and copy

```sh
FeeEditor.Cli inspect Manual0 --json
FeeEditor.Cli copy Manual0 Manual0-copy
FeeEditor.Cli --version
```

Both game saves and `Global` saves support inspection and lossless copies.

## Development and release

```sh
dotnet build FeeEditor.slnx -c Release
dotnet run --project test/Gui.Smoke.csproj -c Release --no-build
python3 tools/test_cli.py --cli cli/bin/Release/net10.0/FeeEditor.Cli.dll
python3 tools/release.py minor --check
python3 tools/release.py minor
```

The release script runs tests, updates `VERSION`, creates `chore: release vX.Y.Z`
and pushes the commit and tag. GitHub Actions builds and packages the downloads
and generates release notes. Do not create tags or upload release files manually.
