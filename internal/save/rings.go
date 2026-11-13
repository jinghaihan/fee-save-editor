package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type equipmentLocation struct {
	links                                   CharacterRingLinks
	linksOffset, engageOffset, statusOffset int
	target                                  *uint32
}

func (s *Save) equipmentLocations() []equipmentLocation {
	has := false
	for _, sec := range s.Sections {
		has = has || sec.Name == "UNIT"
	}
	if !has {
		return []equipmentLocation{}
	}
	out := []equipmentLocation{}
	used := map[uint32]bool{}
	for _, c := range s.roster().characters {
		r := reader{s.data, c.progress.tailStart, c.end}
		r.skip(2)
		need(r.byte() == 4, "Unsupported unit battle-data version.")
		stats := int(r.byte())
		need(stats <= 43, "Invalid unit battle-data count.")
		r.skip(stats*8 + 2)
		weapons := int(r.byte())
		need(weapons <= 10, "Invalid unit weapon-rank count.")
		r.skip(weapons + 2)
		offset := r.pos
		emblem, partner, ring := r.u32(), r.u32(), r.u32()
		r.skip(4)
		present := r.byte()
		need(present <= 1, "Invalid optional unit target.")
		var target *uint32
		if present == 1 {
			target = r.ref()
		}
		r.skip(14)
		need(r.pos == r.end, "Unrecognized character equipment trailer.")
		if ring != 0 {
			need(!used[ring], "A ring instance has multiple owners.")
			used[ring] = true
		}
		out = append(out, equipmentLocation{CharacterRingLinks{c.character.Index, emblem, partner, ring}, offset, c.progress.tailStart, c.start + 36, target})
	}
	return out
}
func (s *Save) CharacterRingLinks() (out []CharacterRingLinks, err error) {
	defer recoverError(&err)
	out = []CharacterRingLinks{}
	for _, v := range s.equipmentLocations() {
		out = append(out, v.links)
	}
	return out, nil
}

type ownedEmblem struct {
	instance, godHash, holder    uint32
	darkness, reserved, escaping bool
	dirtiness                    byte
	dirtOffset, start, end       int
}

func (s *Save) ownedEmblems() []ownedEmblem {
	_, r := s.pool("GOD", 8)
	count := int(r.u32())
	need(count <= 128, "Invalid owned Emblem count.")
	out := []ownedEmblem{}
	used := map[uint32]bool{}
	for range count {
		start := r.pos
		id := r.u32()
		need(id != 0 && !used[id], "Invalid owned Emblem instance.")
		used[id] = true
		hash := r.ref()
		need(hash != nil, "An Emblem has no game-data reference.")
		holder := r.u32()
		dark, reserved, escaping := r.byte(), r.byte(), r.byte()
		need(dark <= 1 && reserved <= 1 && escaping <= 1, "Invalid owned Emblem flags.")
		offset := r.pos
		dirt := r.byte()
		synchro := int(r.byte())
		for range synchro {
			need(r.str() != nil, "Invalid Emblem synchro key.")
			r.skip(2)
		}
		weapons := int(r.byte())
		for range weapons {
			need(r.str() != nil, "Invalid Emblem weapon key.")
			r.skip(10)
			r.str()
		}
		out = append(out, ownedEmblem{id, *hash, holder, dark != 0, reserved != 0, escaping != 0, dirt, offset, start, r.pos})
	}
	need(r.pos == r.end, "Unrecognized owned Emblem data.")
	return out
}

type EmblemCondition struct {
	InstanceId, BondHolderId uint32
	EmblemId                 string
	Dirtiness                int
}

func (s *Save) EmblemConditions() (out []EmblemCondition, err error) {
	defer recoverError(&err)
	out = []EmblemCondition{}
	for _, v := range s.ownedEmblems() {
		for _, d := range catalog.Emblems {
			if catalog.Hash(d.Id) == v.godHash && d.Id != "GID_リュール" {
				out = append(out, EmblemCondition{v.instance, v.holder, d.Id, int(v.dirtiness)})
			}
		}
	}
	return out, nil
}
func (s *Save) WithEmblemDirtiness(instance *uint32, dirtiness int) (out *Save, err error) {
	defer recoverError(&err)
	need(dirtiness >= 0 && dirtiness <= 255, "Ring/bracelet dirtiness must be 0–255; 0 is clean.")
	conditions, e := s.EmblemConditions()
	if e != nil {
		return nil, e
	}
	selected := map[uint32]bool{}
	for _, v := range conditions {
		if instance == nil || v.InstanceId == *instance {
			selected[v.InstanceId] = true
		}
	}
	need(instance == nil || len(selected) == 1, "Select an owned, known Emblem ring or bracelet.")
	b := bytes.Clone(s.data)
	for _, v := range s.ownedEmblems() {
		if selected[v.instance] {
			b[v.dirtOffset] = byte(dirtiness)
		}
	}
	return verified(b), nil
}

