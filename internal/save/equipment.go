package save

import (
	"bytes"
	"encoding/binary"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type EquipmentKind int

const (
	EquipmentNone EquipmentKind = iota
	EquipmentEmblem
	EquipmentBondRing
)

type EquipmentSelection struct {
	Kind       EquipmentKind
	InstanceId uint32
}
type EquipmentOption struct {
	Selection  EquipmentSelection
	EmblemId   *string
	RingHash   *uint32
	OwnerIndex *int
	StockCount int
}

func equipmentCharacter(c RosterCharacter) bool {
	return c.Force != Enemy && c.Force != Temporary && catalog.Person(c.PersonHash) != nil
}
func (s *Save) rosterEquipment(index int) EquipmentSelection {
	locations := s.equipmentLocations()
	need(index >= 0 && index < len(locations), "Invalid character index.")
	link := locations[index].links
	need(link.RingInstance == 0 || link.EmblemInstance == 0, "A character has both an Emblem and a bond ring equipped.")
	if link.EmblemInstance != 0 {
		return EquipmentSelection{EquipmentEmblem, link.EmblemInstance}
	}
	if link.RingInstance != 0 {
		return EquipmentSelection{EquipmentBondRing, link.RingInstance}
	}
	return EquipmentSelection{}
}
func (s *Save) RosterEquipment(index int) (out EquipmentSelection, err error) {
	defer recoverError(&err)
	return s.rosterEquipment(index), nil
}
func validateEquipmentLinks(links []equipmentLocation, gods []ownedEmblem) {
	used := map[uint32]bool{}
	for _, v := range links {
		link := v.links
		if link.EmblemInstance != 0 {
			found := false
			for _, god := range gods {
				found = found || god.instance == link.EmblemInstance
			}
			need(!used[link.EmblemInstance] && found, "An Emblem link is missing or has multiple owners.")
			used[link.EmblemInstance] = true
		}
		need(link.RingInstance == 0 || link.EmblemInstance == 0, "A character has both an Emblem and a bond ring equipped.")
	}
}
func (s *Save) equipmentOptions(index int) []EquipmentOption {
	roster := s.roster()
	c := roster.get(index).character
	locations := s.equipmentLocations()
	gods := s.ownedEmblems()
	bonds := s.emblems()
	rings := s.rings()
	validateEquipmentLinks(locations, gods)
	out := []EquipmentOption{{Selection: EquipmentSelection{}}}
	if !equipmentCharacter(c) || locations[index].links.PartnerEmblemInstance != 0 {
		return out
	}
	person := catalog.Person(c.PersonHash)
	for _, god := range gods {
		var holder *SavedEmblem
		for _, v := range bonds.emblems {
			if v.InstanceId == god.holder {
				copy := v
				holder = &copy
			}
		}
		if god.darkness || god.reserved || god.escaping || holder == nil || catalog.Hash(holder.EmblemId) != god.godHash || catalog.Emblem(holder.EmblemId) == nil || holder.EmblemId == "GID_リュール" {
			continue
		}
		hasBond := false
		for _, v := range holder.Bonds {
			hasBond = hasBond || v.PersonId == person.Id
		}
		if !hasBond {
			continue
		}
		var owner *int
		partner := false
		for _, v := range locations {
			partner = partner || v.links.PartnerEmblemInstance == god.instance
			if v.links.EmblemInstance == god.instance {
				i := v.links.CharacterIndex
				owner = &i
			}
		}
		if partner || owner != nil && (!equipmentCharacter(roster.characters[*owner].character) || locations[*owner].links.PartnerEmblemInstance != 0) {
			continue
		}
		id := holder.EmblemId
		out = append(out, EquipmentOption{EquipmentSelection{EquipmentEmblem, god.instance}, &id, nil, owner, 1})
	}
	for _, v := range rings.entries {
		ring := v.ring
		if ring.StockCount < 1 || ring.StockCount > 99 || catalog.BondRing(ring.RingHash) == nil {
			continue
		}
		if ring.OwnerIndex != nil && (!equipmentCharacter(roster.characters[*ring.OwnerIndex].character) || locations[*ring.OwnerIndex].links.PartnerEmblemInstance != 0) {
			continue
		}
		hash := ring.RingHash
		out = append(out, EquipmentOption{EquipmentSelection{EquipmentBondRing, ring.InstanceId}, nil, &hash, ring.OwnerIndex, ring.StockCount})
	}
	return out
}
func (s *Save) RosterEquipmentOptions(index int) (out []EquipmentOption, err error) {
	defer recoverError(&err)
	return s.equipmentOptions(index), nil
}
func ringTotals(rings []BondRing) map[uint32]int {
	out := map[uint32]int{}
	for _, v := range rings {
		out[v.RingHash] += v.StockCount
	}
	return out
}
func validateRingEquipment(rings []BondRing) {
	need(len(rings) <= 750, "The bond ring pool is full.")
	equipped := 0
	for _, v := range rings {
		need(v.InstanceId >= 1 && v.InstanceId <= 750 && v.StockCount >= 0 && v.StockCount <= 99 && (v.OwnerIndex == nil || v.StockCount == 1), "Invalid bond ring stock, ownership or pool capacity.")
		if v.OwnerIndex != nil {
			equipped++
		}
	}
	need(equipped <= 50 && len(rings)-equipped <= 700, "Invalid bond ring pool capacity.")
	for _, n := range ringTotals(rings) {
		need(n <= 99, "Identical rings exceed the native stock limit.")
	}
}
func freeRingInstance(rings []BondRing) uint32 {
	used := map[uint32]bool{}
	for _, v := range rings {
		used[v.InstanceId] = true
	}
	for id := uint32(1); id <= 750; id++ {
		if !used[id] {
			return id
		}
	}
	panic(dataError("The bond ring pool has no free equipment slot."))
}
func returnEquippedRing(rings []BondRing, instance uint32) []BondRing {
	selected := -1
	for i, v := range rings {
		if v.InstanceId == instance {
			selected = i
		}
	}
	need(selected >= 0, "The equipped ring is missing.")
	ring := rings[selected]
	need(catalog.BondRing(ring.RingHash) != nil, "The current unknown bond ring cannot be reassigned.")
	for i, v := range rings {
		if v.RingHash == ring.RingHash && v.OwnerIndex == nil {
			rings[i].StockCount++
			return append(rings[:selected], rings[selected+1:]...)
		}
	}
	rings[selected].OwnerIndex = nil
	return rings
}
func (s *Save) WithRosterEquipment(index int, selection EquipmentSelection) (out *Save, err error) {
	defer recoverError(&err)
	need(selection.Kind >= EquipmentNone && selection.Kind <= EquipmentBondRing && (selection.Kind == EquipmentNone) == (selection.InstanceId == 0), "Choose none, an existing Emblem, or an existing bond ring.")
	roster := s.roster()
	c := roster.get(index).character
	locations := s.equipmentLocations()
	current := s.rosterEquipment(index)
	if selection == current {
		return s, nil
	}
	need(equipmentCharacter(c), "Select a known playable character.")
	need(locations[index].links.PartnerEmblemInstance == 0, "End the character's Engage+ link in-game before changing equipment.")
	options := s.equipmentOptions(index)
	var selected *EquipmentOption
	currentAvailable := false
	for _, v := range options {
		if v.Selection == selection {
			copy := v
			selected = &copy
		}
		currentAvailable = currentAvailable || v.Selection == current
	}
	need(selected != nil, "This equipment is unavailable for the selected character.")
	ringLayout := s.rings()
	rings := []BondRing{}
	for _, v := range ringLayout.entries {
		rings = append(rings, v.ring)
	}
	validateRingEquipment(rings)
	totals := ringTotals(rings)
	p := roster.section.PayloadOffset()
	unit := bytes.Clone(s.data[p : p+roster.section.Length])
	set := func(owner int, emblem, ring uint32) {
		loc := locations[owner]
		put32(unit, loc.linksOffset-p, emblem)
		put32(unit, loc.linksOffset+8-p, ring)
		clear(unit[loc.engageOffset-p : loc.engageOffset-p+2])
		offset := loc.statusOffset - p
		flags := binary.LittleEndian.Uint64(unit[offset:])
		binary.LittleEndian.PutUint64(unit[offset:], flags & ^uint64(0x07800080))
	}
	need(current.Kind != EquipmentEmblem || currentAvailable, "The current special or unknown Emblem cannot be reassigned.")
	if current.Kind == EquipmentBondRing {
		rings = returnEquippedRing(rings, current.InstanceId)
	}
	set(index, 0, 0)
	var equippedRing uint32
	if selection.Kind == EquipmentBondRing {
		found := -1
		for i, v := range rings {
			if v.InstanceId == selection.InstanceId {
				found = i
			}
		}
		need(found >= 0, "The selected ring is missing.")
		ring := rings[found]
		owner := index
		if selected.OwnerIndex != nil {
			set(*selected.OwnerIndex, 0, 0)
			rings[found].OwnerIndex = &owner
			equippedRing = ring.InstanceId
		} else if ring.StockCount == 1 {
			rings[found].OwnerIndex = &owner
			equippedRing = ring.InstanceId
		} else {
			rings[found].StockCount--
			equippedRing = freeRingInstance(rings)
			rings = append(rings, BondRing{equippedRing, ring.RingHash, 1, &owner})
		}
	} else if selection.Kind == EquipmentEmblem && selected.OwnerIndex != nil {
		set(*selected.OwnerIndex, 0, 0)
	}
	emblem := uint32(0)
	if selection.Kind == EquipmentEmblem {
		emblem = selection.InstanceId
	}
	set(index, emblem, equippedRing)
	validateRingEquipment(rings)
	edited := s.writeRingPool(ringLayout, rings)
	edited = edited.replaceSection(edited.Section("UNIT"), unit)
	expected := selection
	if selection.Kind == EquipmentBondRing {
		expected.InstanceId = equippedRing
	}
	need(edited.rosterEquipment(index) == expected, "Equipment did not survive serialization.")
	actual, e := edited.BondRings()
	if e != nil {
		return nil, e
	}
	after := ringTotals(actual)
	need(len(after) == len(totals), "Equipment changed saved ring stock.")
	for h, n := range totals {
		need(after[h] == n, "Equipment changed saved ring stock.")
	}
	edited.equipmentOptions(index)
	return edited, nil
}
