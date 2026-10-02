# Minigame record editing

## Verified fields

The USER game-variable dictionary stores all exposed values as signed 32-bit
integers. The catalog contains 12 strength-training scores, three wyvern scores
and 20 fish catch counts. Wyvern records also store `RankNum`; fishing records
also store `BestSize` and `BestRank`: 78 integer fields in total.

- Training: `G_Muscle{PushUp|SitUp|Squat}Best{Normal|Hard|Master|Eternal}`.
- Wyvern: `G_DragonRide{Normal|Hard|Expert}{Score|RankNum}`.
- Fishing: `G_Fishing_<FishName>_{Count|BestSize|BestRank}`. The internal
  Japanese fish identifier is kept in the file; displayed names are localized.

The game metadata declares `App.DragonRide.Ranks` as NoRecord=0, SSS=1, SS=2,
S=3, A=4, B=5, C=6, D=7, E=8 and F=9. `App.Fishing.SizeRank` is Tiny=0,
Small=1, Middle=2, Large=3, Big=4 and Giant=5. In the provided executable
(build ID prefix `8C08B9719E085F91847B5E0F935D9488`), the fishing picture-item
constructor at `0x2602E80` sets its crown flag when the stored best rank exceeds
4 and its unknown flag when the catch count is absent or below 1.

## Editing boundaries

The editor validates integer storage bounds and the two rank enumerations.
It does not claim that the signed integer maximum is attainable during play.
No artificial "max score" action is provided. Saved rank and score/size are
edited independently; no guessed threshold mapping is applied.

Edits preserve omitted fields, unknown variables, session play counts, unlock
flags, temporary stat bonuses, achievements, inventory and prize flags. Missing
nonzero values are inserted through the common game-variable writer, updating
the dictionary length, USER section length, section offsets and CRC. Missing
zero values are left absent, so viewing and copying an unchanged save remains
byte-identical.

Tests cover all 78 fields, malformed associated variables, enum bounds, integer
overflow, selection/language changes and atomic rejection of invalid edits.
On the supplied saves, tests compare every changed byte against the requested
integer fields and CRC, then restore all records and compare the complete file.
These are file-format and GUI/CLI tests, not a console gameplay validation.

## Catalog sources

Fish identifiers and names come from pinned sources:

- [FishingFishData.xml](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/FishingFishData.xml).
- [English Hub messages](https://github.com/delvier/Iron19_L10n/blob/810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b/US/USen/Hub.txt).
- [Simplified Chinese Hub messages](https://github.com/delvier/Iron19_L10n/blob/810fc6d5336e2caf6e434cc6dc316e8ceac5dc7b/CN/CNch/Hub.txt).
