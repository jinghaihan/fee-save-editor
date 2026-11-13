package save

import (
	"bytes"
	"encoding/json"
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"io"
	"slices"
)

type RosterTransferItem struct {
	ItemHash          uint32
	Uses, RefineLevel int
	EngravingHash     *uint32
}
type RosterTransfer struct {
	Format                          string
	Version                         int
	GameVersion, PersonHash         uint32
	Gender                          int
	ClassHash                       uint32
	Values                          *RosterValue
	PersonalStats                   []int
	CurrentHP, InternalLevel        int
	Proficiencies, SelectedWeapons  uint32
	ClassSkill                      *uint32
	InheritedSkills, EquippedSkills []uint32
	Items                           []*RosterTransferItem
}

func transferItem(v *InventoryItem) *RosterTransferItem {
	if v == nil {
		return nil
	}
	return &RosterTransferItem{v.ItemHash, v.Uses, v.RefineLevel, v.EngravingHash}
}
func hashes(skills []RosterSkill) []uint32 {
	out := make([]uint32, len(skills))
	for i, v := range skills {
		out[i] = v.Hash
	}
	return out
}
func sameHash(a, b *uint32) bool { return a == nil && b == nil || a != nil && b != nil && *a == *b }
func sameTransferItem(a, b *RosterTransferItem) bool {
	return a == nil && b == nil || a != nil && b != nil && a.ItemHash == b.ItemHash && a.Uses == b.Uses && a.RefineLevel == b.RefineLevel && sameHash(a.EngravingHash, b.EngravingHash)
}
func transferCharacter(c RosterCharacter) {
	need(c.Force != Enemy && c.Force != Temporary && catalog.Person(c.PersonHash) != nil, "Select a known playable roster character.")
}
func (s *Save) ExportRosterCharacter(index int) (out []byte, err error) {
	defer recoverError(&err)
	c := s.roster().get(index).character
	transferCharacter(c)
	p := c.Progress
	t := RosterTransfer{Format: "FeeEditor.RosterCharacter", Version: 1, GameVersion: *s.GameVersion, PersonHash: c.PersonHash, Gender: p.Gender, ClassHash: c.ClassHash, Values: &c.Values, PersonalStats: make([]int, len(c.Stats)), CurrentHP: p.CurrentHP, InternalLevel: p.InternalLevel, Proficiencies: p.Proficiencies, SelectedWeapons: p.SelectedWeapons, ClassSkill: p.ClassSkill, InheritedSkills: hashes(p.InheritedSkills), EquippedSkills: hashes(p.EquippedSkills), Items: make([]*RosterTransferItem, len(c.Items))}
	for i, v := range c.Stats {
		t.PersonalStats[i] = v.PersonalValue
	}
	for i, v := range c.Items {
		t.Items[i] = transferItem(v.Item)
	}
	return json.MarshalIndent(t, "", "  ")
}
func checkJSONDuplicates(d *json.Decoder) error {
	token, e := d.Token()
	if e != nil {
		return e
	}
	delim, ok := token.(json.Delim)
	if !ok {
		return nil
	}
	switch delim {
	case '{':
		seen := map[string]bool{}
		for d.More() {
			key, e := d.Token()
			if e != nil {
				return e
			}
			name, ok := key.(string)
			if !ok || seen[name] {
				return fmt.Errorf("duplicate character property: %v", key)
			}
			seen[name] = true
			if e = checkJSONDuplicates(d); e != nil {
				return e
			}
		}
	case '[':
		for d.More() {
			if e = checkJSONDuplicates(d); e != nil {
				return e
			}
		}
	default:
		return fmt.Errorf("invalid JSON delimiter")
	}
	_, e = d.Token()
	return e
}
func requireJSONFields(raw json.RawMessage, required, nullable []string) error {
	var fields map[string]json.RawMessage
	if e := json.Unmarshal(raw, &fields); e != nil || fields == nil {
		return fmt.Errorf("the character file is missing an object")
	}
	for key := range fields {
		if !slices.Contains(required, key) {
			return fmt.Errorf("unknown character property: %s", key)
		}
	}
	for _, key := range required {
		value, ok := fields[key]
		if !ok || bytes.Equal(bytes.TrimSpace(value), []byte("null")) && !slices.Contains(nullable, key) {
			return fmt.Errorf("missing or null character property: %s", key)
		}
	}
	return nil
}
func ReadRosterTransfer(b []byte) (out RosterTransfer, err error) {
	if len(b) == 0 || len(b) > 1048576 {
		return out, fmt.Errorf("a character file must be between 1 byte and 1 MiB")
	}
	dup := json.NewDecoder(bytes.NewReader(b))
	if e := checkJSONDuplicates(dup); e != nil {
		return out, e
	}
	if _, e := dup.Token(); e != io.EOF {
		return out, fmt.Errorf("unexpected data after character JSON")
	}
	keys := []string{"Format", "Version", "GameVersion", "PersonHash", "Gender", "ClassHash", "Values", "PersonalStats", "CurrentHP", "InternalLevel", "Proficiencies", "SelectedWeapons", "ClassSkill", "InheritedSkills", "EquippedSkills", "Items"}
	if e := requireJSONFields(b, keys, []string{"ClassSkill"}); e != nil {
		return out, e
	}
	var raw map[string]json.RawMessage
	if e := json.Unmarshal(b, &raw); e != nil {
		return out, e
	}
	if e := requireJSONFields(raw["Values"], []string{"Level", "Experience", "SkillPoints"}, nil); e != nil {
		return out, e
	}
	var items []json.RawMessage
	if e := json.Unmarshal(raw["Items"], &items); e != nil {
		return out, e
	}
	for _, item := range items {
		if bytes.Equal(bytes.TrimSpace(item), []byte("null")) {
			continue
		}
		if e := requireJSONFields(item, []string{"ItemHash", "Uses", "RefineLevel", "EngravingHash"}, []string{"EngravingHash"}); e != nil {
			return out, e
		}
	}
	decoder := json.NewDecoder(bytes.NewReader(b))
	decoder.DisallowUnknownFields()
	err = decoder.Decode(&out)
	return out, err
}
func transferWeapons(job catalog.ClassDefinition, saved uint32) uint32 {
	variants := job.WeaponVariants()
	if slices.Contains(variants, saved) {
		return saved
	}
	const special uint32 = 1 << 9
	if job.Weapons[9] == 1 && slices.Contains(variants, saved|special) {
		return saved | special
	}
	if saved == 0 && len(variants) == 1 {
		return variants[0]
	}
	panic(dataError("Invalid imported class weapon combination."))
}
func validateTransferSkills(selected []uint32, existing []RosterSkill, maximum int, equipped bool) {
	if slices.Equal(selected, hashes(existing)) {
		return
	}
	need(len(selected) <= maximum, "Invalid imported inherited skills.")
	used := map[uint32]bool{}
	families := map[string]bool{}
	for _, hash := range selected {
		def := catalog.Skill(hash)
		need(!used[hash] && def != nil && def.Inheritable, "Invalid imported inherited skills.")
		used[hash] = true
		if equipped {
			need(!families[def.Family], "Different tiers of the same skill cannot occupy both slots.")
			families[def.Family] = true
		}
	}
}
func (s *Save) validateTransferItems(target RosterCharacter, items []*RosterTransferItem) {
	for slot, requested := range items {
		current := target.Items[slot].Item
		if sameTransferItem(transferItem(current), requested) {
			continue
		}
		checkItemEditable(current)
		if requested == nil {
			continue
		}
		if def := catalog.RosterItem(requested.ItemHash); def != nil {
			need(!def.EngageOnly, "Engage weapons and reserved slots are not portable character items.")
		}
		validateItem(&InventoryItem{requested.ItemHash, requested.Uses, requested.RefineLevel, 0, requested.EngravingHash})
		if requested.EngravingHash == nil {
			continue
		}
		engraving := catalog.Engraving(*requested.EngravingHash)
		need(engraving != nil, "Invalid imported engraving.")
		same := func(item *RosterTransferItem) bool {
			if item == nil || item.EngravingHash == nil {
				return false
			}
			d := catalog.Engraving(*item.EngravingHash)
			return d != nil && d.Id == engraving.Id
		}
		count := 0
		for _, item := range items {
			if same(item) {
				count++
			}
		}
		need(count == 1, "Invalid or duplicate imported engraving.")
		for _, row := range s.inventory().slots {
			need(!same(transferItem(row.Item)), "An imported engraving is already used elsewhere in this save.")
		}
		for _, row := range s.roster().characters {
			c := row.character
			if c.Index == target.Index || c.Force == Enemy || c.Force == Temporary {
				continue
			}
			for _, slot := range c.Items {
				need(!same(transferItem(slot.Item)), "An imported engraving is already used elsewhere in this save.")
			}
		}
	}
}
func (s *Save) validateTransfer(target RosterCharacter, t RosterTransfer) {
	need(t.Format == "FeeEditor.RosterCharacter" && t.Version == 1 && t.GameVersion == *s.GameVersion, "The character file format or game version does not match this save.")
	need(t.PersonHash == target.PersonHash && t.Gender == target.Progress.Gender, "Import into the same character and gender; character identity is not replaced.")
	need(t.Values != nil && len(t.PersonalStats) == 11 && len(t.Items) == 8 && t.InheritedSkills != nil && t.EquippedSkills != nil, "The character file has missing or incorrectly sized fields.")
	var job *catalog.ClassDefinition
	for _, v := range catalog.ClassesFor(target.PersonHash, target.Progress.Gender) {
		if v.Hash() == t.ClassHash {
			copy := v
			job = &copy
		}
	}
	need(job != nil, "This character cannot use the imported class.")
	transferWeapons(*job, t.SelectedWeapons)
	v := *t.Values
	need(v.Level >= 1 && v.Level <= job.MaxLevel && v.Experience >= 0 && v.Experience <= 99 && (v.Level != job.MaxLevel || v.Experience == 0) && v.SkillPoints >= 0 && v.SkillPoints <= 9999 && t.InternalLevel >= -100 && t.InternalLevel <= 100, "Invalid imported level, experience, SP or internal level.")
	for stat, value := range t.PersonalStats {
		if stat == int(Sight) {
			need(value == target.Stats[stat].PersonalValue, "Sight is not an editable stat.")
			continue
		}
		low := 0
		if stat == 0 {
			low = 1
		}
		high := 127
		if stat == int(Movement) {
			high = 2
		}
		need(value >= max(-128, low-job.BaseStats[stat]) && value <= high, "Imported personal stat is outside its limits.")
	}
	need(t.Proficiencies & ^uint32(510) == target.Progress.Proficiencies & ^uint32(510), "Reserved proficiency bits must not change.")
	if t.ClassSkill != nil {
		level := 5
		if job.MaxLevel >= 40 {
			level = 25
		}
		need(job.LearningSkill != "" && *t.ClassSkill == catalog.Hash(job.LearningSkill) && v.Level >= level, "The imported class skill is not available at this class level.")
	}
	validateTransferSkills(t.InheritedSkills, target.Progress.InheritedSkills, 1280, false)
	validateTransferSkills(t.EquippedSkills, target.Progress.EquippedSkills, 2, true)
	for _, h := range t.EquippedSkills {
		need(slices.Contains(t.InheritedSkills, h) || slices.Contains(hashes(target.Progress.EquippedSkills), h) && !slices.Contains(hashes(target.Progress.InheritedSkills), h), "Unlock imported skills before equipping them.")
	}
	s.validateTransferItems(target, t.Items)
}
func transferSkillRows(requested []uint32, existing []RosterSkill) []RosterSkill {
	out := make([]RosterSkill, len(requested))
	for i, hash := range requested {
		out[i] = RosterSkill{hash, 0, 11}
		for _, old := range existing {
			if old.Hash == hash {
				out[i] = old
				break
			}
		}
	}
	return out
}
func (s *Save) ImportRosterCharacter(index int, b []byte) (out *Save, err error) {
	defer recoverError(&err)
	t, e := ReadRosterTransfer(b)
	if e != nil {
		return nil, e
	}
	c := s.roster().get(index).character
	transferCharacter(c)
	s.validateTransfer(c, t)
	job := catalog.Class(t.ClassHash)
	edited := s
	if t.ClassHash != c.ClassHash || t.SelectedWeapons != c.Progress.SelectedWeapons {
		edited, e = edited.WithRosterClass(index, job.Id, transferWeapons(*job, t.SelectedWeapons))
		if e != nil {
			return nil, e
		}
	}
	edited, e = edited.WithRosterValues(index, *t.Values)
	if e != nil {
		return nil, e
	}
	for stat, value := range t.PersonalStats {
		if stat == int(Sight) {
			continue
		}
		edited, e = edited.WithRosterStat(index, RosterStat(stat), value, true)
		if e != nil {
			return nil, e
		}
	}
	maxHP, e := edited.RosterMaximumHP(index)
	if e != nil {
		return nil, e
	}
	need(t.CurrentHP >= 0 && t.CurrentHP <= maxHP, "Imported current HP must not exceed maximum HP.")
	edited, e = edited.WithRosterCondition(index, t.InternalLevel, t.CurrentHP)
	if e != nil {
		return nil, e
	}
	edited, e = edited.WithRosterProficiencies(index, t.Proficiencies&510)
	if e != nil {
		return nil, e
	}
	loc := edited.roster().get(index)
	if loc.character.Progress.SelectedWeapons != t.SelectedWeapons {
		data := bytes.Clone(edited.data)
		put32(data, loc.progress.masksOffset+8, t.SelectedWeapons)
		edited = verified(data)
	}
	if !sameHash(loc.character.Progress.ClassSkill, t.ClassSkill) {
		edited, e = edited.WithRosterClassSkill(index, t.ClassSkill != nil)
		if e != nil {
			return nil, e
		}
	}
	l := edited.roster()
	loc = l.get(index)
	existing := loc.character.Progress.InheritedSkills
	if !slices.Equal(t.InheritedSkills, hashes(existing)) {
		edited = edited.replaceCharacter(l, loc, loc.progress.poolStart, loc.progress.poolEnd, skillBytes(transferSkillRows(t.InheritedSkills, existing)))
	}
	equipped := edited.roster().get(index).character.Progress.EquippedSkills
	edited = edited.replaceEquipped(index, transferSkillRows(t.EquippedSkills, equipped))
	for slot, requested := range t.Items {
		l := edited.roster()
		loc := l.get(index)
		current := loc.character.Items[slot].Item
		if sameTransferItem(transferItem(current), requested) {
			continue
		}
		var item *InventoryItem
		if requested != nil {
			flags := uint32(0)
			if current != nil {
				flags = current.Flags
			}
			item = &InventoryItem{requested.ItemHash, requested.Uses, requested.RefineLevel, flags, requested.EngravingHash}
		}
		edited = edited.replaceCharacter(l, loc, loc.itemStarts[slot], loc.itemEnds[slot], itemBytes(item))
	}
	actual, e := edited.ExportRosterCharacter(index)
	if e != nil {
		return nil, e
	}
	expected, e := json.MarshalIndent(t, "", "  ")
	if e != nil {
		return nil, e
	}
	need(bytes.Equal(actual, expected), "The imported character values did not survive serialization.")
	return edited, nil
}
