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

The Main and convoy fields below have separate typed-layout tests. Other gameplay
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
equipped unit items. Tests cover empty/unknown/engraved entries, 999-slot capacity,
invalid ranges and encodings, exact reversal, untouched other sections and live
language switching. In-game loading remains unverified.

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
