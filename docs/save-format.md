# Engage save container

These are observations from format-version 9 game saves and a matching global
save. They describe the outer container, not a complete gameplay schema.

## Layout

| Component | Game save | Global save |
| --- | --- | --- |
| Header | 128 bytes | None |
| Section index | Offset `0x80` | Offset `0` |
| First section | Offset `0x104` | Offset `0x84` |

The index consists of the physical bytes `EDNI` followed by 32 little-endian
absolute offsets. Unused entries are zero. The final nonzero entry points to
the `LVRC` footer. Neither signature is a typo: FourCC values are stored in
little-endian order.

A section consists of:

1. A four-byte tag, reversed to obtain the logical name (`RESU` becomes `USER`).
2. A little-endian unsigned size that includes its own four-byte size word,
   but excludes the tag.
3. The payload, whose length is the stored size minus four.

Sections are contiguous. An offset must match the preceding section's end.
The footer is four physical `LVRC` bytes followed by an IEEE CRC32, stored
little-endian. The CRC covers every preceding byte, including `LVRC`, but
excludes the CRC value itself.

## Preservation and verification

The reader keeps the original byte buffer, including unknown header fields and
section payloads. Serialization returns a separate copy of that buffer. Saving
creates a temporary file, flushes it, rereads and validates it, compares it with
the original buffer, then moves it to a previously unused destination path.
Existing files are never overwritten by the current copy command.

Game saves and global saves are recognized separately. The first two game
header words are exposed as format version and raw game version code. The
game version code is not assumed to be a displayable release number.

## Current evidence

Two real game saves (manual and automatic) and one global save pass structural
validation and byte-identical round trips through both the CLI and GUI.
Private saves are not committed. Automated tests construct synthetic game and
global containers, and test truncation, incorrect CRCs, invalid sizes, malformed
indices, destination conflicts, search, and repeated language switching.

The Main, convoy and roster fields below have separate typed-layout tests. Other gameplay
fields remain uneditable. A valid outer checksum alone does not prove that the game
accepts a gameplay edit.

## Main fields (USER version 20)

Main editing currently requires game format 9, USER version 20 and the verified
hashed chapter encoding in the summary. The USER payload starts with a version
word and 28 padding bytes. Its settings follow the user-status word and sequence
byte. The game-variable block is then parsed by its own size, version, record
count, UTF-16LE key lengths and typed integer/string values; values are not
located by searching for substrings or assuming a fixed absolute file offset.

Money follows that variable block. After four counters and a length-prefixed
world-map string come spendable bond fragments, lifetime bond fragments and
Sommie's length-prefixed name. Iron, steel and silver amounts are the integer
variables `G_所持_IID_てつの晶石`, `G_所持_IID_はがねの晶石` and
`G_所持_IID_ぎんの晶石` respectively. Missing, duplicated or wrongly typed
material records prevent Main editing without preventing container inspection.

Difficulty and mode are written both to USER and the summary bytes at `0x17`
and `0x19`. A unique valid IEEE CRC32 prefix in the 128-byte summary identifies
the summary checksum word; unsupported or ambiguous summaries are not edited.
Both the summary checksum and the whole-file checksum are updated. The separate
original-difficulty flag and lifetime bond-fragment total are preserved.

Changing Sommie's name resizes its own string, adjusts the USER size and every
subsequent section-index offset, and preserves unknown USER fields and later
section payloads. Core edits are immutable and are reparsed before being returned.
GUI Save Copy applies pending valid Main inputs automatically; invalid inputs
produce no output. Global saves remain inspection/copy-only.

Resource amounts are limited to the game's ranges: money and spendable bond
fragments are `0..9,999,999`; iron, steel and silver are `0..9,999`. These limits
are shared by the GUI and core writer, so CLI edits cannot bypass them. Validation
rejects out-of-range edits instead of silently clamping values. Existing over-limit
saves can still be inspected and copied byte-for-byte, but the Main form is not
populated with silently truncated values.

