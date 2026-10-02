# Donations

Four editable countries are selected from `HubInvestment.xml` where `IsNotLevel`
is false: Firene, Brodia, Elusia and Solm. Lythos, Gradlon and DLC locations are
not additional donation countries.

Source: [HubInvestment.xml](https://github.com/Xzonn/FireEmblemEngageData/blob/86b8be7b9820e1bb3bce87d2a9a805ead85d92ab/data/xml/HubInvestment.xml).
`tools/import_donation_catalog.py` pins the data revision and imports names in all nine supported languages from the pinned game-text revision.

| Level | Cumulative donated gold |
| --- | ---: |
| 1 | 0 |
| 2 | 5,000 |
| 3 | 15,000 |
| 4 | 40,000 |
| 5 | 90,000 |

The table's incremental costs are 0, 5,000, 10,000, 25,000 and 50,000. Summing
them gives the thresholds above. Partial donations remain exact; amounts above
90,000 remain level five, rather than being silently clamped on load.

## Save fields and native validation

The version-20 USER game-variable block stores four signed integers:

- `G_投資_フィレネ`
- `G_投資_ブロディア`
- `G_投資_イルシオン`
- `G_投資_ソルム`

In the examined executable (build ID `8c08b9719e085f91847b5e0f935d9488`), their
getter/setter RVAs are `0x25105d0`/`0x2510690`, `0x2510840`/`0x2510900`,
`0x2510ab0`/`0x2510b70` and `0x2510d20`/`0x2510de0`. All clamp to
`0..9,999,999`. Missing integer variables read as zero. The editor rejects
duplicate, wrongly typed, negative and over-limit amounts instead of guessing.
New nonzero variables are inserted only when absent, updating block size/count,
section offsets and CRC32 while preserving unmodeled fields.

Setting a level writes its exact cumulative threshold. Max actions set exactly
90,000 per selected country, even if a prior amount was higher. Edits do not
charge/refund money, modify achievement counters, replay one-time item/animal/
accessory rewards, reset claimed rewards, or unlock chapters. The existing
world-map progression remains responsible for access to countries. This is an
amount/level editor, not a simulation of the game's donation purchase routine.

Tests cover boundaries, partial and above-threshold values, absent-key insertion,
malformed records, atomic rejection, and exact restoration of existing fields
in both synthetic and local private saves. Original private saves are not written.
Game-load verification remains separate from serialization verification.
