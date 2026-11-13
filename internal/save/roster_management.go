package save

import (
	"bytes"
	"encoding/binary"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

const protectedRecoveryStatus uint64 = 1<<3 | 1<<21 | 1<<22 | 1<<39 | 1<<45
const deadStatusMask uint64 = 0x10000a00

func (s *Save) validateOutOfBattle(l rosterLayout) {
	sec := s.Section("USER")
	need(sec.Length >= 37 && get32(s.data, sec.PayloadOffset()) == 20, "Roster management requires a verified USER section.")
	context := sec.PayloadOffset() + 32
	status, sequence := get32(s.data, context), s.data[context+4]
	need(status&2 == 0 && (sequence == 1 || sequence == 4 || sequence == 6), "Manage characters and Emblems in an out-of-battle chapter, Somniel or world-map save.")
	for _, v := range l.characters {
		need(v.character.Force > Ally, "Manage characters and Emblems in an out-of-battle save.")
	}
}
func (s *Save) uniqueCharacter(l rosterLayout, c RosterCharacter) {
	count := 0
	for _, v := range l.characters {
		if v.character.PersonHash == c.PersonHash {
			count++
		}
	}
	need(count == 1, "The save contains duplicate records for this character.")
}
func (s *Save) validateInactive(index int) rosterLayout {
	l := s.roster()
	s.validateOutOfBattle(l)
	c := l.get(index).character
	person := catalog.Person(c.PersonHash)
	need(person != nil && person.Id != "PID_リュール" && (c.Force == Absent || c.Force == Dead || c.Force == Lost) && c.StatusFlags&protectedRecoveryStatus == 0, "The protagonist, deployed, guest and story-restricted characters cannot be managed.")
	s.uniqueCharacter(l, c)
	need(s.equipmentLocations()[index].links.PartnerEmblemInstance == 0, "End Engage+ in-game before managing this character.")
	return l
}
func (s *Save) rewriteRosterPool(l rosterLayout, removed *int, destination *UnitForce, record []byte) *Save {
	p := l.section.PayloadOffset()
	b := bytes.Clone(s.data[p : p+32])
	for force := Player; force <= Temporary; force++ {
		group := []characterLayout{}
		for _, v := range l.characters {
			if (removed == nil || v.character.Index != *removed) && v.character.Force == force {
				group = append(group, v)
			}
		}
		add := record != nil && destination != nil && *destination == force
		count := len(group)
		if add {
			count++
		}
		if count == 0 {
			continue
		}
		need(count <= 255, "The destination force is full.")
		b = append(b, byte(force), byte(count))
		for _, c := range group {
			b = append(b, s.data[c.start:c.end]...)
		}
		if add {
			b = append(b, record...)
		}
	}
	b = append(b, 255)
	return s.replaceSection(l.section, b)
}
func (s *Save) WithRosterForce(index int, force UnitForce) (out *Save, err error) {
	defer recoverError(&err)
	need(force == Absent || force == Dead || force == Lost, "Choose an inactive force: Absent, Dead or Lost.")
	l := s.validateInactive(index)
	c := l.get(index)
	if c.character.Force == force {
		return s, nil
	}
	edited := s.rewriteRosterPool(l, &index, &force, s.data[c.start:c.end])
	edited.equipmentLocations()
	return edited, nil
}
func (s *Save) validateRecovery(l rosterLayout, c RosterCharacter) {
	need(catalog.Person(c.PersonHash) != nil && (c.Force == Absent || c.Force == Dead || c.Force == Lost) && (c.Availability() == "Dead" || c.Availability() == "Lost"), "Select an existing dead or lost playable character.")
	need(c.StatusFlags&protectedRecoveryStatus == 0, "Guest, summoned, relay or story-restricted characters cannot be restored.")
	need(catalog.Class(c.ClassHash) != nil, "Character restoration requires a verified class for maximum HP.")
	s.uniqueCharacter(l, c)
	s.validateOutOfBattle(l)
}
func (s *Save) CanRestoreRosterCharacter(index int) bool {
	_, e := func() (out bool, err error) {
		defer recoverError(&err)
		l := s.roster()
		s.validateRecovery(l, l.get(index).character)
		return true, nil
	}()
	return e == nil
}
func (s *Save) RestoreRosterCharacter(index int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.roster()
	c := l.get(index)
	s.validateRecovery(l, c.character)
	record := bytes.Clone(s.data[c.start:c.end])
	binary.LittleEndian.PutUint64(record[36:], c.character.StatusFlags & ^deadStatusMask)
	record[c.levelOffset+2-c.start] = byte(clamp(c.character.Stats[0].Value+c.progress.hpBonus, 1, 255))
	if c.character.Force == Absent {
		p := l.section.PayloadOffset()
		b := bytes.Clone(s.data[p : p+l.section.Length])
		copy(b[c.start-p:], record)
		return s.replaceSection(l.section, b), nil
	}
	destination := Absent
	return s.rewriteRosterPool(l, &index, &destination, record), nil
}
func (s *Save) validateClassRepair(index int) *catalog.ClassDefinition {
	l := s.roster()
	s.validateOutOfBattle(l)
	c := l.get(index).character
	need(catalog.Class(c.ClassHash) == nil && (c.Force == Absent || c.Force == Dead || c.Force == Lost) && c.StatusFlags&protectedRecoveryStatus == 0, "Select an inactive playable character with a missing or unknown class.")
	person := catalog.Person(c.PersonHash)
	need(person != nil, "Unknown characters cannot be repaired.")
	s.uniqueCharacter(l, c)
	for _, job := range catalog.ClassesFor(person.Hash(), c.Progress.Gender) {
		if job.Id == person.BirthClass {
			copy := job
			return &copy
		}
	}
	panic(dataError("This character has no verified starting class."))
}
func (s *Save) CanRepairRosterClass(index int) bool {
	_, e := func() (out bool, err error) { defer recoverError(&err); s.validateClassRepair(index); return true, nil }()
	return e == nil
}
func (s *Save) RepairRosterClass(index int) (out *Save, err error) {
	defer recoverError(&err)
	job := s.validateClassRepair(index)
	l := s.roster()
	c := l.get(index)
	h := job.Hash()
	edited := s.replaceCharacter(l, c, c.classStart, c.classEnd, reference(&h))
	c = edited.roster().get(index)
	b := bytes.Clone(edited.data)
	level := clamp(c.character.Values.Level, 1, job.MaxLevel)
	b[c.levelOffset] = byte(level)
	exp := min(c.character.Values.Experience, 99)
	if level == job.MaxLevel {
		exp = 0
	}
	b[c.levelOffset+1] = byte(exp)
	weapons := job.WeaponVariants()[0]
	put32(b, c.progress.masksOffset+4, c.character.Progress.Proficiencies|weapons)
	put32(b, c.progress.masksOffset+8, weapons)
	b[c.levelOffset+2] = byte(min(c.character.Progress.CurrentHP, clamp(c.character.Stats[0].Value+c.progress.hpBonus, 1, 255)))
	return verified(b).WithRosterClassSkill(index, false)
}
func (s *Save) RepairAllRosterClasses() (out *Save, err error) {
	defer recoverError(&err)
	edited := s
	for _, c := range s.roster().characters {
		if edited.CanRepairRosterClass(c.character.Index) {
			var e error
			edited, e = edited.RepairRosterClass(c.character.Index)
			if e != nil {
				return nil, e
			}
		}
	}
	return edited, nil
}
func (s *Save) CanMoveRosterCharacter(index int) bool {
	_, e := func() (out bool, err error) { defer recoverError(&err); s.validateInactive(index); return true, nil }()
	return e == nil
}
func (s *Save) validateRosterDeletion(index int) {
	l := s.validateInactive(index)
	c := l.get(index).character
	for _, slot := range c.Items {
		if slot.Item != nil {
			def := catalog.RosterItem(slot.Item.ItemHash)
			need(def != nil && def.EngageOnly, "Move the character's items to the convoy or clear the item slots before deletion.")
		}
	}
	pid := catalog.Person(c.PersonHash).Id
	for _, v := range s.emblems().emblems {
		need(v.PactPartner == nil || *v.PactPartner != pid, "Pact partners cannot be deleted.")
	}
	for _, v := range l.characters {
		need(v.attackTarget == nil || *v.attackTarget != c.PersonHash, "Characters with saved target links cannot be deleted.")
		need(get32(s.data, v.end-6) == 0, "Characters with saved owner links cannot be deleted.")
	}
	for _, v := range s.equipmentLocations() {
		need(v.target == nil || *v.target != c.PersonHash, "Characters with saved target links cannot be deleted.")
	}
	selection := s.rosterEquipment(index)
	available := false
	for _, v := range s.equipmentOptions(index) {
		available = available || v.Selection == selection
	}
	need(available, "The character's special equipment cannot be safely returned.")
}
func (s *Save) CanDeleteRosterCharacter(index int) bool {
	_, e := func() (out bool, err error) {
		defer recoverError(&err)
		s.validateRosterDeletion(index)
		return true, nil
	}()
	return e == nil
}
func (s *Save) WithoutRosterCharacter(index int) (out *Save, err error) {
	defer recoverError(&err)
	s.validateRosterDeletion(index)
	edited, e := s.WithRosterEquipment(index, EquipmentSelection{})
	if e != nil {
		return nil, e
	}
	l := edited.roster()
	out = edited.rewriteRosterPool(l, &index, nil, nil)
	out.equipmentLocations()
	return out, nil
}