type ringLocation struct {
	ring        BondRing
	stockOffset int
}
type ringLayout struct {
	section Section
	entries []ringLocation
}

func (s *Save) rings() ringLayout {
	sec, r := s.pool("RING", 3)
	count := int(r.u32())
	need(count <= 750 && count == (r.end-r.pos)/11 && (r.end-r.pos)%11 == 0, "Invalid bond ring pool size.")
	owners := map[uint32]int{}
	for _, v := range s.equipmentLocations() {
		if v.links.RingInstance != 0 {
			owners[v.links.RingInstance] = v.links.CharacterIndex
		}
	}
	used := map[uint32]bool{}
	l := ringLayout{section: sec, entries: []ringLocation{}}
	for range count {
		id := r.u32()
		need(id != 0 && !used[id], "Duplicate or empty bond ring instance.")
		used[id] = true
		hash := r.ref()
		need(hash != nil, "A bond ring has no data reference.")
		offset := r.pos
		stock := int(r.byte())
		var owner *int
		if i, ok := owners[id]; ok {
			owner = &i
		}
		l.entries = append(l.entries, ringLocation{BondRing{id, *hash, stock, owner}, offset})
	}
	for id := range owners {
		need(used[id], "An equipped bond ring is missing from the ring pool.")
	}
	return l
}
func (s *Save) BondRings() (out []BondRing, err error) {
	defer recoverError(&err)
	out = []BondRing{}
	for _, v := range s.rings().entries {
		out = append(out, v.ring)
	}
	return out, nil
}
func ringMaximum(ring BondRing, rings []BondRing) int {
	need(catalog.BondRing(ring.RingHash) != nil, "Unknown bond ring limits cannot be guessed.")
	if ring.OwnerIndex != nil {
		return 1
	}
	other := 0
	for _, v := range rings {
		if v.RingHash == ring.RingHash && v.InstanceId != ring.InstanceId {
			other += v.StockCount
		}
	}
	return max(0, 99-other)
}
func (s *Save) MaximumBondRingStock(instance uint32) (out int, err error) {
	defer recoverError(&err)
	rings, e := s.BondRings()
	if e != nil {
		return 0, e
	}
	for _, v := range rings {
		if v.InstanceId == instance {
			return ringMaximum(v, rings), nil
		}
	}
	panic(dataError("Select an existing bond ring instance."))
}
func (s *Save) WithBondRingStock(instance uint32, stock int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.rings()
	rings := []BondRing{}
	var selected *ringLocation
	for _, v := range l.entries {
		rings = append(rings, v.ring)
		if v.ring.InstanceId == instance {
			copy := v
			selected = &copy
		}
	}
	need(selected != nil, "Select an existing bond ring instance.")
	ring := selected.ring
	need(catalog.BondRing(ring.RingHash) != nil, "Unknown bond ring limits cannot be guessed.")
	minimum, maximum := 0, 99
	if ring.OwnerIndex != nil {
		minimum = 1
		maximum = 1
	}
	need(stock >= minimum && stock <= maximum, "Invalid bond ring stock.")
	if stock == ring.StockCount {
		return s, nil
	}
	need(stock <= ringMaximum(ring, rings), "Identical rings, including equipped copies, share a 99-copy limit.")
	b := bytes.Clone(s.data)
	b[selected.stockOffset] = byte(stock)
	return verified(b), nil
}
func (s *Save) writeRingPool(l ringLayout, rings []BondRing) *Save {
	b := make([]byte, 36+len(rings)*11)
	copy(b, s.data[l.section.PayloadOffset():l.section.PayloadOffset()+32])
	put32(b, 32, uint32(len(rings)))
	for i, v := range rings {
		p := 36 + i*11
		put32(b, p, v.InstanceId)
		put16(b, p+4, 0xefcd)
		put32(b, p+6, v.RingHash)
		b[p+10] = byte(v.StockCount)
	}
	return s.replaceSection(l.section, b)
}
func addBondRing(rings []BondRing, d catalog.BondRingDefinition) []BondRing {
	total := 0
	existing := -1
	equipped := 0
	used := map[uint32]bool{}
	for i, v := range rings {
		used[v.InstanceId] = true
		if v.OwnerIndex != nil {
			equipped++
		}
		if v.RingHash == d.Hash() {
			total += v.StockCount
			if v.OwnerIndex == nil && existing < 0 {
				existing = i
			}
		}
	}
	need(total < 99, "This bond ring already has the maximum total stock of 99.")
	if existing >= 0 {
		rings[existing].StockCount++
		return rings
	}
	need(len(rings) < 750 && len(rings)-equipped < 700 && equipped <= 50, "The bond ring pool has no safe free slot.")
	for _, v := range rings {
		need(v.InstanceId <= 750, "The bond ring pool has no safe free slot.")
	}
	for id := uint32(1); id <= 750; id++ {
		if !used[id] {
			return append(rings, BondRing{id, d.Hash(), 1, nil})
		}
	}
	panic(dataError("The bond ring pool has no safe free slot."))
}
func (s *Save) WithMissingSBondRings() (out *Save, err error) {
	defer recoverError(&err)
	l := s.rings()
	rings := []BondRing{}
	for _, v := range l.entries {
		rings = append(rings, v.ring)
	}
	acquired := []catalog.BondRingDefinition{}
	for _, d := range catalog.BondRings {
		if d.Rank != 3 {
			continue
		}
		has := false
		for _, v := range rings {
			has = has || v.RingHash == d.Hash() && v.StockCount > 0
		}
		if !has {
			rings = addBondRing(rings, d)
			acquired = append(acquired, d)
		}
	}
	if len(acquired) == 0 {
		return s, nil
	}
	edited := s.writeRingPool(l, rings)
	for _, d := range acquired {
		edited = edited.withVariableFlag(d.AcquisitionKey(), 1<<d.Rank, false)
	}
	return edited, nil
}
func (s *Save) WithMeldedBondRing(instance uint32) (out *Save, err error) {
	defer recoverError(&err)
	l := s.rings()
	var selected *BondRing
	rings := []BondRing{}
	for _, v := range l.entries {
		rings = append(rings, v.ring)
		if v.ring.InstanceId == instance {
			copy := v.ring
			selected = &copy
		}
	}
	need(selected != nil, "Select an existing bond ring instance.")
	need(selected.OwnerIndex == nil, "Equipped bond rings cannot be used as melding materials.")
	source := catalog.BondRing(selected.RingHash)
	need(source != nil, "Unknown bond ring.")
	result := catalog.MeldResult(*source)
	need(result != nil, "Only ordinary C, B or A bond rings can be melded.")
	var rule catalog.MeldingRule
	for _, v := range catalog.MeldingRules {
		if v.SourceRank == source.Rank {
			rule = v
		}
	}
	total, materials := 0, 0
	for _, v := range rings {
		if v.RingHash == selected.RingHash {
			total += v.StockCount
			if v.OwnerIndex == nil {
				need(v.StockCount <= 99, "Invalid ring stock.")
				materials += v.StockCount
			}
		}
	}
	need(total <= 99 && materials >= rule.RequiredRings, "Not enough valid unequipped copies of this bond ring to meld.")
	main, e := s.Main()
	if e != nil {
		return nil, e
	}
	need(main.BondFragments >= rule.BondFragments, "Not enough bond fragments to meld this ring.")
	remaining := rule.RequiredRings
	for pass := 0; pass < 2; pass++ {
		for i := 0; i < len(rings) && remaining > 0; i++ {
			v := rings[i]
			if v.RingHash != selected.RingHash || v.OwnerIndex != nil || (pass == 0) != (v.InstanceId == instance) {
				continue
			}
			used := min(remaining, v.StockCount)
			if used == 0 {
				continue
			}
			remaining -= used
			if used == v.StockCount {
				rings = append(rings[:i], rings[i+1:]...)
				i--
			} else {
				rings[i].StockCount -= used
			}
		}
	}
	rings = addBondRing(rings, *result)
	edited := s.writeRingPool(l, rings).withVariableFlag(result.AcquisitionKey(), 1<<result.Rank, false)
	values, e := edited.Main()
	if e != nil {
		return nil, e
	}
	values.BondFragments = main.BondFragments - rule.BondFragments
	return edited.WithMain(values)
}
