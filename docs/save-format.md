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

`core/Data/items.json` contains 364 base/DLC convoy definitions with names in
all nine supported languages. `tools/import_item_catalog.py` uses the pinned
complete [Item table](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/Item.xml).
The importer includes convoy kinds 1–10 and excludes quantity-based items and
entries marked chapter-only, enemy-only, Engage-only, unpublished or not
entrustable. Forging ranges come from refinement rows, not a universal +5.

Edits accept `1..MaxUses` for finite-use items, the unlimited-use sentinel `255`
for weapons, and `0..MaxRefine` for each known item. Unknown existing items can be
read, copied or explicitly deleted/replaced; their maximum values are not guessed.
Batch restoration skips unknown items and unlimited-use weapons. Selected-item
restoration rejects an empty or unknown slot. Existing unusual values are readable
without mutation, but new out-of-range edits are rejected.

Item replacement preserves the slot's flags and engraving reference; adding an
item initializes these fields to zero and no engraving unless explicitly assigned.
An engraved weapon cannot be replaced by an ineligible item without explicitly
clearing its engraving. Save Copy applies pending valid item inputs even after
switching pages. Tests cover empty/unknown/engraved entries, 999-slot capacity,
invalid ranges and encodings, exact reversal, untouched other sections and live
language switching. In-game loading remains unverified.

### Quantity-based items (USER version 20)

Seals, ingredients, gifts and materials are not UnitItems in TRAN. They are
typed integer variables named `G_所持_<IID>` in USER. The same importer produces
`core/Data/quantity-items.json`: 105 entries comprising four reclass items
(including DLC Mystic Satchel and Mage Cannon), 17 materials, 33 ingredients,
45 gifts and six key items/fishing rods. These definitions are excluded from
convoy and carried-item replacement selectors. Existing raw entries are never
deleted merely because their ID is not in an editable catalog.

The supplied version-304 executable (build prefix `8C08B971`) verifies
`ItemData.GetMaxInventory` at `0x27b1a10`: kinds 14–16 have a maximum of 9999;
other quantity items have a maximum of 999. `SetInventory` at `0x27b1a30` clamps
to those bounds before writing the game variable. Editor writes instead reject
invalid values without mutation. Absent variables read as zero; nonzero edits
insert one correctly typed record and update the variable block/section sizes,
later section offsets and CRC. Duplicate/noninteger variables are rejected.
Existing positive out-of-range cheat values are preserved on read/copy and when
editing another item. A quantity edit does not grant associated chapter/event
flags or change recruitment, Pact Ring partner data, outfits or gold/fragments.

The GUI puts Convoy, Reclass Items, Materials, Ingredients, Gifts and Key Items
tabs above both cards, with separate convoy/quantity editors. Quantity tabs have
search, saved item name and amount input, not durability/refinement/engraving
fields. All definitions remain visible at zero. Iron/steel/silver inputs synchronize with Main without discarding
unrelated pending inputs. Save Copy applies pending edits from either item editor.
Fill All sets every item in the selected category to its native maximum,
regardless of search. Key Items is excluded from bulk filling; no chapter/event
flags are changed. Emblems uses the same page-level tab layout for Bonds and
Bond Rings.
CLI parity is provided by `items quantity-catalog`, `items quantities` (both
accept `--language <code>`), `items quantity-set --item <IID> --amount <value>`,
and `items quantity-fill --category <ReclassItems|Materials|Ingredients|Gifts>`.
Tests cover every item boundary, missing-variable insertion, exact target-only
changes/reversal, untouched TRAN/UNIT and shared-field synchronization.

### Weapon engravings

The pinned complete [God table](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/God.xml)
provides the 20 playable base/DLC engraving identities and six modifiers. Save
references are GID hashes, not engraving-name message IDs. Dimitri and Claude's
alternate references share Edelgard's engraving identity; they are not extra
independent engravings. Names reuse the localized Emblem catalog.

Weapon eligibility comes from the same revision's [Item table](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/Item.xml):
kinds 1–6, 8 and 9, excluding Engage weapons and the no-engraving flag
`0x08000000`. This yields 299 weapon IDs, including DLC. The supplied executable's
`UnitItem.CanEngrave` / `SetEngrave` at RVAs `0x1fb0050` / `0x1fb0080` check the
weapon predicate and this exclusion flag. The native serializer at `0x1fb2220`
writes the GodData engraving reference after the other UnitItem fields.

