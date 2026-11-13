package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"slices"
)

type EmblemBond struct {
	PersonId          string
	Level, Experience int
	InheritedSkills   []uint32
	TalkFlags         byte
}
type SavedEmblem struct {
	InstanceId  uint32
	EmblemId    string
	PactPartner *string
	Bonds       []EmblemBond
}
type BondRing struct {
	InstanceId, RingHash uint32
	StockCount           int
	OwnerIndex           *int
}
type CharacterRingLinks struct {
	CharacterIndex                                      int
	EmblemInstance, PartnerEmblemInstance, RingInstance uint32
}
type bondLocation struct {
	instance                  uint32
	bond                      EmblemBond
	valuesOffset, flagsOffset int
}
type bondHolder struct {
	instance         uint32
	countOffset, end int
}
type emblemLayout struct {
	section Section
	emblems []SavedEmblem
	bonds   []bondLocation
	holders []bondHolder
}

func (s *Save) pool(name string, version uint32) (Section, *reader) {
	s.RequireGame()
	sec := s.Section(name)
	r := &reader{s.data, sec.PayloadOffset(), sec.PayloadOffset() + sec.Length}
	need(r.u32() == version && r.u32() == 0xcdcdcdcd, "Unsupported "+name+" pool version.")
	r.skip(24)
	return sec, r
}
func (s *Save) emblems() emblemLayout {
	sec, r := s.pool("GDBD", 2)
	count := int(r.u32())
	need(count <= 64 && count <= (r.end-r.pos)/11, "Invalid Emblem bond holder count.")
	l := emblemLayout{section: sec, emblems: []SavedEmblem{}, bonds: []bondLocation{}, holders: []bondHolder{}}
	instances := map[uint32]bool{}
	ids := map[string]bool{}
	for range count {
		instance := r.u32()
		gid := r.str()
		need(gid != nil, "An Emblem has no ID.")
		need(instance != 0 && !instances[instance] && !ids[*gid], "Duplicate or empty Emblem instance.")
		instances[instance] = true
		ids[*gid] = true
		special := r.byte()
		need(special <= 1, "Unsupported Pact Ring record.")
		var partner *string
		if special == 1 {
			need(r.u32() == 0, "Unsupported Pact Ring record.")
			partner = r.str()
		}
		countOffset := r.pos
		bonds := int(r.u16())
		need(bonds <= 250 && bonds <= (r.end-r.pos)/20, "Invalid Emblem bond count.")
		entries := make([]EmblemBond, bonds)
		people := map[string]bool{}
		for i := range bonds {
			pid := r.str()
			need(pid != nil, "A bond has no character ID.")
			need(!people[*pid] && r.u32() == 3, "Duplicate character bond or unsupported bond version.")
			people[*pid] = true
			offset := r.pos
			level, exp := int(r.byte()), int(r.u16())
			need(r.u32() == 2, "Unsupported Emblem inherited-skill version.")
			n := int(r.u32())
			need(n <= 1280 && n <= (r.end-r.pos-1)/4, "Invalid Emblem inherited-skill count.")
			hashes := make([]uint32, n)
			for j := range n {
				hashes[j] = r.u32()
			}
			flagsOffset := r.pos
			entry := EmblemBond{*pid, level, exp, hashes, r.byte()}
			entries[i] = entry
			l.bonds = append(l.bonds, bondLocation{instance, entry, offset, flagsOffset})
		}
		l.emblems = append(l.emblems, SavedEmblem{instance, *gid, partner, entries})
		l.holders = append(l.holders, bondHolder{instance, countOffset, r.pos})
	}
	need(r.pos == r.end, "The Emblem bond pool has unrecognized trailing data.")
	return l
}
func (s *Save) Emblems() (out []SavedEmblem, err error) {
	defer recoverError(&err)
	return s.emblems().emblems, nil
}
func (s *Save) withVariableFlag(key string, bits int, preserveNonzero bool) *Save {
	l := s.variables()
	old := s.integer(l, key)
	if preserveNonzero && old != 0 || old|bits == old {
		return s
	}
	return s.setIntegerValues([]GameVariable{{key, old | bits}})
}
func MaximumBondLevel(emblem SavedEmblem, person string) int {
	if emblem.EmblemId == "GID_リュール" && emblem.PactPartner != nil && *emblem.PactPartner == person && person != "" {
		return 21
	}
	return 20
}
func SelectableBondLevels(emblem SavedEmblem, person string) []int {
	if emblem.EmblemId == "GID_リュール" {
		if MaximumBondLevel(emblem, person) == 21 {
			return []int{1, 5, 10, 20, 21}
		}
		return []int{1, 5, 10, 20}
	}
	out := make([]int, 20)
	for i := range out {
		out[i] = i + 1
	}
	return out
}
func BondLevelForExperience(exp int) int {
	need(exp >= 0 && exp <= 208, "Bond EXP must be 0–208.")
	level := 0
	for _, threshold := range catalog.BondExperience {
		if threshold <= exp {
			level++
		}
	}
	return level
}
func BondExperienceForLevel(emblem SavedEmblem, person string, level int) int {
	need(slices.Contains(SelectableBondLevels(emblem, person), level), "This bond level is not available for the selected character and Emblem.")
	if level == 21 {
		return 209
	}
	return catalog.BondExperience[level-1]
}
func (s *Save) WithEmblemBond(instance uint32, person string, level int, experience *int) (out *Save, err error) {
	defer recoverError(&err)
	need(catalog.Person(catalog.Hash(person)) != nil, "Select a known playable character bond.")
	l := s.emblems()
	var emblem *SavedEmblem
	for _, v := range l.emblems {
		if v.InstanceId == instance {
			copy := v
			emblem = &copy
		}
	}
	need(emblem != nil, "Select an existing Emblem instance.")
	def := catalog.Emblem(emblem.EmblemId)
	need(def != nil, "The selected Emblem is not in the verified catalog.")
	var location *bondLocation
	for _, v := range l.bonds {
		if v.instance == instance && v.bond.PersonId == person {
			copy := v
			location = &copy
		}
	}
	need(location != nil, "The selected character has no saved bond with this Emblem.")
	threshold := BondExperienceForLevel(*emblem, person, level)
	exp := threshold
	if experience != nil {
		exp = *experience
	}
	if level == 21 {
		need(exp == threshold, "Pact bond EXP does not match the native threshold.")
	} else {
		need(BondLevelForExperience(exp) == level, "Bond level and EXP do not match the game's thresholds.")
	}
	need(location.bond.Level <= 20 || level == 21, "A special Pact Ring bond cannot be replaced with a normal bond level.")
	need(location.bond.Level <= 21, "An unrecognized special bond cannot be edited.")
	edited := s
	if location.bond.Level != level || location.bond.Experience != exp {
		b := bytes.Clone(s.data)
		b[location.valuesOffset] = byte(level)
		put16(b, location.valuesOffset+1, uint16(exp))
		if location.bond.Level != level {
			flags := 0
			if level >= 5 {
				flags |= 2
			}
			if level >= 10 {
				flags |= 4
			}
			if level >= 20 {
				flags |= 8
			}
			b[location.flagsOffset] = (b[location.flagsOffset] &^ 14) | byte(flags)
		}
		edited = verified(b)
	}
	if level >= 11 && def.LevelCapVariable != nil {
		edited = edited.withVariableFlag(*def.LevelCapVariable, 1, true)
	}
	if emblem.EmblemId == "GID_リュール" {
		edited = edited.withAlearSupport(person, level)
	}
	return edited, nil
}
func (s *Save) MaximizeEmblemBonds(instance uint32, person string) (out *Save, err error) {
	defer recoverError(&err)
	var emblem *SavedEmblem
	for _, v := range s.emblems().emblems {
		if v.InstanceId == instance {
			copy := v
			emblem = &copy
		}
	}
	need(emblem != nil && catalog.Emblem(emblem.EmblemId) != nil, "Select an existing verified Emblem instance.")
	edited := s
	found := person == ""
	for _, bond := range emblem.Bonds {
		if person != "" && bond.PersonId != person {
			continue
		}
		found = true
		maximum := MaximumBondLevel(*emblem, bond.PersonId)
		if catalog.Person(catalog.Hash(bond.PersonId)) == nil || bond.Level > 21 || bond.Level > maximum {
			need(person == "", "Unknown character or special bond limits cannot be guessed.")
			continue
		}
		var e error
		edited, e = edited.WithEmblemBond(instance, bond.PersonId, maximum, nil)
		if e != nil {
			return nil, e
		}
	}
	need(found, "Select an existing character bond.")
	return edited, nil
}
