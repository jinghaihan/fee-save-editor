# amiibo Collection

The dedicated amiibo page has independent Outfits and Music cards, each with
12 official localized names, an unlocked count, checked/disabled boxes for unlocked
rewards, selectable boxes for locked rewards, and Unlock Selected / Unlock All actions. The File menu saves edits
through the existing verified-copy workflow. All nine UI languages are supported.

The whitelist is explicit in `internal/catalog/data/amiibo.json.gz`: the 12 named
`G_所持_AID_` outfit keys and `G_BGM_Amiibo_03_ST_Play` through
`G_BGM_Amiibo_14_ST_Play`. No prefix or keyword matching is used for writes.
Names are imported from Accessories and MusicName at Iron19_L10n revision
`810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b` by `import_amiibo_catalog.py`.

An absent key reads as locked. Unlocking uses the save section writer to update
an existing integer or append a missing integer with value 1. Already-unlocked
keys are no-ops. Duplicate and noninteger reward keys reject the entire operation.
Variable block lengths/counts, USER length, section index offsets and CRC32 are
rebuilt using the existing section writer. No fixed file offsets are used.

Edits do not set outfit-read flags, add exchange tickets, fabricate scans, change
daily scan counts/timestamps, advance chapters, recruit characters or install DLC.
Other variables, including strings, retain their raw bytes and ordering.

Focused tests cover absent/zero/partial/unlocked collections, single/group/bulk
edits, repeated selections, no-op idempotency, invalid keys/groups, malformed
types/duplicates, serialization/checksums, section and variable byte preservation,
GUI selection/language/navigation/save behavior, and copies of private saves.
These checks do not substitute for in-game verification of the unlocked rewards.
