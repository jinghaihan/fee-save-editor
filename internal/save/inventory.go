package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type InventoryItem struct {
	ItemHash          uint32
	Uses, RefineLevel int
	Flags             uint32
	EngravingHash     *uint32
}
type InventorySlot struct {
	Slot int
	Item *InventoryItem
}
type inventoryLayout struct {
	section      Section
	slots        []InventorySlot
	starts, ends []int
}

func (s *Save) inventory() inventoryLayout {
	s.RequireGame()
	sec := s.Section("TRAN")
	r := reader{s.data, sec.PayloadOffset(), sec.PayloadOffset() + sec.Length}
	need(r.u32() == 1, "Unsupported TRAN section version.")
	r.skip(28)
	count := int(r.u16())
	need(count >= 1 && count <= 999 && count <= (r.end-r.pos)/9, "Invalid convoy capacity.")
	l := inventoryLayout{section: sec, slots: make([]InventorySlot, count), starts: make([]int, count), ends: make([]int, count)}
	for i := range count {
		l.starts[i] = r.pos
		need(r.u32() == 1, "Unsupported convoy entry version.")
		l.slots[i] = InventorySlot{i, readItem(&r)}
		l.ends[i] = r.pos
	}
	need(r.pos == r.end, "The convoy contains unrecognized trailing data.")
	return l
}
func readItem(r *reader) *InventoryItem {
	need(r.u32() == 5, "Unsupported UnitItem version.")
	present := r.byte()
	need(present <= 1, "Invalid item presence flag.")
	if present == 0 {
		return nil
	}
	hash := r.ref()
	need(hash != nil, "An occupied item has no item reference.")
	return &InventoryItem{*hash, int(r.byte()), int(r.byte()), r.u32(), r.ref()}
}
func reference(hash *uint32) []byte {
	if hash == nil {
		return []byte{0xdb, 0xcc}
	}
	b := make([]byte, 6)
	put16(b, 0, 0xefcd)
	put32(b, 2, *hash)
	return b
}
func itemBytes(item *InventoryItem) []byte {
	b := make([]byte, 5)
	put32(b, 0, 5)
	if item == nil {
		return b
	}
	b[4] = 1
	b = append(b, reference(&item.ItemHash)...)
	b = append(b, byte(item.Uses), byte(item.RefineLevel))
	b = append(b, make([]byte, 4)...)
	put32(b, len(b)-4, item.Flags)
	return append(b, reference(item.EngravingHash)...)
}
func validateItem(item *InventoryItem) {
	if item == nil {
		return
	}
	d := catalog.Item(item.ItemHash)
	need(d != nil, "This item is not in the verified convoy catalog.")
	need((d.MaxUses == 255 && item.Uses == 255) || (d.MaxUses != 255 && item.Uses >= 1 && item.Uses <= d.MaxUses), "Invalid remaining uses.")
	need(item.RefineLevel >= 0 && item.RefineLevel <= d.MaxRefine, "Invalid refine level.")
	need(item.EngravingHash == nil || catalog.CanEngrave(item.ItemHash), "This item cannot carry a weapon engraving.")
}
func splice(b []byte, start, end int, replacement []byte) []byte {
	out := make([]byte, 0, len(b)+len(replacement)-(end-start))
	out = append(out, b[:start]...)
	out = append(out, replacement...)
	return append(out, b[end:]...)
}
func (s *Save) replaceInventory(l inventoryLayout, slot int, item *InventoryItem) *Save {
	need(slot >= 0 && slot < len(l.slots), "The convoy slot is outside the saved capacity.")
	b := make([]byte, 4)
	put32(b, 0, 1)
	b = append(b, itemBytes(item)...)
	p := l.section.PayloadOffset()
	return s.replaceSection(l.section, splice(s.data[p:p+l.section.Length], l.starts[slot]-p, l.ends[slot]-p, b))
}
func (s *Save) Inventory() (out []InventorySlot, err error) {
	defer recoverError(&err)
	return s.inventory().slots, nil
}
func (s *Save) WithInventoryItem(slot int, id string, uses, refine int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.inventory()
	need(slot >= 0 && slot < len(l.slots), "The convoy slot is outside the saved capacity.")
	d, e := catalog.ItemByID(id)
	need(e == nil, "Choose a verified convoy item.")
	item := &InventoryItem{ItemHash: d.Hash(), Uses: uses, RefineLevel: refine}
	if old := l.slots[slot].Item; old != nil {
		item.Flags = old.Flags
		item.EngravingHash = old.EngravingHash
	}
	validateItem(item)
	return s.replaceInventory(l, slot, item), nil
}
func (s *Save) AddInventoryItem(id string, uses *int, refine int) (out *Save, err error) {
	defer recoverError(&err)
	d, e := catalog.ItemByID(id)
	need(e == nil, "Choose a verified convoy item.")
	n := d.MaxUses
	if uses != nil {
		n = *uses
	}
	for _, slot := range s.inventory().slots {
		if slot.Item == nil {
			return s.WithInventoryItem(slot.Slot, id, n, refine)
		}
	}
	panic(dataError("The convoy is full."))
}
func (s *Save) DeleteInventoryItem(slot int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.inventory()
	return s.replaceInventory(l, slot, nil), nil
}
func (s *Save) RestoreInventoryUses(slot *int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.inventory()
	if slot != nil {
		need(*slot >= 0 && *slot < len(l.slots), "Invalid convoy slot.")
		i := l.slots[*slot].Item
		need(i != nil && catalog.Item(i.ItemHash) != nil, "Select a known item to restore its uses.")
	}
	p := l.section.PayloadOffset()
	b := bytes.Clone(s.data[p : p+l.section.Length])
	for _, row := range l.slots {
		if slot != nil && row.Slot != *slot {
			continue
		}
		i := row.Item
		if i == nil {
			continue
		}
		d := catalog.Item(i.ItemHash)
		if d != nil && d.MaxUses != 255 {
			b[l.starts[row.Slot]+15-p] = byte(d.MaxUses)
		}
	}
	return s.replaceSection(l.section, b), nil
}