Assigning an engraving first validates both TRAN and UNIT, then clears the same
engraving from other owned convoy/carried weapons, excluding Enemy and Temporary
forces. Shared aliases count as the same engraving. A missing or unsupported
ownership section rejects assignment instead of risking duplicates. Clearing a
selected engraving needs only its own section. An unknown engraving is visible
and preserved unless the user explicitly clears or replaces it. Engraving-only
edits preserve raw uses, refinement and flags, including existing unusual values
on newly supported DLC weapons. No unverified limits are fabricated.
Length-changing references update section offsets, Unit lengths and CRC32.
Tests cover transfers in all directions, CLI/GUI behavior, pending edits across
pages and language switches, and exact clear/restore on real equipped weapons.

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

`tools/import_roster_catalog.py` generates minimal facts and nine-language
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

## Emblem bonds and ring stock

`GDBD` pool version 2 has a 32-byte header and a 32-bit holder count.
Each holder saves its instance ID, UTF-16 GID, an optional version-0 Pact Ring
partner record, and a 16-bit character-bond count. A bond saves a UTF-16 PID,
version 3, byte level, 16-bit cumulative EXP, inherited-skill version 2 with
32-bit count/hash entries, then one byte of bond-conversation notification flags.
This is verified against `GodBond.OnSerialize` at `0x2b4ed10` and
`GodBondHolder.OnSerialize` at `0x2b4fe30` in the supplied version-304 executable.

Normal levels 1–20 use EXP thresholds
`0,11,22,33,44,55,66,77,88,99,109,120,131,142,153,164,175,186,197,208`.
Level/EXP edits must agree. Native `GodBond.SetLevel` at `0x2b4d760` sets
conversation bits 2/4/8 at levels 5/10/20 and unlocks a defined global level-cap
variable above 10. The editor follows those flags and preserves other bits and
already purchased inherited skills. Unlike the native debug setter, it does not
clear the purchased-skill dictionary. A level-21 Pact Ring bond is preserved and
cannot be replaced by a normal level-20 edit. Only the existing Alear Pact partner
can reach 21 / 209 EXP; ordinary bonds reach 20 / 208 EXP. Story ownership and
Pact partners are not invented by editing bond progress.

Alear bonds use support-derived levels `1,5,10,20,21`, rather than the full
normal 1–20 progression. `GodBond.SetLevelFromUnitReliance` at `0x2b4e2b0`
maps None/C/B/A/APlus to those levels. Editing an Alear bond synchronizes the
existing corresponding `UREL` support record; a missing pair is rejected rather
than fabricated. Alear's own record has no self-support pair. `UREL` pool version
1 contains a count and concatenated PID-pair UTF-16 keys, followed by record
version 1, byte rank and signed-byte EXP/score. This matches
`UnitRelianceData.Serialize` at `0x1c5b470` and the pool serializer loop at
`0x1c5b3b0`. Changing rank writes its pair-specific cumulative threshold; score,
other pairs and opaque bytes are preserved. An already matching support retains
its saved points. Earlier notes described the EXP as partial and reset it to zero;
that interpretation was corrected after checking the native level-up function.

Single and batch maximum actions share the same character/Emblem limit resolver.
Batch maximum applies to all existing known character bonds for the chosen Emblem,
not just search results. Unknown characters and unrecognized special levels are
preserved. Changes are returned as one immutable result so a failed relationship
validation cannot partially apply a batch. GUI controls use levels by default,
with a linked EXP input directly below the level selector; Alear EXP is read-only.

For the 12 base Emblems, cap-variable IDs come from the pinned `God.xml` table.
Setting a bond above 10 sets that integer to 1, adding the typed variable if absent
and resizing the USER block/section/index with CRC32 recomputation. Alear has no
cap variable; DLC bonds are edited without fabricating paralogue-completion keys.

`RING` pool version 3 saves a 32-bit count and fixed 11-byte records: instance ID,
nullable hash-reference encoding (occupied entries require a hash), and byte stock.
`UnitRing.OnSerialize` is at `0x1c5c9d0`; the stock setter at `0x1c5c8b0` limits
unowned common rings to 0–99 and refuses stock edits on owned rings. An equipped
instance is therefore constrained to exactly one. `IsSingleRank` affects rank
variants, not the common-ring stock limit. Pool capacity is 750 records, not 750
total copies. Existing stock edits preserve pool membership.