The constants are present in
[GameUserData in the published executable dump](https://github.com/laqieer/FE17-DOC/blob/main/il2cpp/dump.cs#L743691).
They were also checked against the supplied executable (build ID
`8c08b9719e085f91847b5e0f935d948800000000000000000000000000000000000`):
gold uses `0x98967f` at `0x250e450`, bond fragments at `0x250ecc0`, and the three
material setters use `0x270f` at `0x250e640`, `0x250e8a0` and `0x250eb00`.
The executable and save files are not distributed with the project.

Serialized strings have a defensive 4096-byte limit; the game's actual
Sommie name-entry limit has not been established. Synthetic tests and local
manual/automatic-save tests cover no-op preservation, byte-diff allowlists,
Unicode name resizing, exact edit reversal, malformed inputs, zero/maximum/over-limit
resource amounts and language switching.
In-game loading of edited saves is still to be tested.

## Convoy (TRAN version 1, UnitItem version 5)

The TRAN payload contains a version word, 28 preserved padding bytes and a
16-bit saved capacity. The supplied game saves have 999 slots. Each slot begins
with Transporter.Data version `1`, UnitItem version `5` and a one-byte presence
flag. Empty slots end there. An occupied slot then contains:

| Field | Encoding |
| --- | --- |
| Item reference | `0xefcd` (UInt16), followed by the UInt32 item-ID hash |
| Remaining uses | Byte |
| Refinement | Byte |
| Flags | UInt32, preserved |
| Engraving reference | `0xccdb` (UInt16) for none, or `0xefcd` + UInt32 hash |

The serialized hash is FNV-1 over UTF-16 code units, starting at `2166136261`:
multiply by `16777619` modulo 2^32, then XOR the next code unit. This is not
FNV-1a or a hash over UTF-8 bytes. Independently observed references include
Recover `0x4e134981`, Boots `0x27ec834e`, Elixir `0x4c74ae9a` and Fensalir
`0xe9d2a0e9`; all 118 occupied slots in both local saves resolve with this encoding.

The matching executable's UnitItem serializer at `0x1fb2220` and deserializer
at `0x1fb2320` confirm this field order. These addresses apply only to the build
ID recorded above. Adding/deleting a slot entry resizes TRAN, relocates all later
index offsets and updates the outer CRC without changing other section payloads.
The saved capacity itself is never changed. One slot represents one item;
remaining uses are not an inventory quantity.

`core/Data/items.json` contains minimal item facts and two-language names, generated
with `tools/import_item_catalog.py` from FE17-DOC commit
`99677e4cad22b636bee4af5a3052003bed17c443`:
[item table](https://github.com/laqieer/FE17-DOC/blob/99677e4cad22b636bee4af5a3052003bed17c443/fe_assets_gamedata/Item.xml)
and [names](https://github.com/laqieer/FE17-DOC/blob/99677e4cad22b636bee4af5a3052003bed17c443/translations/Item.csv).
The importer includes convoy item kinds 1–10 and excludes entries marked
chapter-only, enemy-only, Engage-only, unpublished or not entrustable. Forging
ranges come from the refinement rows rather than assuming every item allows +5.
The catalog is a bounded snapshot, not a claim of complete DLC coverage.

Edits accept `1..MaxUses` for finite-use items, the unlimited-use sentinel `255`
for weapons, and `0..MaxRefine` for each known item. Unknown existing items can be
read, copied or explicitly deleted/replaced; their maximum values are not guessed.
Batch restoration skips unknown items and unlimited-use weapons. Selected-item
restoration rejects an empty or unknown slot. Existing unusual values are readable
without mutation, but new out-of-range edits are rejected.

Item replacement preserves the slot's flags and engraving reference; adding an
item initializes these fields to zero and no engraving. An engraved weapon cannot
be replaced by a non-forgeable item. Engraving changes remain unimplemented pending
ownership checks across convoy and unit equipment. Save Copy applies pending valid
item inputs when the Items panel is active. The inventory editor does not alter
equipped unit items; these are handled by the Roster panel. Tests cover empty/unknown/engraved entries, 999-slot capacity,
invalid ranges and encodings, exact reversal, untouched other sections and live
language switching. In-game loading remains unverified.

## Roster (UNIT version 0, Unit version 40)

The UNIT payload has version `0`, marker `0xcdcdcdcd` and 24 preserved bytes.
It contains ascending force groups, each with a force byte, a count byte and
that many length-prefixed Unit records. Byte `0xff` ends the pool. The supplied
manual save contains 41 characters in the Absent force; the automatic save also
contains active player and enemy groups. No field
is located by guessing a fixed absolute file offset or scanning for a character name.

Each Unit record starts with its length, version `40`, marker and 24 reserved
bytes. Then follow status (UInt64), person/class hashed references, three
version-0 capability blocks (11 bytes each), two UInt32 seeds, byte level/EXP/HP,
four signed position bytes and a float angle. An optional target person uses a
**presence byte**, followed by a hashed reference only when present, then a
force mask. This differs from the two-byte null marker used for item engravings.
The item list is version `2`, with eight version-5 UnitItem records. UnitItem
encoding is shared with the convoy but has no Transporter.Data wrapper.

The following progression blocks are decoded as described below. Private skills,
accessories, enhancements, AI and other actor data remain unchanged by edits.
The final ten bytes contain two indices, Int16 SP,
an owner integer and two signed target coordinates. Variable-length item changes
update the Unit record length, UNIT section size, subsequent section offsets
and the outer checksum. Other characters and sections remain unchanged.

The supplied executable confirms Unit serialization at `0x1a500e0`, UnitPool at
`0x1c556a0` and UnitItemList at `0x1fb89e0`. These addresses apply only to the
build ID documented above. Its unenhanced-stat function at `0x1a30d10` combines
class base + signed saved base, clamped to class cap + person cap modifier.
HP is at least one. Movement editing allows class base + up to two permanent
points, matching `CanCapabilityGrow` at `0x1a5d980`. Sight is not editable.
These fields do not include equipment, Emblem or temporary battle bonuses.
Existing values already above a displayed cap are preserved on inspection/no-op
copy; explicitly changing a stat validates and writes only its signed base.
Reducing HP clamps current HP down but does not heal it.

The Stats GUI edits the signed personal bytes directly and previews the
class-dependent result/cap without changing the stored overflow. Personal edits
use the signed-byte storage range; their minimum keeps the current class's
unenhanced value nonnegative (HP at least one). Movement retains the verified
two-point limit. The CLI's `personal-stat` has the same validation; `stat`
continues to accept a displayed current-class value for compatibility.

Maximum attributes use, for each of the nine HP-through-Build fields,
`max(class limit + personal cap modifier - class base)` across that owner's
available classes and customization gender. This covers generic, exclusive and
DLC classes, including low-tier classes. Each personal value is set to that exact
threshold, including values already above it. Single/batch maximum writes only
those nine bytes per selected known playable unit plus the outer CRC32; level,
EXP, SP, growth accumulators,
current HP, Sight, Movement, carried items and other unit fields are preserved.
Enemy, temporary and unknown units are excluded from batch maximum.

Level/EXP award logic at `0x1a39d40` uses the current job's MaxLevel and resets
EXP at maximum level. New edits enforce level `1..MaxLevel`, EXP `0..99` (zero
at maximum), and SP `0..9999` as in the setter at `0x1a39db0`. Level editing
does not run the game's growth code.

Reclassing follows `Unit.ClassChange` at `0x1a3c7b0`: start at level 1 (21 when
moving from an advanced class, or a level-21+ special class, to a 40-level class),
clear EXP and the learned class skill, and retain base stats and carried items.
The internal-level calculator is `clamp(oldInternal + oldLevel - 1, 0, cap)`;
cap is 30/40/50 for Normal/Hard/Maddening. Subtract `newLevel - 1`, with floor 0.
Current HP is lowered only if the new maximum requires it; reclassing does not
heal. The selected weapon mask follows job weapon codes: 1 mandatory, 2 choose
one, 3 choose two. Required proficiencies are added while existing ones remain.
Switching only the weapon branch does not reset level, EXP or the learned skill.

Class eligibility uses the original vanilla job flags from pinned FE17-DOC, not
the randomizer's modified eligibility flags. Exclusive base/promoted classes are
matched to their character's canonical birth class; Enchanter and Mage Cannoneer
are available as DLC generic classes. The base fliers are female-only; Alear's
gender comes from the save's UnitEdit customization rather than the person table.

`tools/import_roster_catalog.py` generates minimal facts and English/Chinese
names, including DLC, from pinned
[data tables](https://github.com/LordMewtwo73/feEngage-randomizer/tree/8a64328fc9a4df7649852ec2ac8b7d5beaedbc58/assets/VanillaFiles)
and [localized messages](https://github.com/delvier/Iron19_L10n/tree/810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b).
Only the 41 canonical playable person IDs are imported, not custom appended
dragon-form rows. These tables are not claimed to be wholly unmodified vanilla
data: names, bases/caps and MaxLevel are imported, with original vanilla flags
and an explicit DLC class allowlist used for eligibility. Full source dumps are not bundled. Display-only
item names do not expand the verified editable convoy-item catalog.

Engage items (item-data flag 128), including `IID_エンゲージ枠`, occupy entries
throughout the same eight-slot list, not a fixed last-three-slot range.
`PutEngageItem` at `0x1fb84c0` manages them; they are shown but cannot be replaced
or deleted through ordinary carried-item editing. Known finite-use items can be
restored in bulk, while unknown and unlimited-use entries are preserved.

Synthetic fixtures cover optional targets, malformed record lengths and versions,
class-specific limits, personal caps, existing overflow, engraved/unknown items,
byte-exact reversal and relocation. Real manual/automatic copies are tested
without modifying the source. GUI tests include repeated live language switching
and preservation of pending edits across tabs. In-game loading remains unverified.

### Additional character progression blocks

After the eight carried items, unit version 40 contains accessory-list version 0
(four nullable references), ExtraSight, and three skill arrays: equipped, private,
and inherited/purchased. Skill-array version 1 stores a 32-bit count followed by
hash-reference/32-bit-age/32-bit-category entries. The inherited slots use category
11; unrelated private entries and all existing entry metadata are preserved.
An optional learned class-skill reference follows, then three 32-bit weapon masks
(original proficiency, current proficiency, selected class weapons).

EnhanceFactors version 6 contains three capability arrays; EnhanceCalculator
version 1 contains another. Each capability is version 1, count 11, and eleven
32-bit values. The next signed byte is InternalLevel. The parser checks every
version/count and leaves subsequent AI, customization, Emblem and battle state
untouched. This layout was checked against all characters in both supplied manual
and automatic saves, including DLC, optional targets and equipped skills.

Inherited skills use the pinned randomizer's `SkillData.xml` IDs and positive
inheritance SP costs (Inherit/Sync categories), with the original vanilla skill
message keys and localized Patch0–3 messages. The editable catalog contains 277
skills in 94 tier families. Nel's `SID_裏邪竜ノ娘_兵種スキル` is resolved directly from
her job's LearningSkill and `MSID_JobSkill_ShadowPrincessR` in Patch3. Skill IDs
and names, not numeric UI positions, determine edits. Unknown skills remain visible
and are retained. Upgrading a family replaces its earlier tier in the inherited
pool and equipped slots; removing a skill also removes it from those slots.
New inheritance entries use age 0/category 11, consistent with `AddToEquipSkillPool`
at `0x1a36560`; existing entry metadata and private skills are preserved.

Weapon proficiency edits affect only Sword through Arts bits (mask 510); innate
and current-class requirements cannot be removed, while Special and unknown bits
are retained. Current HP is bounded by unenhanced maximum HP plus the serialized
enhancement calculator's HP bonus, up to 255. Internal level is a signed byte;
the editor accepts -100 through 100, matching the native setter's bounds.
Changing it or the displayed level does not simulate growth. Skill and class-skill
changes relocate the containing record/section and preserve subsequent sections.

## Resource inputs

The executable dump is useful for analyzing serialization and game logic.
It is not a replacement for the game data tables and localized text. A complete
RomFS dump is not required for this project.

The [fee-texts extraction script](https://github.com/Asvel/fee-texts/blob/master/scripts/Program.cs)
locates data tables under `Data/StreamingAssets/aa/Switch/fe_assets_gamedata`
and messages under `Data/StreamingAssets/aa/Switch/fe_assets_message`.
Only specific required bundles should be requested as the field mappings are
implemented. Game executables, resource dumps, and private save files must not
be committed.
