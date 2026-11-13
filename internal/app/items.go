package app

import (
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"sort"
	"strconv"
	"strings"
)

func (a *App) itemName(item *save.InventoryItem) string {
	if item == nil {
		return a.T("EmptySlot")
	}
	if d := catalog.Item(item.ItemHash); d != nil {
		return d.Name(a.Language)
	}
	if d := catalog.RosterItem(item.ItemHash); d != nil {
		return d.Name(a.Language)
	}
	return fmt.Sprintf("%s (0x%08X)", a.T("UnknownItem"), item.ItemHash)
}
func (a *App) itemLabel(item *save.InventoryItem) string {
	name := a.itemName(item)
	if item == nil {
		return name
	}
	if item.RefineLevel != 0 {
		name += fmt.Sprintf(" +%d", item.RefineLevel)
	}
	if d := catalog.Item(item.ItemHash); d != nil && d.MaxUses != 255 {
		name += fmt.Sprintf(" · %d/%d", item.Uses, d.MaxUses)
	}
	return name
}
func (a *App) itemsPage(c *ui.Context) {
	tab := a.tabsView(c, "items", "Convoy", "ReclassItems", "Materials", "Ingredients", "Gifts", "KeyItems")
	if tab > 0 {
		a.quantityPage(c, []string{"", "ReclassItems", "Materials", "Ingredients", "Gifts", "KeyItems"}[tab])
		return
	}
	slots, ok := data(c, a, "inventory", a.Save.Inventory)
	if !ok {
		return
	}
	rows := []row{}
	for _, slot := range slots {
		if slot.Item != nil {
			rows = append(rows, row{strconv.Itoa(slot.Slot), a.itemLabel(slot.Item), ""})
		}
	}
	a.split(c, "convoy", "Convoy", rows, func() { ui.Text(c, fmt.Sprintf("%d / %d", len(rows), len(slots))) }, func() {
		a.button(c, "RestoreAllUses", func() {
			if a.ApplyPending("convoy") == nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.RestoreInventoryUses(nil) })
			}
		})
	}, func(id string) {
		slot := -1
		var item *save.InventoryItem
		if id != "" {
			slot, _ = strconv.Atoi(id)
			item = slots[slot].Item
		}
		a.itemEditor(c, "convoy", nil, slot, item)
	})
}
func (a *App) quantityPage(c *ui.Context, category string) {
	items, ok := data(c, a, "quantities", a.Save.Quantities)
	if !ok {
		return
	}
	rows := []row{}
	for _, r := range items {
		if r.Definition.Category == category {
			rows = append(rows, row{r.Definition.Id, r.Definition.Names.Name(a.Language), fmt.Sprint(r.Amount)})
		}
	}
	a.split(c, "quantity", category, rows, nil, func() {
		if category != "KeyItems" {
			a.button(c, "FillAll", func() {
				if a.ApplyPending("quantity") == nil {
					a.apply(func(s *save.Save) (*save.Save, error) { return s.FillQuantities(category) })
				}
			})
		}
	}, func(id string) {
		for _, r := range items {
			if r.Definition.Id != id {
				continue
			}
			ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
				ui.Text(c, a.T("ItemEditor")).Bold()
				ui.Text(c, r.Definition.Names.Name(a.Language))
				a.numberEdit(c, "Amount", "quantity/"+id, r.Amount, 0, r.Definition.Maximum, func(s *save.Save, n int) (*save.Save, error) { return s.WithQuantity(id, n) })
				a.applyButton(c, "quantity")
			})
		}
	})
}

type itemDraft struct {
	ID, EngravingID, OriginalEngravingID string
	Uses, Refine                         float64
}