The S-ring fill and meld operations can resize the ordinary ring pool. Native
`SingletonPool.OnDeserialize` at `0x3207330` reconstructs its free-ID queue from
unused IDs 1 through capacity; it does not save a next-ID counter in the header.
New records use the lowest unused ID in 1–750. Existing IDs and the 32-byte pool
header are preserved, with limits of 700 unequipped and 50 equipped instances.
Native `UnitRingPool.Add` at `0x1c5d420` caps total copies of one ring definition
at 99, including equipped copies. Both new stock and edits to existing stacks
follow this aggregate limit. The maximum for an unequipped stack is 99 minus
all other copies of the same ring hash. An unchanged pre-existing excess is
preserved, not silently normalized when opening or copying a save.

Fill S creates one missing copy of each of the 123 named S rings (120 normal
families and the three Heroes bonus rings). Existing positive stock, including
worn rings, counts as owned; a zero-stock unequipped row is reused. Other ranks,
unknown entries, GDBD/GOD and UNIT equipment links are not altered. It is a direct
editor action and does not consume fragments or simulate gacha/story unlocks.

Melding consumes only unequipped copies of the selected character and rank,
creates one next-rank ring, and deducts spendable fragments. The pinned complete
`Params.xml` table specifies 2 C + 100 fragments → B, 3 B + 1,000 → A and
4 A + 10,000 → S. S/single-rank/unknown rings cannot be melded. Native subtraction
at `0x1c5d5f0` removes fully consumed rows; remaining stacks retain their IDs.
Acquisition mirrors `RingData.SetProcurement` at `0x2425940`: OR the rank bit
into the integer USER variable `G_指輪_<group>`. `RingData.OnBuild` derives the
group by removing the `RNID_` prefix and the two-character rank suffix. Other
rank bits are preserved. No achievements, global gallery or story flags are
fabricated. Invalid material counts, fragments, capacity, links or variable
schemas fail atomically; source save bytes remain unchanged.

`tools/import_bond_ring_rules.py` imports the three melding costs from
Xzonn/FireEmblemEngageData revision `86b8be7b9820e1bb3bce87d2a9a805ead85d92ab`.

Owners are resolved from the UNIT trailer after AI/customization: two bytes,
battle-data version 4 and sparse count/pairs, two bytes, weapon-rank count/bytes,
two bytes, and three 32-bit instance links (Emblem, partner Emblem, bond ring).
The parser verifies the rest of the variable-length trailer and rejects duplicate
ring owners or references to missing ring instances.

### Roster equipment

The owned GOD pool has version 8: a 32-byte header, 32-bit count, and records
containing an instance ID, GodData hash reference, GDBD holder ID, four bytes
(darkness, reserved deletion, escaping and cleaning progress), a byte-counted
synchro dictionary (UTF-16 key plus UInt16 value), and a byte-counted refinement
dictionary (UTF-16 weapon key, ten level bytes and a nullable skill key).
Native `GodUnit.OnSerialize` at `0x2345420` does not serialize parent/child unit
ownership. UNIT instance links reconstruct ownership; changing normal equipment
therefore preserves GOD and GDBD bytes.

Equipment choices include only owned, known normal/DLC Emblems with a matching
GDBD holder and a bond record for the character. Dark, reserved, escaping,
partner-linked and Alear's special Engage+ Emblem are excluded. Enemy/temporary
owners and active Engage+ links are protected. A normal Emblem and a Bond Ring
are mutually exclusive; a transfer clears the previous owner's normal link.

Bond Ring assignment follows native `UnitRingPool.SetOwner` at `0x1c5d760`: a
single-copy stock entry retains its ID; larger stacks separate one worn copy
using a free native ID. Unequipping follows `ClearOwner` at `0x1c5d8a0`, returning
the worn copy to an unworn stack or retaining it as the only stock entry.
Stock totals by ring hash must remain unchanged. Capacity, instance IDs,
ownership and stock limits are validated before exposing an immutable result.

Native `Unit.SetRingImpl` at `0x1a4e000` and `Unit.SetGodUnit` at `0x1a4f180`
clear Engage count/turn and status mask `0x07800080` (Engage and Dual Guard state).
The editor applies the same reset only to characters whose equipment changes;
partner links and all other unit fields are preserved. GUI/CLI regression tests
cover synthetic pools and temporary copies of both private saves. In-game loading
of edited equipment has not yet been verified. Addresses refer to the supplied
game-version-304 NSO with build prefix `8C08B971`.

### Adding missing Emblems

The add operation supports 12 normal base-game Emblems and all 7 DLC Bracelets,
not Alear's special Engage+ record. Ownership is checked in `GOD`, not inferred
from the surviving `GDBD` bond holder. Existing dark, reserved or escaping records
are not duplicated or reactivated. Existing holder bytes, purchased skills and
Pact associations are retained exactly. When a holder is absent, saved known
playable units in Player/Absent/Dead/Lost forces receive level 1, EXP 0, an empty
version-2 inherited-skill set and zero conversation flags. Enemy/Ally/Temporary
units are not added. Native holder initialization visits those same four forces.

