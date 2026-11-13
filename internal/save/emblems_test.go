package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"testing"
)

func TestEmblemBondSerialization(t *testing.T) {
	s := loadFixture(t, "emblems")
	original := s.Bytes()
	rows, e := s.Emblems()
	if e != nil || len(rows) != 3 {
		t.Fatalf("emblems: %v", e)
	}
	edited, e := s.WithEmblemBond(1, "PID_ユナカ", 5, nil)
	if e != nil {
		t.Fatal(e)
	}
	after, e := edited.Emblems()
	if e != nil {
		t.Fatal(e)
	}
	bond := after[0].Bonds[1]
	if bond.Level != 5 || bond.Experience != catalog.BondExperience[4] || bond.TalkFlags != 34 {
		t.Fatalf("bond mismatch: %+v", bond)
	}
	if len(bond.InheritedSkills) != 2 || bond.InheritedSkills[0] != 0x12345678 {
		t.Fatal("bond metadata changed")
	}
	restored, e := edited.WithEmblemBond(1, "PID_ユナカ", 1, nil)
	if e != nil || !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("bond restoration differs")
	}
	if _, e = s.WithEmblemBond(3, "PID_ユナカ", 20, nil); e == nil {
		t.Fatal("Pact downgrade accepted")
	}
	wrong := 208
	if _, e = s.WithEmblemBond(1, "PID_ユナカ", 5, &wrong); e == nil {
		t.Fatal("mismatched bond EXP accepted")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestRingStockAndMelding(t *testing.T) {
	s := loadFixture(t, "emblems")
	original := s.Bytes()
	rings, e := s.BondRings()
	if e != nil || len(rings) != 2 {
		t.Fatalf("rings: %v", e)
	}
	edited, e := s.WithBondRingStock(10, 99)
	if e != nil {
		t.Fatal(e)
	}
	restored, e := edited.WithBondRingStock(10, 7)
	if e != nil || !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("stock restoration differs")
	}
	if _, e = s.WithBondRingStock(10, 100); e == nil {
		t.Fatal("invalid stock accepted")
	}
	edited, e = s.WithMeldedBondRing(10)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := edited.BondRings()
	if e != nil {
		t.Fatal(e)
	}
	if len(rows) != 3 || rows[0].StockCount != 5 {
		t.Fatalf("melded rings: %+v", rows)
	}
	a, _ := s.Main()
	b, _ := edited.Main()
	if a.BondFragments-b.BondFragments != 100 {
		t.Fatal("melding cost mismatch")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestSupportAndDirtiness(t *testing.T) {
	s := loadFixture(t, "supports")
	original := s.Bytes()
	rows, e := s.Supports()
	if e != nil || len(rows) != 5 {
		t.Fatalf("supports: %v", e)
	}
	edited, e := s.MaximizeSupports("")
	if e != nil {
		t.Fatal(e)
	}
	after, e := edited.Supports()
	if e != nil {
		t.Fatal(e)
	}
	for i, rank := range []int{3, 3, 3, 4, 1} {
		if after[i].Rank != rank || after[i].Score != rows[i].Score {
			t.Fatal("support state mismatch")
		}
	}
	if _, e = edited.WithSupport(after[3].Key, 3, nil); e == nil {
		t.Fatal("Pact downgrade accepted")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
	equipment := loadFixture(t, "equipment")
	conditions, e := equipment.EmblemConditions()
	if e != nil || len(conditions) != 2 {
		t.Fatalf("conditions: %v", e)
	}
	id := conditions[0].InstanceId
	edited, e = equipment.WithEmblemDirtiness(&id, 255)
	if e != nil {
		t.Fatal(e)
	}
	restored, e := edited.WithEmblemDirtiness(&id, conditions[0].Dirtiness)
	if e != nil || !bytes.Equal(restored.Bytes(), equipment.Bytes()) {
		t.Fatal("dirtiness restoration differs")
	}
	if _, e = equipment.WithEmblemDirtiness(&id, 256); e == nil {
		t.Fatal("invalid dirtiness accepted")
	}
}
