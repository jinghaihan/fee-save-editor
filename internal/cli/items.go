package cli

import (
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"io"
	"strings"
)

func engravingID(text string) *string {
	if strings.EqualFold(text, "none") {
		return nil
	}
	return &text
}
func itemObject(row save.InventorySlot, lang string) object {
	item := row.Item
	var id *string
	name := fmt.Sprintf("Unknown item (0x%08X)", item.ItemHash)
	if d := catalog.Item(item.ItemHash); d != nil {
		v := d.Id
		id = &v
		name = d.Name(lang)
	}
	var engravedID, engravedName *string
	if item.EngravingHash != nil {
		text := fmt.Sprintf("0x%08X", *item.EngravingHash)
		engravedName = &text
		if d := catalog.Engraving(*item.EngravingHash); d != nil {
			id := d.Id
			engravedID = &id
			name := catalog.Emblem(d.Id).Name(lang)
			engravedName = &name
		}
	}
	return object{"Slot": row.Slot, "Id": id, "Name": name, "ItemHash": item.ItemHash, "Uses": item.Uses, "RefineLevel": item.RefineLevel, "Flags": item.Flags, "EngravingHash": item.EngravingHash, "EngravingId": engravedID, "Engraving": engravedName}
}
func runItems(args []string, out io.Writer) {
	check(len(args) > 0, "Provide an items command.")
	verb := args[0]
	switch verb {
	case "catalog":
		parse(args[1:], "--json")
		rows := []object{}
		for _, d := range catalog.Items {
			rows = append(rows, object{"Id": d.Id, "English": d.English, "MaxUses": d.MaxUses, "MaxRefine": d.MaxRefine, "UnlimitedUses": d.MaxUses == 255})
		}
		outputJSON(out, rows)
		return
	case "engravings":
		parse(args[1:], "--json")
		rows := []object{}
		for _, d := range catalog.Engravings {
			emblem := catalog.Emblem(d.Id)
			rows = append(rows, object{"Id": d.Id, "English": emblem.Name("en"), "Chinese": emblem.Name("zh-Hans"), "Power": d.Power, "Weight": d.Weight, "Hit": d.Hit, "Critical": d.Critical, "Avoid": d.Avoid, "Secure": d.Secure})
		}
		outputJSON(out, rows)
		return
	case "quantity-catalog":
		lang := language(args[1:])
		rows := []object{}
		for _, d := range catalog.QuantityItems {
			rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Category": d.Category, "Maximum": d.Maximum})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	if verb == "list" {
		parse(args[2:], "--json")
		slots := take(s.Inventory())
		rows := []object{}
		for _, row := range slots {
			if row.Item != nil {
				rows = append(rows, itemObject(row, "en"))
			}
		}
		outputJSON(out, object{"Capacity": len(slots), "Occupied": len(rows), "Items": rows})
		return
	}
	if verb == "quantities" {
		lang := language(args[2:])
		rows := []object{}
		for _, r := range take(s.Quantities()) {
			d := r.Definition
			rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Category": d.Category, "Amount": r.Amount, "Maximum": d.Maximum})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	allowed := []string{}
	switch verb {
	case "quantity-set":
		allowed = []string{"--item", "--amount"}
	case "quantity-fill":
		allowed = []string{"--category"}
	case "set":
		allowed = []string{"--slot", "--item", "--uses", "--refine", "--engraving"}
	case "add":
		allowed = []string{"--item", "--uses", "--refine", "--engraving"}
	case "engrave":
		allowed = []string{"--slot", "--engraving"}
	case "delete":
		allowed = []string{"--slot"}
	case "restore":
		allowed = []string{"--slot", "--all"}
	default:
		fail("Unknown items command.")
	}
	o := parse(args[3:], allowed...)
	check(len(o) > 0, "Provide item options and their values.")
	switch verb {
	case "quantity-set":
		s = take(s.WithQuantity(o.required("--item"), o.number("--amount")))
	case "quantity-fill":
		category := o.required("--category")
		known := ""
		for _, name := range []string{"ReclassItems", "Materials", "Ingredients", "Gifts"} {
			if strings.EqualFold(category, name) {
				known = name
			}
		}
		check(known != "", "Unknown quantity item category.")
		s = take(s.FillQuantities(known))
	case "add":
		var uses *int
		if o.has("--uses") {
			n := o.number("--uses")
			uses = &n
		}
		slot := -1
		for _, row := range take(s.Inventory()) {
			if row.Item == nil {
				slot = row.Slot
				break
			}
		}
		s = take(s.AddInventoryItem(o.required("--item"), uses, o.integerOr("--refine", 0)))
		if o.has("--engraving") {
			s = take(s.WithInventoryEngraving(slot, engravingID(o["--engraving"])))
		}
	case "restore":
		check(len(o) == 1, "Use --all or --slot.")
		var slot *int
		if o.has("--slot") {
			n := o.number("--slot")
			slot = &n
		}
		s = take(s.RestoreInventoryUses(slot))
	default:
		slot := o.number("--slot")
		check(slot >= 0, "Slot must be nonnegative.")
		switch verb {
		case "delete":
			s = take(s.DeleteInventoryItem(slot))
		case "engrave":
			s = take(s.WithInventoryEngraving(slot, engravingID(o.required("--engraving"))))
		case "set":
			rows := take(s.Inventory())
			check(slot < len(rows), "Invalid convoy slot.")
			old := rows[slot].Item
			id := o["--item"]
			if id == "" && old != nil {
				if d := catalog.Item(old.ItemHash); d != nil {
					id = d.Id
				}
			}
			check(id != "", "Choose an item with --item for an empty or unknown slot.")
			d := take(catalog.ItemByID(id))
			uses, refine := d.MaxUses, 0
			if old != nil && old.ItemHash == d.Hash() {
				uses, refine = old.Uses, old.RefineLevel
			}
			uses = o.integerOr("--uses", uses)
			refine = o.integerOr("--refine", refine)
			if o.has("--engraving") {
				s = take(s.WithEngravedItem(nil, slot, id, uses, refine, engravingID(o["--engraving"])))
			} else {
				s = take(s.WithInventoryItem(slot, id, uses, refine))
			}
		}
	}
	write(out, s, args[2])
}
