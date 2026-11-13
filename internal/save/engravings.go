package save

import "github.com/jinghaihan/fee-save-editor/internal/catalog"

type engravingOwner struct {
	character *int
	slot      int
	item      *InventoryItem
}

func selectEngraving(item InventoryItem, id *string) *InventoryItem {
	if id == nil {
		item.EngravingHash = nil
		return &item
	}
	def := catalog.Engraving(catalog.Hash(*id))
	valid := false
	if def != nil {
		valid = def.Id == *id
		for _, alias := range def.Aliases {
			valid = valid || alias == *id
		}
	}
	need(valid, "Choose a verified Emblem engraving.")
	need(catalog.CanEngrave(item.ItemHash), "Only verified engravable weapons can receive an engraving.")
	hash := def.Hash()
	item.EngravingHash = &hash
	return &item
}
func (s *Save) replaceEngravingOwner(owner engravingOwner) *Save {
	if owner.character != nil {
		l := s.roster()
		c := l.get(*owner.character)
		characterItem(c, owner.slot)
		return s.replaceCharacter(l, c, c.itemStarts[owner.slot], c.itemEnds[owner.slot], itemBytes(owner.item))
	}
	return s.replaceInventory(s.inventory(), owner.slot, owner.item)
}
func (s *Save) assignEngraving(target engravingOwner) *Save {
	edited := s
	if target.item.EngravingHash != nil {
		def := catalog.Engraving(*target.item.EngravingHash)
		need(def != nil, "Choose a verified Emblem engraving.")
		inventory := s.inventory()
		roster := s.roster()
		owners := []engravingOwner{}
		for _, v := range inventory.slots {
			if v.Item != nil {
				owners = append(owners, engravingOwner{nil, v.Slot, v.Item})
			}
		}
		for _, c := range roster.characters {
			if c.character.Force == Enemy || c.character.Force == Temporary {
				continue
			}
			for _, v := range c.character.Items {
				if v.Item != nil {
					index := c.character.Index
					owners = append(owners, engravingOwner{&index, v.Slot, v.Item})
				}
			}
		}
		for _, owner := range owners {
			same := owner.character == nil && target.character == nil || owner.character != nil && target.character != nil && *owner.character == *target.character
			if same && owner.slot == target.slot || owner.item.EngravingHash == nil {
				continue
			}
			previous := catalog.Engraving(*owner.item.EngravingHash)
			if previous == nil || previous.Id != def.Id {
				continue
			}
			if owner.character != nil {
				checkItemEditable(owner.item)
			}
			copy := *owner.item
			copy.EngravingHash = nil
			owner.item = &copy
			edited = edited.replaceEngravingOwner(owner)
		}
	}
	return edited.replaceEngravingOwner(target)
}
func (s *Save) WithInventoryEngraving(slot int, id *string) (out *Save, err error) {
	defer recoverError(&err)
	l := s.inventory()
	need(slot >= 0 && slot < len(l.slots), "Invalid convoy slot.")
	old := l.slots[slot].Item
	need(old != nil, "Select an occupied convoy slot.")
	return s.assignEngraving(engravingOwner{nil, slot, selectEngraving(*old, id)}), nil
}
func (s *Save) WithRosterEngraving(index, slot int, id *string) (out *Save, err error) {
	defer recoverError(&err)
	c := s.roster().get(index)
	need(c.character.Force != Enemy && c.character.Force != Temporary, "Enemy and temporary units cannot receive player weapon engravings.")
	old := characterItem(c, slot)
	need(old != nil, "Select an occupied character item slot.")
	checkItemEditable(old)
	return s.assignEngraving(engravingOwner{&index, slot, selectEngraving(*old, id)}), nil
}
func (s *Save) WithEngravedItem(index *int, slot int, id string, uses, refine int, engraving *string) (out *Save, err error) {
	defer recoverError(&err)
	d, e := catalog.ItemByID(id)
	need(e == nil, "Choose a verified convoy item.")
	item := InventoryItem{ItemHash: d.Hash(), Uses: uses, RefineLevel: refine}
	var old *InventoryItem
	if index == nil {
		l := s.inventory()
		need(slot >= 0 && slot < len(l.slots), "Invalid convoy slot.")
		old = l.slots[slot].Item
	} else {
		c := s.roster().get(*index)
		need(c.character.Force != Enemy && c.character.Force != Temporary, "Enemy and temporary units cannot receive player weapon engravings.")
		old = characterItem(c, slot)
		checkItemEditable(old)
	}
	if old != nil {
		item.Flags = old.Flags
	}
	selected := selectEngraving(item, engraving)
	validateItem(selected)
	return s.assignEngraving(engravingOwner{index, slot, selected}), nil
}