func newItemDraft(item *save.InventoryItem) itemDraft {
	d := itemDraft{Uses: 1}
	if item == nil {
		if len(catalog.Items) > 0 {
			d.ID = catalog.Items[0].Id
			d.Uses = float64(catalog.Items[0].MaxUses)
		}
		return d
	}
	if def := catalog.Item(item.ItemHash); def != nil {
		d.ID = def.Id
	}
	d.Uses = float64(item.Uses)
	d.Refine = float64(item.RefineLevel)
	if item.EngravingHash != nil {
		d.EngravingID = fmt.Sprintf("0x%08X", *item.EngravingHash)
		if def := catalog.Engraving(*item.EngravingHash); def != nil {
			d.EngravingID = def.Id
		}
	}
	d.OriginalEngravingID = d.EngravingID
	return d
}
func (a *App) itemEditor(c *ui.Context, scope string, index *int, slot int, item *save.InventoryItem) {
	key := fmt.Sprintf("%s/%d", scope, slot)
	d := draft(a, key, func() itemDraft { return newItemDraft(item) })
	stage := func() {
		a.stage(key, func(s *save.Save) (*save.Save, error) {
			if d.ID == "" {
				if d.EngravingID == d.OriginalEngravingID {
					return s, nil
				}
				var id *string
				if d.EngravingID != "" {
					v := d.EngravingID
					id = &v
				}
				if index == nil {
					return s.WithInventoryEngraving(slot, id)
				}
				return s.WithRosterEngraving(*index, slot, id)
			}
			if d.EngravingID != d.OriginalEngravingID {
				var id *string
				if d.EngravingID != "" {
					v := d.EngravingID
					id = &v
				}
				return s.WithEngravedItem(index, slot, d.ID, int(d.Uses), int(d.Refine), id)
			}
			if index == nil {
				return s.WithInventoryItem(slot, d.ID, int(d.Uses), int(d.Refine))
			}
			return s.WithRosterItem(*index, slot, d.ID, int(d.Uses), int(d.Refine))
		})
	}
	editable := true
	if item != nil {
		if def := catalog.RosterItem(item.ItemHash); def != nil && def.EngageOnly {
			editable = false
		}
	}
	ui.Column(c.Key(key)).MaxWidth(340).Gap(editorAppearance.ControlGap).Disabled(!editable).Children(func() {
		ui.Text(c, a.T("ItemEditor")).Bold()
		search := a.query(c, key+"/search")
		options := []option{}
		for _, def := range catalog.Items {
			name := def.Name(a.Language)
			if strings.Contains(strings.ToLower(name), strings.ToLower(*search)) || def.Id == d.ID {
				options = append(options, option{def.Id, name})
			}
		}
		sort.SliceStable(options, func(i, j int) bool { return options[i].Name < options[j].Name })
		if a.combo(c, "Item", key+"/item", &d.ID, options) {
			if def := catalog.Item(catalog.Hash(d.ID)); def != nil {
				d.Uses = float64(def.MaxUses)
				d.Refine = 0
			}
			if slot >= 0 {
				stage()
			}
		}
		def := catalog.Item(catalog.Hash(d.ID))
		maximum, refine := 255, 5
		if def != nil {
			maximum, refine = def.MaxUses, def.MaxRefine
		}
		a.field(c, "RemainingUses", func() {
			if ui.NumberInput(c.Key("uses"), &d.Uses, 1, float64(maximum), 1).Label(a.T("RemainingUses")).Changed() && slot >= 0 {
				stage()
			}
			if def != nil && def.MaxUses == 255 {
				ui.Text(c, a.T("Unlimited"))
			}
			a.button(c, "RestoreUses", func() {
				if def != nil {
					d.Uses = float64(def.MaxUses)
					if slot >= 0 {
						stage()
					}
				}
			})
		})
		if a.formNumber(c, "RefineLevel", key+"/refine", &d.Refine, 0, float64(refine)) && slot >= 0 {
			stage()
		}
		engravings := []option{{"", a.T("None")}}
		eligible := def != nil && catalog.CanEngrave(def.Hash())
		for _, engraving := range catalog.Engravings {
			if eligible || engraving.Id == d.EngravingID {
				engravings = append(engravings, option{engraving.Id, catalog.Emblem(engraving.Id).Name(a.Language)})
			}
		}
		if strings.HasPrefix(d.EngravingID, "0x") {
			engravings = append(engravings, option{d.EngravingID, d.EngravingID})
		}
		if a.combo(c, "Engraving", key+"/engraving", &d.EngravingID, engravings) && slot >= 0 {
			stage()
		}
		if e := catalog.Engraving(catalog.Hash(d.EngravingID)); e != nil {
			a.engravingEffects(c, e)
		}
		if ui.Button(c, a.T("ApplyChanges")).Disabled(slot < 0).Clicked() {
			stage()
			a.ApplyPending(scope)
		}
		if index == nil {
			a.columns(c, func() {
				a.button(c, "AddItem", func() {
					if d.ID == "" {
						return
					}
					a.discard(key)
					a.apply(func(s *save.Save) (*save.Save, error) {
						uses := int(d.Uses)
						slots, e := s.Inventory()
						if e != nil {
							return nil, e
						}
						empty := -1
						for _, v := range slots {
							if v.Item == nil {
								empty = v.Slot
								break
							}
						}
						edited, e := s.AddInventoryItem(d.ID, &uses, int(d.Refine))
						if e != nil {
							return nil, e
						}
						if d.EngravingID != "" {
							id := d.EngravingID
							return edited.WithInventoryEngraving(empty, &id)
						}
						return edited, nil
					})
				})
			}, func() {
				if ui.Button(c, a.T("DeleteItem")).Disabled(item == nil).Clicked() {
					if a.ApplyPending(scope) == nil {
						a.apply(func(s *save.Save) (*save.Save, error) { return s.DeleteInventoryItem(slot) })
					}
				}
			})
		} else if ui.Button(c, a.T("DeleteItem")).Disabled(item == nil).Clicked() {
			if a.ApplyPending(scope) == nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.DeleteRosterItem(*index, slot) })
			}
		}
	})
}
func (a *App) engravingEffects(c *ui.Context, e *catalog.EngravingDefinition) {
	values := []struct {
		label string
		value int
	}{{"Might", e.Power}, {"Weight", e.Weight}, {"Hit", e.Hit}, {"Critical", e.Critical}, {"Avoid", e.Avoid}, {"Dodge", e.Secure}}
	for start := 0; start < len(values); start += 3 {
		ui.Row(c).Gap(editorAppearance.SectionGap).Children(func() {
			for _, v := range values[start : start+3] {
				ui.Text(c, fmt.Sprintf("%s %s", a.T(v.label), signed(v.value))).Grow(1).Basis(0)
			}
		})
	}
}
func signed(n int) string {
	if n > 0 {
		return "+" + strconv.Itoa(n)
	}
	return strconv.Itoa(n)
}
