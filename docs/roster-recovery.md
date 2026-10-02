# Character restoration

Roster shows **Available**, **Dead** or **Lost** for existing playable characters.
**Restore Character** clears the saved death flags, restores HP and returns the
selected character to the available bench. It does not spawn a unit on a map or
recruit a character missing from the save. Save through **File → Save Copy**.

Restoration requires a format-version 9 game save with USER version 20, an
out-of-battle ChapterSave, Hub or Gmap sequence (1, 4 or 6), no initialized battle
map, and no Player/Enemy/Ally force pool. Guest, disposition-guest, summoned, relay
and NeverSortie units are protected. Unknown or duplicate person records are
rejected. The CLI provides the same operation as `roster restore-character`.

## Verified serialization

In the supplied 1.3.0 executable, `Unit.Serialize` at `0x1a500e0` writes the status
as a 64-bit field at record offset 36. `Unit.IsDead` at `0x1a23050` tests bit
`0x200`; SetDead also sets DiedHere (`0x800`) and, when applicable, ExistDead
(`0x10000000`). The metadata's DeadMask is their union, `0x10000a00`.
The native `CanSortie` at `0x1a23c80` separately checks NeverSortie (`0x8`);
restoration leaves that story restriction intact and rejects such records.

`UnitPool.Serialize` at `0x1c556a0` writes increasing force/count groups and a 255
terminator. Player/Enemy/Ally records include AI; Absent/Dead/Lost records do not.
Absent (3) is the normal available bench, not a missing character. Dead (4) and
Lost (5) records can therefore move to Absent without inventing an AI block.
Empty groups are omitted, lengths and the section index are rebuilt, and the
save CRC is recalculated. A death flag already in Absent is cleared in place.

Only group membership, DeadMask bits and current HP are changed. Character
identity, class, stats, growth data, skills, carried items, accessories, custom
name, historical battle/death records, Emblem/Bond Ring links and unknown fields
remain intact. All other sections, including USER, UREL, GDBD, GOD and RING, remain
byte-identical. Moving groups can change the editor's character indices; GUI
selection follows person identity and CLI callers must list the roster again.

Tests cover synthetic Dead/Lost records, a modified temporary copy of a real
character record, battle-save rejection, byte preservation, HP, equipment,
selection, pending edits, translations and GUI/CLI copies. Original private saves
are never modified. Console gameplay after restoration has not been tested;
story scripts and prior death-history entries are not rewritten by this action.
