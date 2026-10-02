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

Gameplay field offsets and legal ranges need separate verification before
editing is enabled. A valid outer checksum alone does not prove a gameplay
edit is safe or that the game accepts it.

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
