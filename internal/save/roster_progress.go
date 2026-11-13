package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"slices"
	"strings"
	"unicode"
	"unicode/utf16"
)

func skillBytes(skills []RosterSkill) []byte {
	b := make([]byte, 8)
	put32(b, 0, 1)
	put32(b, 4, uint32(len(skills)))
	for _, v := range skills {
		b = append(b, reference(&v.Hash)...)
		p := len(b)
		b = append(b, make([]byte, 8)...)
		put32(b, p, uint32(v.Age))
		put32(b, p+4, uint32(v.Category))
	}
	return b
}
func (s *Save) replaceEquipped(index int, skills []RosterSkill) *Save {
	l := s.roster()
	c := l.get(index)
	if slices.Equal(c.character.Progress.EquippedSkills, skills) {
		return s
	}
	return s.replaceCharacter(l, c, c.progress.equippedStart, c.progress.equippedEnd, skillBytes(skills))
}
func (s *Save) WithRosterSkill(index int, id string, unlocked bool) (out *Save, err error) {
	defer recoverError(&err)
	def := catalog.Skill(catalog.Hash(id))
	need(def != nil && def.Id == id && def.Inheritable, "Select a verified inheritable skill.")
	l := s.roster()
	c := l.get(index)
	pool := c.character.Progress.InheritedSkills
	has := false
	for _, v := range pool {
		known := catalog.Skill(v.Hash)
		if unlocked && known != nil && known.Family == def.Family && known.Tier > def.Tier {
			return s, nil
		}
		if v.Hash == def.Hash() {
			has = true
		}
	}
	if has == unlocked {
		return s, nil
	}
	replaced := map[uint32]bool{}
	skills := []RosterSkill{}
	for _, v := range pool {
		known := catalog.Skill(v.Hash)
		if v.Hash == def.Hash() || (unlocked && known != nil && known.Family == def.Family) {
			replaced[v.Hash] = true
		} else {
			skills = append(skills, v)
		}
	}
	if unlocked {
		skills = append(skills, RosterSkill{def.Hash(), 0, 11})
	}
	need(len(skills) <= 1280, "The inherited skill pool is full.")
	edited := s.replaceCharacter(l, c, c.progress.poolStart, c.progress.poolEnd, skillBytes(skills))
	equipped := []RosterSkill{}
	for _, v := range c.character.Progress.EquippedSkills {
		if !replaced[v.Hash] {
			equipped = append(equipped, v)
		} else if unlocked {
			v.Hash = def.Hash()
			equipped = append(equipped, v)
		}
	}
	return edited.replaceEquipped(index, equipped), nil
}
func (s *Save) UnlockAllRosterSkills(index int) (out *Save, err error) {
	defer recoverError(&err)
	s.roster().get(index)
	best := map[string]catalog.SkillDefinition{}
	order := []string{}
	for _, v := range catalog.Skills {
		if !v.Inheritable {
			continue
		}
		old, ok := best[v.Family]
		if !ok {
			order = append(order, v.Family)
		}
		if !ok || v.Tier > old.Tier {
			best[v.Family] = v
		}
	}
	edited := s
	for _, family := range order {
		var e error
		edited, e = edited.WithRosterSkill(index, best[family].Id, true)
		if e != nil {
			return nil, e
		}
	}
	return edited, nil
}
func (s *Save) WithRosterEquippedSkills(index int, first, second *uint32) (out *Save, err error) {
	defer recoverError(&err)
	c := s.roster().get(index)
	hashes := []uint32{}
	for _, h := range []*uint32{first, second} {
		if h != nil {
			hashes = append(hashes, *h)
		}
	}
	old := []uint32{}
	for _, v := range c.character.Progress.EquippedSkills {
		old = append(old, v.Hash)
	}
	if slices.Equal(hashes, old) {
		return s, nil
	}
	need(len(hashes) < 2 || hashes[0] != hashes[1], "A skill cannot occupy both inheritance slots.")
	selected := []RosterSkill{}
	for _, h := range hashes {
		def := catalog.Skill(h)
		var previous *RosterSkill
		for _, v := range c.character.Progress.EquippedSkills {
			if v.Hash == h {
				copy := v
				previous = &copy
			}
		}
		need(def != nil && def.Inheritable || previous != nil, "Select an inheritable skill.")
		has := previous != nil
		for _, v := range c.character.Progress.InheritedSkills {
			has = has || v.Hash == h
		}
		need(has, "Unlock the skill before equipping it.")
		for _, v := range selected {
			known := catalog.Skill(v.Hash)
			need(def == nil || known == nil || def.Family != known.Family, "Different tiers of the same skill cannot be equipped together.")
		}
		v := RosterSkill{h, 0, 11}
		if previous != nil {
			v = *previous
		}
		selected = append(selected, v)
	}
	return s.replaceEquipped(index, selected), nil
}
func (s *Save) WithRosterProficiencies(index int, mask uint32) (out *Save, err error) {
	defer recoverError(&err)
	const editable uint32 = 510
	need(mask & ^editable == 0, "Only Sword through Arts proficiencies can be edited.")
	c := s.roster().get(index)
	old := c.character.Progress
	if old.Proficiencies&editable == mask {
		return s, nil
	}
	active := old.SelectedWeapons
	if job := catalog.Class(c.character.ClassHash); active == 0 && job != nil {
		variants := job.WeaponVariants()
		if len(variants) == 1 {
			active = variants[0]
		}
	}
	required := (old.OriginalProficiencies | active) & editable
	need(mask&required == required, "Innate and current-class weapon proficiencies must be retained.")
	b := bytes.Clone(s.data)
	put32(b, c.progress.masksOffset+4, (old.Proficiencies & ^editable)|mask)
	return verified(b), nil
}
func (s *Save) RosterMaximumHP(index int) (value int, err error) {
	defer recoverError(&err)
	c := s.roster().get(index)
	return clamp(c.character.Stats[0].Value+c.progress.hpBonus, 1, 255), nil
}
func (s *Save) WithRosterCondition(index, internalLevel, currentHP int) (out *Save, err error) {
	defer recoverError(&err)
	c := s.roster().get(index)
	old := c.character.Progress
	if internalLevel == old.InternalLevel && currentHP == old.CurrentHP {
		return s, nil
	}
	if internalLevel != old.InternalLevel {
		need(internalLevel >= -100 && internalLevel <= 100, "Internal level must be between -100 and 100.")
	}
	if currentHP != old.CurrentHP {
		need(currentHP >= 0 && currentHP <= clamp(c.character.Stats[0].Value+c.progress.hpBonus, 1, 255), "Current HP must not exceed maximum HP.")
	}
	b := bytes.Clone(s.data)
	b[c.progress.internalOffset] = byte(int8(internalLevel))
	b[c.levelOffset+2] = byte(currentHP)
	return verified(b), nil
}
func (s *Save) WithRosterClassSkill(index int, unlocked bool) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	job := catalog.Class(c.character.ClassHash)
	need(job != nil, "Unknown class.")
	var h *uint32
	if unlocked {
		level := 5
		if job.MaxLevel >= 40 {
			level = 25
		}
		need(job.LearningSkill != "" && c.character.Values.Level >= level, "The class skill requires the class's learning level (5 or 25).")
		v := catalog.Hash(job.LearningSkill)
		h = &v
	}
	old := c.character.Progress.ClassSkill
	if old == nil && h == nil || old != nil && h != nil && *old == *h {
		return s, nil
	}
	b := []byte{0}
	if h != nil {
		b[0] = 1
		b = append(b, reference(h)...)
	}
	return s.replaceCharacter(l, c, c.progress.classSkillStart, c.progress.classSkillEnd, b), nil
}
func (s *Save) RosterClasses(index int) (out []catalog.ClassDefinition, err error) {
	defer recoverError(&err)
	c := s.roster().get(index).character
	need(catalog.Person(c.PersonHash) != nil, "Select a known playable character.")
	return catalog.ClassesFor(c.PersonHash, c.Progress.Gender), nil
}
func (s *Save) WithRosterClass(index int, id string, weapons uint32) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	character := c.character
	var job *catalog.ClassDefinition
	for _, v := range catalog.ClassesFor(character.PersonHash, character.Progress.Gender) {
		if v.Id == id {
			copy := v
			job = &copy
		}
	}
	need(job != nil, "This character cannot use the selected class.")
	variants := job.WeaponVariants()
	need(slices.Contains(variants, weapons), "Select a valid weapon combination for this class.")
	changed := character.ClassHash != job.Hash()
	if !changed && (character.Progress.SelectedWeapons == weapons || character.Progress.SelectedWeapons == 0 && len(variants) == 1) {
		return s, nil
	}
	b := bytes.Clone(s.data)
	if changed {
		old := catalog.Class(character.ClassHash)
		need(old != nil, "The existing class is not recognized.")
		level := 1
		if job.MaxLevel >= 40 && (old.Advanced || old.MaxLevel >= 40 && character.Values.Level >= 21) {
			level = 21
		}
		main := s.mainLayout()
		cap := 30
		switch main.Values.Difficulty {
		case "Hard":
			cap = 40
		case "Maddening":
			cap = 50
		}
		internal := max(0, clamp(character.Progress.InternalLevel+character.Values.Level-1, 0, cap)-level+1)
		need(c.classEnd-c.classStart == 6, "The existing class reference is not serialized.")
		put32(b, c.baseOffset-8, job.Hash())
		b[c.levelOffset] = byte(level)
		b[c.levelOffset+1] = 0
		b[c.progress.internalOffset] = byte(internal)
		person := catalog.Person(character.PersonHash)
		hpLimit := clamp(job.Limits[0]+person.LimitModifiers[0], 1, 255)
		hp := clamp(clamp(job.BaseStats[0]+int(int8(s.data[c.baseOffset])), 1, hpLimit)+c.progress.hpBonus, 1, 255)
		b[c.levelOffset+2] = byte(min(character.Progress.CurrentHP, hp))
	}
	put32(b, c.progress.masksOffset+4, character.Progress.Proficiencies|weapons)
	put32(b, c.progress.masksOffset+8, weapons)
	edited := verified(b)
	if changed {
		return edited.WithRosterClassSkill(index, false)
	}
	return edited, nil
}
func (s *Save) protagonist(l rosterLayout) *characterLayout {
	var result *characterLayout
	for _, c := range l.characters {
		if c.character.PersonHash == catalog.Hash("PID_リュール") {
			need(result == nil, "The save contains ambiguous protagonist records.")
			copy := c
			result = &copy
		}
	}
	return result
}
func (s *Save) ProtagonistName() (name *string, err error) {
	defer recoverError(&err)
	c := s.protagonist(s.roster())
	if c != nil {
		return c.character.Progress.CustomName, nil
	}
	return nil, nil
}
func (s *Save) WithProtagonistName(name string) (out *Save, err error) {
	defer recoverError(&err)
	need(strings.TrimSpace(name) != "", "The protagonist needs a nonempty name.")
	for _, r := range name {
		need(!unicode.IsControl(r), "The name cannot contain control characters.")
	}
	need(len(utf16.Encode([]rune(name)))*2 <= 4096, "The name exceeds the serialized string limit.")
	l := s.roster()
	c := s.protagonist(l)
	need(c != nil, "The save does not contain the protagonist.")
	need(c.progress.nameStart != nil && c.progress.nameEnd != nil, "The protagonist has no saved customization record.")
	return s.replaceCharacter(l, *c, *c.progress.nameStart, *c.progress.nameEnd, wide(name)), nil
}
func (s *Save) ProtagonistGender() (gender *int, err error) {
	defer recoverError(&err)
	c := s.protagonist(s.roster())
	if c == nil || c.progress.nameEnd == nil {
		return nil, nil
	}
	v := c.character.Progress.Gender
	need(v == 1 || v == 2, "Unsupported protagonist gender.")
	return &v, nil
}
func (s *Save) WithProtagonistGender(gender int) (out *Save, err error) {
	defer recoverError(&err)
	need(gender == 1 || gender == 2, "Unsupported protagonist gender.")
	l := s.roster()
	c := s.protagonist(l)
	need(c != nil, "The save has no protagonist record.")
	need(c.progress.nameEnd != nil, "The protagonist has no saved customization record.")
	p := *c.progress.nameEnd
	return s.replaceCharacter(l, *c, p, p+1, []byte{byte(gender)}), nil
}
