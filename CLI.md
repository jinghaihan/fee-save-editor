# CLI guide

Run commands with the .NET 10 SDK:

```sh
dotnet run --project cli/Cli.csproj -c Release -- --help
```

In the examples below, replace `FeeEditor.Cli` with that command or the CLI
executable. Every editing command writes a new file and rejects an existing
destination, including the input file. Reading and editing never overwrite it.
Use `--json` for structured output. Catalog and relationship commands also
accept `--language <code>` where shown by `--help`. Supported codes are `en`,
`zh-Hans`, `zh-Hant`, `ja`, `ko`, `de`, `fr`, `es` and `it`.

## Main

```sh
FeeEditor.Cli main show Manual0 --json
FeeEditor.Cli main name Manual0
FeeEditor.Cli main name-set Manual0 Manual0-edited --name Alear
FeeEditor.Cli main activities Manual0 --json
FeeEditor.Cli main activities-set Manual0 Manual0-edited --training-remaining 1 --arena-remaining 3
FeeEditor.Cli main activities-restore Manual0 Manual0-edited
FeeEditor.Cli main set Manual0 Manual0-edited --money 5000 --bond-fragments 10000 --iron 100 --steel 50 --silver 20
FeeEditor.Cli main set Manual0 Manual0-edited --difficulty maddening --mode classic --sommie-name Sommie
FeeEditor.Cli main donation-catalog --json
FeeEditor.Cli main donations Manual0 --json
FeeEditor.Cli main minigames Manual0 --json --language en
FeeEditor.Cli main minigame-set Manual0 Manual0-edited --record G_MusclePushUpBestNormal --value 2000
FeeEditor.Cli main minigame-set Manual0 Manual0-edited --record G_DragonRideNormalScore --value 40000 --rank SSS
FeeEditor.Cli main donation-set Manual0 Manual0-edited --country Firene --level 5
FeeEditor.Cli main donations-max Manual0 Manual0-edited --all
```

Money and bond fragments accept 0–9,999,999; each ingot type accepts 0–9,999.
Activity edits use remaining attempts: strength training accepts 0–1 and standard
Arena training accepts 0–3. `activities-restore` restores both. These edits leave
high scores, temporary stat bonuses, Emblem training and achievement counters unchanged.
Donation levels 1–5 map to cumulative amounts 0, 5,000, 15,000, 40,000 and
90,000. `donation-set` also accepts `--amount` from 0–9,999,999; when both
level and amount are supplied they must agree. `donations-max` accepts
`--country Firene|Brodia|Elusia|Solm` instead of `--all` to change one country.
Donation edits do not spend money or replay rewards.

`minigames` reads 15 strength-training/wyvern high scores and catch counts for
20 fish species, including their saved sizes and size ranks. `minigame-set` uses
the record keys in that output. `--value` edits a high score or catch count;
`--best-size` edits a fishing record in centimeters. `--rank` accepts a saved
rank number or a name from the record's `Ranks` array. Wyvern ranks are
None=0, SSS=1, SS=2, S=3, A=4 through F=9; fish size ranks run from Tiny=0
through Giant=5. Omitted fields are preserved.

Scores, catch counts and sizes accept nonnegative signed 32-bit integers
(0–2,147,483,647). This is the **storage boundary**, not a verified achievable
gameplay maximum. Saved evaluations and scores are separate editable fields;
the editor does not infer a rank from a score or size. These edits do not change
unlocks, temporary stat bonuses, inventory, achievement counters or rewards.

## Items

```sh
FeeEditor.Cli items catalog --json
FeeEditor.Cli items list Manual0 --json
FeeEditor.Cli items set Manual0 Manual0-edited --slot 0 --item IID_鉄の剣 --refine 5
FeeEditor.Cli items add Manual0 Manual0-edited --item IID_鉄の剣
FeeEditor.Cli items delete Manual0 Manual0-edited --slot 0
FeeEditor.Cli items restore Manual0 Manual0-edited --all
FeeEditor.Cli items engravings --json
FeeEditor.Cli items engrave Manual0 Manual0-edited --slot 0 --engraving GID_マルス
FeeEditor.Cli items quantity-catalog --json --language en
FeeEditor.Cli items quantities Manual0 --json --language en
FeeEditor.Cli items quantity-set Manual0 Manual0-edited --item IID_マスタープルフ --amount 999
FeeEditor.Cli items quantity-fill Manual0 Manual0-edited --category ReclassItems
```

