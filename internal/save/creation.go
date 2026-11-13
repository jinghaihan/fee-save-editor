package save

import (
	"bytes"
	"crypto/rand"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type builder struct{ b []byte }

func (w *builder) u32(v uint32)  { p := len(w.b); w.b = append(w.b, 0, 0, 0, 0); put32(w.b, p, v) }
func (w *builder) u16(v uint16)  { p := len(w.b); w.b = append(w.b, 0, 0); put16(w.b, p, v) }
func (w *builder) byte(v byte)   { w.b = append(w.b, v) }
func (w *builder) zeros(n int)   { w.b = append(w.b, make([]byte, n)...) }
func (w *builder) str(v string)  { w.b = append(w.b, wide(v)...) }
func (w *builder) ref(v *uint32) { w.b = append(w.b, reference(v)...) }
func (w *builder) random(n int) {
	b := make([]byte, n)
	_, e := rand.Read(b)
	need(e == nil, "Could not generate new character identifiers.")
	w.b = append(w.b, b...)
}
func (s *Save) validateEmblemCreation(gods []ownedEmblem, holders []SavedEmblem) {
	need(len(gods) <= 128 && len(holders) <= 64, "Invalid Emblem pool capacity.")
	hashes := map[uint32]bool{}
	for _, v := range holders {
		need(v.InstanceId >= 1 && v.InstanceId <= 64 && len(v.Bonds) <= 48, "Invalid Emblem bond holder capacity.")
	}
	for _, god := range gods {
		need(god.instance >= 1 && god.instance <= 128 && !hashes[god.godHash], "Invalid Emblem ownership.")
		hashes[god.godHash] = true
		found := false
		for _, h := range holders {
			found = found || h.InstanceId == god.holder && catalog.Hash(h.EmblemId) == god.godHash
		}
		need(found, "Invalid Emblem bond references.")
	}
	links := s.equipmentLocations()
	validateEquipmentLinks(links, gods)
	for _, v := range links {
		if v.links.PartnerEmblemInstance == 0 {
			continue
		}
		found := false
		for _, god := range gods {
			found = found || god.instance == v.links.PartnerEmblemInstance
		}
		need(found, "An Engage+ link references a missing Emblem instance.")
	}
}
func (s *Save) MissingEmblems() (out []catalog.EmblemDefinition, err error) {
	defer recoverError(&err)
	gods := s.ownedEmblems()
	s.validateEmblemCreation(gods, s.emblems().emblems)
	owned := map[uint32]bool{}
	for _, v := range gods {
		owned[v.godHash] = true
	}
	out = []catalog.EmblemDefinition{}
	for _, v := range catalog.Emblems {
		if catalog.EmblemWeapons[v.Id] != nil && !owned[catalog.Hash(v.Id)] {
			out = append(out, v)
		}
	}
	return out, nil
}
func initialBond(person string) []byte {
	w := builder{}
	w.str(person)
	w.u32(3)
	w.byte(1)
	w.u16(0)
	w.u32(2)
	w.u32(0)
	w.byte(0)
	return w.b
}
func freeInstance(used map[uint32]bool, capacity uint32) uint32 {
	for i := uint32(1); i <= capacity; i++ {
		if !used[i] {
			return i
		}
	}
	panic(dataError("The Emblem pool has no free slot."))
}
func (s *Save) appendEmblemRecord(name string, record []byte, count int) *Save {
	sec := s.Section(name)
	p := sec.PayloadOffset()
	b := bytes.Clone(s.data[p : p+sec.Length])
	put32(b, 32, uint32(count+1))
	b = append(b, record...)
	return s.replaceSection(sec, b)
}
func (s *Save) WithAddedEmblem(id string) (out *Save, err error) {
	defer recoverError(&err)
	weapons := catalog.EmblemWeapons[id]
	need(weapons != nil, "Select a normal base-game or DLC Emblem. Engage+ cannot be created as a normal ring.")
	gods := s.ownedEmblems()
	holders := s.emblems().emblems
	s.validateEmblemCreation(gods, holders)
	hash := catalog.Hash(id)
	usedGods := map[uint32]bool{}
	for _, v := range gods {
		if v.godHash == hash {
			return s, nil
		}
		usedGods[v.instance] = true
	}
	instance := freeInstance(usedGods, 128)
	var holderID uint32
	usedHolders := map[uint32]bool{}
	for _, v := range holders {
		usedHolders[v.InstanceId] = true
		if v.EmblemId == id {
			holderID = v.InstanceId
		}
	}
	edited := s
	if holderID == 0 {
		holderID = freeInstance(usedHolders, 64)
		people := []string{}
		seen := map[string]bool{}
		for _, v := range s.roster().characters {
			c := v.character
			if c.Force != Player && c.Force != Absent && c.Force != Dead && c.Force != Lost {
				continue
			}
			person := catalog.Person(c.PersonHash)
			if person != nil && !seen[person.Id] {
				seen[person.Id] = true
				people = append(people, person.Id)
			}
		}
		need(len(people) <= 48, "The new Emblem has too many character bonds.")
		w := builder{}
		w.u32(holderID)
		w.str(id)
		w.byte(0)
		w.u16(uint16(len(people)))
		for _, person := range people {
			w.b = append(w.b, initialBond(person)...)
		}
		edited = edited.appendEmblemRecord("GDBD", w.b, len(holders))
	}
	w := builder{}
	w.u32(instance)
	w.ref(&hash)
	w.u32(holderID)
	w.zeros(5)
	w.byte(byte(len(weapons)))
	for _, weapon := range weapons {
		w.str(weapon)
		w.b = append(w.b, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1)
		w.u32(0xffffffff)
	}
	edited = edited.appendEmblemRecord("GOD", w.b, len(gods))
	edited.validateEmblemCreation(edited.ownedEmblems(), edited.emblems().emblems)
	return edited, nil
}
func (s *Save) validateEmblemRemoval(instance uint32) {
	s.validateOutOfBattle(s.roster())
	gods := s.ownedEmblems()
	s.validateEmblemCreation(gods, s.emblems().emblems)
	var selected *ownedEmblem
	for _, v := range gods {
		if v.instance == instance {
			copy := v
			selected = &copy
		}
	}
	need(selected != nil, "Select an owned Emblem instance.")
	known := false
	for id := range catalog.EmblemWeapons {
		known = known || catalog.Hash(id) == selected.godHash
	}
	need(known && !selected.darkness && !selected.reserved && !selected.escaping, "Story-reserved, unknown and Engage+ Emblems cannot be removed.")
	for _, v := range s.equipmentLocations() {
		need(v.links.PartnerEmblemInstance != instance && (v.links.EmblemInstance != instance || v.links.PartnerEmblemInstance == 0), "Engage+ Emblems cannot be removed.")
	}
}
func (s *Save) CanRemoveEmblem(instance uint32) bool {
	_, e := func() (out bool, err error) {
		defer recoverError(&err)
		s.validateEmblemRemoval(instance)
		return true, nil
	}()
	return e == nil
}
func (s *Save) WithoutEmblem(instance uint32) (out *Save, err error) {
	defer recoverError(&err)
	s.validateEmblemRemoval(instance)
	edited := s
	for _, v := range s.equipmentLocations() {
		if v.links.EmblemInstance == instance {
			var e error
			edited, e = edited.WithRosterEquipment(v.links.CharacterIndex, EquipmentSelection{})
			if e != nil {
				return nil, e
			}
			break
		}
	}
	gods := edited.ownedEmblems()
	sec := edited.Section("GOD")
	p := sec.PayloadOffset()
	b := bytes.Clone(edited.data[p : p+sec.Length])
	for _, v := range gods {
		if v.instance == instance {
			b = splice(b, v.start-p, v.end-p, nil)
			break
		}
	}
	put32(b, 32, uint32(len(gods)-1))
	edited = edited.replaceSection(sec, b)
	edited.validateEmblemCreation(edited.ownedEmblems(), edited.emblems().emblems)
	return edited, nil
}
func (s *Save) MissingRosterCharacters() (out []catalog.PersonDefinition, err error) {
	defer recoverError(&err)
	l := s.roster()
	s.validateOutOfBattle(l)
	out = []catalog.PersonDefinition{}
	if len(l.characters) >= 250 {
		return out, nil
	}
	present := map[uint32]bool{}
	for _, v := range l.characters {
		present[v.character.PersonHash] = true
	}
	for _, person := range catalog.Persons {
		if person.Id != "PID_リュール" && !present[person.Hash()] {
			out = append(out, person)
		}
	}
	return out, nil
}
func newRosterCharacter(person catalog.PersonDefinition, job catalog.ClassDefinition, d catalog.RosterCreationDefaults, stats []int) []byte {
	w := builder{}
	w.u32(0)
	w.u32(40)
	w.u32(0xcdcdcdcd)
	w.zeros(32)
	pid, jid := person.Hash(), job.Hash()
	w.ref(&pid)
	w.ref(&jid)
	w.u32(0)
	for _, v := range stats {
		w.byte(byte(int8(v)))
	}
	for range 2 {
		w.u32(0)
		w.zeros(11)
	}
	w.random(8)
	w.byte(1)
	w.byte(0)
	w.byte(byte(calculateStat(HP, stats[0], &job, &person).Value))
	w.u32(0xffffffff)
	w.u32(0)
	w.byte(0)
	w.u32(0)
	w.u32(2)
	w.byte(8)
	for range 8 {
		w.u32(5)
		w.byte(0)
	}
	w.u32(0)
	for range 4 {
		w.ref(nil)
	}
	w.u32(0)
	for range 3 {
		w.u32(1)
		w.u32(0)
	}
	w.byte(0)
	weapons := job.WeaponVariants()[0]
	w.u32(d.OriginalProficiencies)
	w.u32(d.Proficiencies | weapons)
	w.u32(weapons)
	w.u32(6)
	for i := range 4 {
		if i == 3 {
			w.u32(1)
		}
		w.u32(1)
		w.u32(11)
		w.zeros(44)
	}
	w.byte(byte(int8(d.InternalLevel)))
	w.byte(2)
	w.byte(0)
	w.u16(0)
	w.byte(4)
	w.byte(0)
	w.u16(0)
	w.byte(10)
	w.zeros(10)
	w.u16(0)
	w.zeros(16)
	w.byte(0)
	w.random(4)
	w.byte(255)
	w.byte(0)
	w.u16(uint16(d.SkillPoints))
	w.u32(0)
	w.byte(0)
	w.byte(0)
	put32(w.b, 0, uint32(len(w.b)))
	return w.b
}
func (s *Save) withNewCharacterBonds(person string) *Save {
	edited := s
	owned := map[uint32]bool{}
	for _, god := range s.ownedEmblems() {
		owned[god.holder] = true
	}
	for _, holder := range s.emblems().emblems {
		if !owned[holder.InstanceId] || catalog.EmblemWeapons[holder.EmblemId] == nil {
			continue
		}
		has := false
		for _, bond := range holder.Bonds {
			has = has || bond.PersonId == person
		}
		if has {
			continue
		}
		need(len(holder.Bonds) < 48, "The Emblem holder has no free character-bond slot.")
		l := edited.emblems()
		var loc bondHolder
		for _, v := range l.holders {
			if v.instance == holder.InstanceId {
				loc = v
			}
		}
		p := l.section.PayloadOffset()
		b := splice(edited.data[p:p+l.section.Length], loc.end-p, loc.end-p, initialBond(person))
		put16(b, loc.countOffset-p, uint16(len(holder.Bonds)+1))
		edited = edited.replaceSection(l.section, b)
	}
	edited.emblems()
	return edited
}
func (s *Save) WithAddedRosterCharacter(id string) (out *Save, err error) {
	defer recoverError(&err)
	missing, e := s.MissingRosterCharacters()
	if e != nil {
		return nil, e
	}
	var person *catalog.PersonDefinition
	for _, v := range missing {
		if v.Id == id {
			copy := v
			person = &copy
		}
	}
	need(person != nil, "Choose a missing playable character; the protagonist and duplicates cannot be added.")
	s.validateEmblemCreation(s.ownedEmblems(), s.emblems().emblems)
	var job *catalog.ClassDefinition
	for _, v := range catalog.ClassesFor(person.Hash(), person.Gender) {
		if v.Id == person.BirthClass {
			copy := v
			job = &copy
		}
	}
	need(job != nil, "This character has no verified starting class.")
	d, ok := catalog.RosterDefaults[id]
	need(ok, "Select a playable character with verified creation defaults.")
	main, e := s.Main()
	if e != nil {
		return nil, e
	}
	difficulty := 0
	switch main.Difficulty {
	case "Hard":
		difficulty = 1
	case "Maddening":
		difficulty = 2
	}
	record := newRosterCharacter(*person, *job, d, d.PersonalStats[difficulty])
	destination := Absent
	edited := s.rewriteRosterPool(s.roster(), nil, &destination, record)
	edited.equipmentLocations()
	return edited.withNewCharacterBonds(id), nil
}
