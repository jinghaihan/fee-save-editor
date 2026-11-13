package save

import (
	"bytes"
	"encoding/binary"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type UnitForce int

const (
	Player UnitForce = iota
	Enemy
	Ally
	Absent
	Dead
	Lost
	Temporary
)

type RosterStat int

const (
	HP RosterStat = iota
	Strength
	Dexterity
	Speed
	Luck
	Defense
	Magic
	Resistance
	Build
	Sight
	Movement
)

var StatNames = []string{"HP", "Strength", "Dexterity", "Speed", "Luck", "Defense", "Magic", "Resistance", "Build", "Sight", "Movement"}

type RosterValue struct{ Level, Experience, SkillPoints int }
type CharacterStat struct {
	Stat          RosterStat
	Value         int
	Maximum       *int
	PersonalValue int
}
type RosterSkill struct {
	Hash          uint32
	Age, Category int
}
type RosterProgress struct {
	CurrentHP, InternalLevel                              int
	OriginalProficiencies, Proficiencies, SelectedWeapons uint32
	ClassSkill                                            *uint32
	EquippedSkills, InheritedSkills                       []RosterSkill
	CustomName                                            *string
	Gender                                                int
}
type RosterCharacter struct {
	Index                 int
	Force                 UnitForce
	PersonHash, ClassHash uint32
	Values                RosterValue
	Stats                 []CharacterStat
	Items                 []InventorySlot
	Progress              RosterProgress
	StatusFlags           uint64
}

func (c RosterCharacter) Availability() string {
	if c.Force == Lost {
		return "Lost"
	}
	if c.Force == Dead || c.StatusFlags&0x200 != 0 {
		return "Dead"
	}
	if c.Force == Player || c.Force == Absent {
		return "Available"
	}
	return "Other"
}

type progressLayout struct {
	values                                                                                                                          RosterProgress
	equippedStart, equippedEnd, poolStart, poolEnd, classSkillStart, classSkillEnd, masksOffset, internalOffset, hpBonus, tailStart int
	nameStart, nameEnd                                                                                                              *int
}
type characterLayout struct {
	character                           RosterCharacter
	start, end, baseOffset, levelOffset int
	itemStarts, itemEnds                []int
	progress                            progressLayout
	classStart, classEnd                int
	attackTarget                        *uint32
}
type rosterLayout struct {
	section    Section
	characters []characterLayout
}

func clamp(n, low, high int) int { return max(low, min(high, n)) }
func calculateStat(stat RosterStat, personal int, job *catalog.ClassDefinition, person *catalog.PersonDefinition) CharacterStat {
	var maximum *int
	if job != nil && person != nil {
		v := clamp(job.Limits[stat]+person.LimitModifiers[stat], 0, 255)
		maximum = &v
	}
	if stat == Movement && job != nil {
		v := job.BaseStats[stat] + 2
		maximum = &v
	}
	value := personal
	if job != nil && maximum != nil {
		low := 0
		if stat == HP {
			low = 1
		}
		value = clamp(personal+job.BaseStats[stat], low, *maximum)
	}
	return CharacterStat{stat, value, maximum, personal}
}
func (s *Save) roster() rosterLayout {
	s.RequireGame()
	sec := s.Section("UNIT")
	r := reader{s.data, sec.PayloadOffset(), sec.PayloadOffset() + sec.Length}
	need(r.u32() == 0 && r.u32() == 0xcdcdcdcd, "Unsupported UNIT section version.")
	r.skip(24)
	l := rosterLayout{section: sec, characters: []characterLayout{}}
	last := -1
	for {
		force := int(r.byte())
		if force == 255 {
			break
		}
		need(force > last && force <= 6, "Invalid or duplicate unit force.")
		last = force
		count := int(r.byte())
		need(count > 0 && len(l.characters)+count <= 250, "Invalid unit pool count.")
		for range count {
			start := r.pos
			length := int(r.u32())
			need(length >= 160 && length <= r.end-r.pos+4, "Invalid character record length.")
			end := start + length
			u := reader{s.data, start + 4, end}
			need(u.u32() == 40 && u.u32() == 0xcdcdcdcd, "Unsupported character record version.")
			u.skip(24)
			statusOffset := u.pos
			u.skip(8)
			status := binary.LittleEndian.Uint64(s.data[statusOffset:])
			person := u.ref()
			need(person != nil, "A character has no person reference.")
			classStart := u.pos
			job := u.ref()
			classEnd := u.pos
			var jobHash uint32
			if job != nil {
				jobHash = *job
			}
			need(u.u32() == 0, "Unsupported base capability version.")
			base := u.pos
			stats := make([]CharacterStat, 11)
			for i := range stats {
				stats[i] = calculateStat(RosterStat(i), int(int8(u.byte())), catalog.Class(jobHash), catalog.Person(*person))
			}
			for range 2 {
				need(u.u32() == 0, "Unsupported growth capability version.")
				u.skip(11)
			}
			u.skip(8)
			levelOffset := u.pos
			level, exp, hp := int(u.byte()), int(u.byte()), int(u.byte())
			u.skip(8)
			hasTarget := u.byte()
			need(hasTarget <= 1, "Invalid optional target presence flag.")
			var target *uint32
			if hasTarget == 1 {
				target = u.ref()
			}
			u.skip(4)
			need(u.u32() == 2 && u.byte() == 8, "Unsupported character item list.")
			items := make([]InventorySlot, 8)
			starts, ends := make([]int, 8), make([]int, 8)
			for i := range items {
				starts[i] = u.pos
				items[i] = InventorySlot{i, readItem(&u)}
				ends[i] = u.pos
			}
			progress := readProgress(&u, hp, UnitForce(force), *person)
			need(u.end-u.pos >= 10, "The character trailer is truncated.")
			sp := int(int16(get16(s.data, end-8)))
			c := RosterCharacter{len(l.characters), UnitForce(force), *person, jobHash, RosterValue{level, exp, sp}, stats, items, progress.values, status}
			l.characters = append(l.characters, characterLayout{c, start, end, base, levelOffset, starts, ends, progress, classStart, classEnd, target})
			r.skip(end - r.pos)
		}
	}
	need(r.pos == r.end, "The unit pool contains unrecognized trailing data.")
	return l
}
func readSkills(r *reader) []RosterSkill {
	need(r.u32() == 1, "Unsupported skill array version.")
	count := int(r.u32())
	need(count <= 1280 && count <= (r.end-r.pos)/10, "Invalid skill array length.")
	out := make([]RosterSkill, count)
	for i := range out {
		h := r.ref()
		need(h != nil, "A skill entry has no reference.")
		out[i] = RosterSkill{*h, int(int32(r.u32())), int(int32(r.u32()))}
	}
	return out
}
func readEnhancement(r *reader) int {
	need(r.u32() == 1 && r.u32() == 11, "Unsupported enhancement capability version.")
	hp := int(int32(r.u32()))
	r.skip(40)
	return hp
}
func readProgress(r *reader, hp int, force UnitForce, person uint32) progressLayout {
	need(r.u32() == 0, "Unsupported accessory list version.")
	for range 4 {
		r.ref()
	}
	r.skip(4)
	p := progressLayout{}
	p.equippedStart = r.pos
	equipped := readSkills(r)
	p.equippedEnd = r.pos
	readSkills(r)
	p.poolStart = r.pos
	pool := readSkills(r)
	p.poolEnd = r.pos
	p.classSkillStart = r.pos
	present := r.byte()
	need(present <= 1, "Invalid class skill presence flag.")
	var classSkill *uint32
	if present == 1 {
		classSkill = r.ref()
	}
	p.classSkillEnd = r.pos
	p.masksOffset = r.pos
	original, aptitude, weapons := r.u32(), r.u32(), r.u32()
	need(r.u32() == 6, "Unsupported enhancement factors version.")
	for range 3 {
		readEnhancement(r)
	}
	need(r.u32() == 1, "Unsupported enhancement calculator version.")
	p.hpBonus = readEnhancement(r)
	p.internalOffset = r.pos
	internal := int(int8(r.byte()))
	if force <= Ally {
		need(r.u32() == 7, "Unsupported character AI version.")
		r.skip(17)
		r.str()
		need(r.u32() == 0, "Unsupported AI movement range version.")
		r.skip(7)
		for range 4 {
			r.str()
		}
		for range 16 {
			need(r.u32() == 0, "Unsupported AI value version.")
			r.skip(2)
		}
	}
	need(r.byte() == 2, "Unsupported character customization version.")
	custom := r.byte()
	need(custom <= 1, "Invalid character customization presence flag.")
	gender := 0
	if def := catalog.Person(person); def != nil {
		gender = def.Gender
	}
	var name *string
	if custom == 1 {
		start := r.pos
		p.nameStart = &start
		name = r.str()
		end := r.pos
		p.nameEnd = &end
		gender = int(r.byte())
		r.skip(3)
	}
	p.values = RosterProgress{hp, internal, original, aptitude, weapons, classSkill, equipped, pool, name, gender}
	p.tailStart = r.pos
	return p
}
func (l rosterLayout) get(index int) characterLayout {
	need(index >= 0 && index < len(l.characters), "The character index is outside the saved roster.")
	return l.characters[index]
}
func (s *Save) Roster() (out []RosterCharacter, err error) {
	defer recoverError(&err)
	l := s.roster()
	out = make([]RosterCharacter, len(l.characters))
	for i, c := range l.characters {
		out[i] = c.character
	}
	return out, nil
}
func (s *Save) replaceCharacter(l rosterLayout, c characterLayout, start, end int, replacement []byte) *Save {
	need(start >= c.start+4 && end >= start && end <= c.end, "Invalid character replacement range.")
	record := splice(s.data[c.start:c.end], start-c.start, end-c.start, replacement)
	put32(record, 0, uint32(len(record)))
	p := l.section.PayloadOffset()
	return s.replaceSection(l.section, splice(s.data[p:p+l.section.Length], c.start-p, c.end-p, record))
}
func (s *Save) WithRosterValues(index int, v RosterValue) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	old := c.character.Values
	if v == old {
		return s, nil
	}
	if v.Level != old.Level || v.Experience != old.Experience {
		job := catalog.Class(c.character.ClassHash)
		need(job != nil, "This class has no verified level limit.")
		need(v.Level >= 1 && v.Level <= job.MaxLevel, "Invalid character level.")
		need(v.Experience >= 0 && v.Experience <= 99 && (v.Level != job.MaxLevel || v.Experience == 0), "Experience must be 0–99, or 0 at maximum level.")
	}
	if v.SkillPoints != old.SkillPoints {
		need(v.SkillPoints >= 0 && v.SkillPoints <= 9999, "SP must be between 0 and 9999.")
	}
	b := bytes.Clone(s.data)
	b[c.levelOffset] = byte(v.Level)
	b[c.levelOffset+1] = byte(v.Experience)
	put16(b, c.end-8, uint16(v.SkillPoints))
	return verified(b), nil
}
func (s *Save) WithRosterStat(index int, stat RosterStat, value int, personal bool) (out *Save, err error) {
	defer recoverError(&err)
	need(stat >= HP && stat <= Movement && stat != Sight, "Select an editable character stat.")
	l := s.roster()
	c := l.get(index)
	old := c.character.Stats[stat]
	if (personal && value == old.PersonalValue) || (!personal && value == old.Value) {
		return s, nil
	}
	job := catalog.Class(c.character.ClassHash)
	person := catalog.Person(c.character.PersonHash)
	need(job != nil && person != nil, "This character or class has no verified stat limits.")
	low := 0
	if stat == HP {
		low = 1
	}
	stored := value
	if personal {
		high := 127
		if stat == Movement {
			high = 2
		}
		need(value >= max(-128, low-job.BaseStats[stat]) && value <= high, "Personal stat is outside its valid range.")
	} else {
		stored = value - job.BaseStats[stat]
		need(old.Maximum != nil && value >= low && value <= *old.Maximum && stored >= -120 && stored <= 120, "The displayed stat is outside its valid range.")
	}
	b := bytes.Clone(s.data)
	b[c.baseOffset+int(stat)] = byte(int8(stored))
	if stat == HP {
		maxHP := calculateStat(stat, stored, job, person).Value + c.progress.hpBonus
		b[c.levelOffset+2] = byte(min(int(b[c.levelOffset+2]), clamp(maxHP, 1, 255)))
	}
	return verified(b), nil
}
func maximumPersonal(person uint32, gender int) []int {
	p := catalog.Person(person)
	need(p != nil, "Select a known playable character.")
	classes := catalog.ClassesFor(person, gender)
	need(len(classes) > 0, "This character has no verified playable classes.")
	values := make([]int, 9)
	for i := range values {
		v := -1000
		for _, job := range classes {
			v = max(v, clamp(job.Limits[i]+p.LimitModifiers[i], 0, 255)-job.BaseStats[i])
		}
		need(v >= -128 && v <= 127, "A class cap cannot be represented by personal stat storage.")
		values[i] = v
	}
	return values
}
func (s *Save) MaximizeRosterStats(index *int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	if index != nil {
		c := l.get(*index).character
		need(c.Force != Enemy && c.Force != Temporary && catalog.Person(c.PersonHash) != nil, "Select a known playable roster character.")
	}
	b := bytes.Clone(s.data)
	for i, c := range l.characters {
		if index != nil && i != *index {
			continue
		}
		if c.character.Force == Enemy || c.character.Force == Temporary || catalog.Person(c.character.PersonHash) == nil {
			continue
		}
		for stat, v := range maximumPersonal(c.character.PersonHash, c.character.Progress.Gender) {
			b[c.baseOffset+stat] = byte(int8(v))
		}
	}
	return verified(b), nil
}
func characterItem(c characterLayout, slot int) *InventoryItem {
	need(slot >= 0 && slot < len(c.character.Items), "Invalid character item slot.")
	return c.character.Items[slot].Item
}
func checkItemEditable(item *InventoryItem) {
	if item != nil {
		def := catalog.RosterItem(item.ItemHash)
		need(def == nil || !def.EngageOnly, "Engage weapons and their reserved slots are managed by the game.")
	}
}
func (s *Save) WithRosterItem(index, slot int, id string, uses, refine int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	old := characterItem(c, slot)
	checkItemEditable(old)
	d, e := catalog.ItemByID(id)
	need(e == nil, "Choose a verified convoy item.")
	item := &InventoryItem{ItemHash: d.Hash(), Uses: uses, RefineLevel: refine}
	if old != nil {
		item.Flags = old.Flags
		item.EngravingHash = old.EngravingHash
	}
	validateItem(item)
	return s.replaceCharacter(l, c, c.itemStarts[slot], c.itemEnds[slot], itemBytes(item)), nil
}
func (s *Save) DeleteRosterItem(index, slot int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	checkItemEditable(characterItem(c, slot))
	return s.replaceCharacter(l, c, c.itemStarts[slot], c.itemEnds[slot], itemBytes(nil)), nil
}
func (s *Save) RestoreRosterUses(index int) (out *Save, err error) {
	defer recoverError(&err)
	c := s.roster().get(index)
	b := bytes.Clone(s.data)
	for _, slot := range c.character.Items {
		if slot.Item == nil {
			continue
		}
		d := catalog.Item(slot.Item.ItemHash)
		if d != nil && d.MaxUses != 255 {
			b[c.itemStarts[slot.Slot]+11] = byte(d.MaxUses)
		}
	}
	return verified(b), nil
}