Obtain item IDs from `items catalog` and zero-based slot indices from `items list`.
`set` and `add` also accept `--uses` and `--engraving <GID|none>`.
`restore --slot <index>` changes one item. Uses and refinement follow each item's
limits; weapons have unlimited uses. Engravings transfer from their previous
player-owned weapon. Omitting an engraving preserves the existing one.

Quantity items use separate saved counters, not convoy slots. `quantity-catalog`
lists all 105 IDs, categories and individual `Maximum` counts. `quantities`
includes saved counts. Normal counters accept 0–999; iron, steel and silver
ingots accept 0–9,999 and share their values with Main. `quantity-fill` accepts
`ReclassItems`, `Materials`, `Ingredients` or `Gifts` and fills only that category.
Key items remain individually editable; these operations do not unlock chapters
or change story flags. Weapons are managed through the convoy or Roster instead.

## Roster

Export a character's editable data, then import it into the same person and gender in a compatible save:

```sh
FeeEditor.Cli roster export Manual0 Alear.fee-character.json --character 0
FeeEditor.Cli roster import Auto Auto-edited --character 0 --file Alear.fee-character.json
```

Transfers include class/progression, personal stats, inherited skills, proficiencies and carried items. They preserve the destination's recruitment, story and Emblem associations. Character and game versions must match; numeric limits and protected equipment are checked. Engravings already owned by another weapon reject the import. See [character transfer details](docs/roster-transfer.md).

```sh
FeeEditor.Cli roster list Manual0 --json --language en
FeeEditor.Cli roster catalog --json
FeeEditor.Cli roster set Manual0 Manual0-edited --character 0 --level 10 --experience 50 --sp 9999
FeeEditor.Cli roster personal-stat Manual0 Manual0-edited --character 0 --stat Strength --value 41
FeeEditor.Cli roster stats-max Manual0 Manual0-edited --all
FeeEditor.Cli roster class Manual0 Manual0-edited --character 0 --class JID_パラディン --weapons Sword
FeeEditor.Cli roster condition Manual0 Manual0-edited --character 0 --internal-level 20 --hp 30
FeeEditor.Cli roster restore Manual0 Manual0-edited --character 0
FeeEditor.Cli roster restore-character Manual0 Manual0-edited --character 1
FeeEditor.Cli roster missing Manual0 --json
FeeEditor.Cli roster add Manual0 Manual0-edited --person PID_フラン
FeeEditor.Cli roster move Manual0 Manual0-edited --character 1 --force Lost
FeeEditor.Cli roster delete Manual0 Manual0-edited --character 1
FeeEditor.Cli roster class-repair Manual0 Manual0-edited --character 1
FeeEditor.Cli roster classes-repair Manual0 Manual0-edited --all
```

Use character indices from `roster list` and IDs from `roster catalog`.
`restore` refills carried-item uses. `restore-character` returns an existing dead
or lost playable character to the available bench and restores HP. Use a chapter,
Somniel or world-map save outside battle; unknown, guest, summoned, relay and
story-restricted units are excluded. Recruitment and story progress are not edited.
Character indices can change when moving between force pools; run `roster list`
again before another indexed operation. Its `Availability` field distinguishes
normal bench characters from dead or lost units.
`add` creates a missing playable character at level 1 in their starting class,
using difficulty-specific starting personal offsets, initial SP/proficiencies and
full HP. It does not calculate recruitment-level growth, provide equipment or
advance story events. Owned normal Emblems receive any missing level-1 bond record;
existing bonds are preserved. The protagonist and duplicate characters are excluded.
`move` accepts only `Absent`, `Dead` or `Lost` outside battle. It preserves the
character record, including death flags; use `restore-character` to revive a unit.
`delete` rejects the protagonist, Pact partner, deployed/guest/story-restricted
characters and saved target/owner associations. Clear ordinary carried items first.
Equipped Emblems are unlinked and Bond Rings returned to stock; historical bonds
and support records remain. `class-repair` restores an inactive playable unit's
missing/unknown class to its verified starting class, resets its class skill and
checks level, EXP, weapon branch and HP. Valid classes are untouched. The GUI also
performs this repair in memory when opening a compatible non-battle save.
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

Inspect equipment choices, then equip an owned instance or unequip the selected character:

```sh
FeeEditor.Cli roster equipment Manual0 --character 0 --json --language en
FeeEditor.Cli roster equipment-set Manual0 Manual0-edited --character 0 --emblem 1
FeeEditor.Cli roster equipment-set Manual0 Manual0-edited --character 0 --ring 10
FeeEditor.Cli roster equipment-set Manual0 Manual0-edited --character 0 --none true
```

