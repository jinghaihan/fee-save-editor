package save

import (
	"bytes"
	"testing"
)

func TestEquipmentConservesRingStock(t *testing.T) {
	s := loadFixture(t, "equipment")
	original := s.Bytes()
	current, e := s.RosterEquipment(0)
	if e != nil || current != (EquipmentSelection{EquipmentEmblem, 1}) {
		t.Fatalf("equipment: %+v %v", current, e)
	}
	options, e := s.RosterEquipmentOptions(0)
	if e != nil {
		t.Fatal(e)
	}
	for _, option := range options {
		if option.Selection.Kind != EquipmentBondRing || option.StockCount <= 1 {
			continue
		}
		edited, e := s.WithRosterEquipment(0, option.Selection)
		if e != nil {
			t.Fatal(e)
		}
		ring, e := edited.RosterEquipment(0)
		if e != nil || ring.Kind != EquipmentBondRing {
			t.Fatal("ring not equipped")
		}
		before, _ := s.BondRings()
		after, _ := edited.BondRings()
		for hash, n := range ringTotals(before) {
			if ringTotals(after)[hash] != n {
				t.Fatal("equipment changed total stock")
			}
		}
		moved, e := edited.WithRosterEquipment(1, ring)
		if e != nil {
			t.Fatal(e)
		}
		empty, _ := moved.RosterEquipment(0)
		if empty.Kind != EquipmentNone {
			t.Fatal("previous owner retained ring")
		}
		returned, e := moved.WithRosterEquipment(1, EquipmentSelection{})
		if e != nil {
			t.Fatal(e)
		}
		after, _ = returned.BondRings()
		for hash, n := range ringTotals(before) {
			if ringTotals(after)[hash] != n {
				t.Fatal("return changed total stock")
			}
		}
		break
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
	if _, e = s.WithRosterEquipment(0, EquipmentSelection{EquipmentEmblem, 999}); e == nil {
		t.Fatal("unknown equipment accepted")
	}
}
func TestEngravingTransfer(t *testing.T) {
	s := loadFixture(t, "engravings")
	s, err := s.WithInventoryEngraving(1, nil)
	if err != nil {
		t.Fatal(err)
	}
	original := s.Bytes()
	id := "GID_マルス"
	edited, e := s.WithInventoryEngraving(1, &id)
	if e != nil {
		t.Fatal(e)
	}
	rows, _ := edited.Inventory()
	if rows[1].Item.EngravingHash == nil {
		t.Fatal("engraving not written")
	}
	restored, e := edited.WithInventoryEngraving(1, nil)
	if e != nil || !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("engraving restore differs")
	}
	index := 0
	moved, e := edited.WithEngravedItem(&index, 1, "IID_フェンサリル", 255, 0, &id)
	if e != nil {
		t.Fatal(e)
	}
	rows, _ = moved.Inventory()
	roster, _ := moved.Roster()
	if rows[1].Item.EngravingHash != nil || roster[0].Items[1].Item.EngravingHash == nil {
		t.Fatal("engraving transfer duplicated ownership")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