Native `GodPool.Create` at `0x23349c0` calls `GodUnit.Build` at `0x2334b50`, which
reuses or creates the bond holder, clears temporary state and initializes Engage
weapon refinement. `GodBond.Build` at `0x2b4d040` confirms the initial bond values.
`GodUnit.InitGodWeaponRefine` at `0x233e890` and its constructor at `0x2343a70`
initialize each weapon with capacity 0, nine refinement levels of 1 and no skill.
The weapon list includes all nine style-specific fields of the growth table,
matching `GetGodWeaponList` at `0x2343620`. Tiki has seven entries and Byleth ten,
rather than the usual three.

The `GOD` constructor at `0x2334500` allocates 128 instances; the `GDBD` constructor
at `0x2b50f00` allocates 64 holders, each with a 48-bond pool. New GOD and GDBD IDs
are independently allocated from their free native ranges and linked explicitly.
Malformed ownership, duplicate hashes, dangling links and exhausted pools reject
the operation. Existing pool records are retained byte-for-byte; only the required
pool counts and appended records change. Section sizes, index offsets and CRC32
are rebuilt. No UNIT equipment, USER resources, level-cap or story flags change.
The native procurement call goes to runtime recording at `0x2718eb0`; it is not a
story-unlock variable and is not synthesized here.

`tools/import_emblem_creation.py` derives the minimal weapon list from the complete
base/DLC `God.xml` at FireEmblemEngageData revision
`86b8be7b9820e1bb3bce87d2a9a805ead85d92ab`. Synthetic tests cover every supported
Emblem, reuse/new-holder paths, equipment, capacity and no-op handling. Independent
CLI decoding verifies flags, references, weapon initialization, preserved sections
and edits of temporary copies of the supplied saves. Gameplay after acquisition
has not yet been tested.

`tools/import_emblem_catalog.py` imports minimal identifiers, names and thresholds
from FE17-DOC revision `99677e4cad22b636bee4af5a3052003bed17c443` and Iron19_L10n
revision `810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b`. It includes all 20 saved main/DLC
Emblems and 483 named common rings. Four untranslated color/debug table entries
are not editable catalog entries; unknown saved records remain visible by hash.
Both supplied saves parse completely: 820 bonds and 374 rings each, with 41 unit
links in the manual save and 52 in the automatic save (including non-roster units).
Each has 231 character-support records, all at rank A, including all 40 Alear/ally
pairs; the Alear Pact-partner field is empty. These private inputs are not bundled.
Tests use synthetic fixtures and temporary copies; in-game loading is not yet verified.

## Character supports

The pinned [complete support table](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/Reliance.xml)
contains 41 playable characters and 231 base/DLC pairs. A nonzero `ExpType` selects
one of the table's C/B/A cumulative threshold patterns; zero denotes no support.
Only those pairs are editable, and only when a corresponding UREL record already
exists. Either ordering of a PID-pair key resolves to the same catalog pair;
duplicate/reversed duplicates, unsupported versions and trailing data are rejected.

Rank is saved separately as None/C/B/A/APlus (0–4). The native `CanLevelUp` at
`0x1c5a8a0` checks points against the next threshold minus one. `LevelUp` at
`0x1c5aa20` increments both rank and points by one, rather than clearing points.
The setter at `0x1c5c4d0` clamps points to 0 through the next rank's threshold minus
one; A and APlus use the game's 100-point boundary (0–99). The native APlus setter
at `0x1c5af50` writes rank 4 and 99 points. Rank selection uses the corresponding
C/B/A threshold, or 99 for the existing Pact partner. Point-only selection maps
through those thresholds but does not convert ordinary pairs to APlus.

Existing unlocked ranks with low points are valid to preserve: opening, copying
and maximizing an already-maxed pair never infer a lower rank or rewrite points.
The user's Manual0 and Auto contain all 231 pairs at A, with differing saved points.
Support edits preserve the map score byte and every unrelated section. Changing
Alear's rank updates an existing Alear Emblem bond to 1/5/10/20/21, while preserving
purchased skills and unrelated flags; missing Emblems/bonds are not fabricated.
Pact changes are restricted to the saved GDBD partner and never create or replace
that partner. All-rank maximum returns an immutable result and leaves unknown
pairs and unmatched existing special ranks unchanged. The global conversation
gallery is a separate record and is not modified by this panel.

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