Use the instance IDs returned by `roster equipment`, not catalog IDs. Exactly one
of `--emblem`, `--ring` or `--none` is required. Equipment already worn by another
playable character transfers to the selected character. Equipping a stacked Bond
Ring separates one copy; unequipping returns it to stock. Total copies are preserved.
Unavailable story Emblems and active Engage+ links cannot be reassigned.

## Emblems and Bond Rings

```sh
FeeEditor.Cli emblems catalog --json
FeeEditor.Cli emblems list Manual0 --json
FeeEditor.Cli emblems conditions Manual0 --json
FeeEditor.Cli emblems dirt-set Manual0 Manual0-edited --instance 1 --value 128
FeeEditor.Cli emblems clean Manual0 Manual0-edited --instance 1
FeeEditor.Cli emblems clean-all Manual0 Manual0-edited
FeeEditor.Cli emblems missing Manual0 --json
FeeEditor.Cli emblems add Manual0 Manual0-edited --emblem GID_チキ
FeeEditor.Cli emblems remove Manual0 Manual0-edited --instance 1
FeeEditor.Cli emblems rings Manual0 --json
FeeEditor.Cli emblems bond-set Manual0 Manual0-edited --instance 1 --person PID_ヴァンドレ --level 20
FeeEditor.Cli emblems bond-max Manual0 Manual0-edited --instance 1 --person PID_ヴァンドレ
FeeEditor.Cli emblems bonds-max Manual0 Manual0-edited --instance 1
FeeEditor.Cli emblems ring-set Manual0 Manual0-edited --instance 100 --amount 5
FeeEditor.Cli emblems rings-fill-s Manual0 Manual0-edited
FeeEditor.Cli emblems ring-meld Manual0 Manual0-edited --instance 100
```

Use saved instance IDs from the lists; the example numbers are placeholders.
`conditions` reports owned ring/bracelet instance IDs separately from bond-holder
IDs. Use its `InstanceId` for `dirt-set` and `clean`, not an ID from the bond list.
Dirtiness is 0–255, with 0 clean. `clean-all` cleans every owned, known physical
Emblem Ring and Bracelet without choosing an instance. Cleaning changes only each ring/bracelet's dirty
value; equipment, bonds, and other rings are preserved. Engage+ Alear is excluded.
`catalog` includes each Bond Ring's nonzero `StatBonuses` and localized skill
names/descriptions, including S-rank effects and the three Heroes bonus rings.
`missing` lists absent normal Emblems; `add` takes a catalog GID instead of an
instance ID. All 12 base-game rings and 7 DLC Bracelets are supported. An
already-owned Emblem is a no-op, including reserved or escaping story records.
Existing bonds and purchased skills are preserved. First-time acquisition creates
level-1 bonds with 0 EXP for saved playable units and initializes unrefined Engage
weapons. The new ring is unequipped; equip it through Roster. Adding a DLC Bracelet
does not install or grant DLC. Engage+ Alear and story-completion flags are not
created. `remove` uses the owned instance ID from `conditions`, requires a
non-battle save, unequips any owner first and preserves all historical bonds and
skills for re-acquisition. Reserved, dark, escaping, unknown and Engage+ Emblems
are excluded.

Bond levels map to the game's EXP thresholds. `bond-set` also accepts
`--experience`; both fields must agree when supplied together. Alear bonds
synchronize existing supports and do not create or replace a Pact partner.

Identical rings of the same rank share a 99-copy limit across all stacks,
including equipped copies. `rings` reports each stack's `MaximumStock`; equipped
instances stay at one. Unchanged existing excess stock is preserved when copying
a save, but new edits must fit the shared limit. `rings-fill-s`
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

The Inspector's Variables tab and these commands edit existing numeric game variables:

```sh
FeeEditor.Cli variables list Manual0 --json
FeeEditor.Cli variables set Manual0 Manual0-edited --key G_所持_IID_てつの晶石 --value 100
```

Values accept signed 32-bit integers (−2,147,483,648–2,147,483,647). This is raw
storage editing, not a guarantee that a value is valid for its game mechanic;
prefer the dedicated panels for known fields. String variables, missing keys,
duplicate keys and unknown block formats are rejected. No variable is inserted
or removed. GUI edits reload the dedicated panels to keep their values in sync.

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
