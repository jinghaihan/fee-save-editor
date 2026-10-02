# Character export and import

Select a character in Roster, then use **Export** or **Import** below the character list. Export includes pending edits for that character without modifying the loaded save. Import refreshes the selected character's controls; use **File → Save Copy** to write the edited game save.

Character files use versioned `.fee-character.json` documents, not raw UNIT records. Import requires a format-version 9 game save with the same game-version field, person and gender. Enemy, temporary and unknown units are excluded. Import does not recruit a character, change their identity, or create/delete a unit.

Transferred fields:

- Class, weapon branch, level, experience and SP.
- Ten editable personal stats, current HP and internal level.
- Editable weapon proficiencies, inherited/equipped skills and learned class skill.
- Eight carried-item slots, including uses, refinement and weapon engravings.

The destination retains its force/recruitment state, custom name, innate proficiencies, personal skills, growth data, accessories, enhancements, AI, item flags, Emblem/Bond Ring associations and unknown fields. Sight is not editable. Imported class and skill choices, numeric limits and item limits use the editor's verified catalogs and validators.

Unknown skills/items and Engage-only equipment can round-trip unchanged, but cannot replace different destination records. An engraving already used in the convoy or by another character rejects the import instead of removing it from another weapon. Neither imports nor exports overwrite existing files. Invalid transfers leave the loaded save unchanged.

Tests cover synthetic and local real-save round trips, exact restoration, record resizing, identity/gender/version mismatches, malformed JSON, limits, protected equipment, engraving conflicts, translated controls and GUI/CLI save copies. Console gameplay after import has not been tested.
