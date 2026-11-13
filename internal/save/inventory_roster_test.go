package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"os"
	"testing"
)

func loadFixture(t *testing.T, name string) *Save {
	t.Helper()
	b, e := os.ReadFile("testdata/" + name + ".bin")
	if e != nil {
		t.Fatal(e)
	}
	s, e := Parse(b)
	if e != nil {
		t.Fatal(e)
	}
	return s
}
func TestInventoryEditsAndRestoration(t *testing.T) {
	s := loadFixture(t, "inventory")
	original := s.Bytes()
	slots, e := s.Inventory()
	if e != nil {
		t.Fatal(e)
	}
	occupied := -1
	for _, slot := range slots {
		if slot.Item != nil && catalog.Item(slot.Item.ItemHash) != nil {
			occupied = slot.Slot
			break
		}
	}
	if occupied < 0 {
		t.Fatal("fixture has no known item")
	}
	old := slots[occupied].Item
	d := catalog.Item(old.ItemHash)
	edited, e := s.WithInventoryItem(occupied, d.Id, d.MaxUses, d.MaxRefine)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := edited.Inventory()
	if e != nil {
		t.Fatal(e)
	}
	if rows[occupied].Item.Flags != old.Flags {
		t.Fatal("item flags changed")
	}
	restored, e := edited.WithInventoryItem(occupied, d.Id, old.Uses, old.RefineLevel)
	if e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("restore changed unrelated bytes")
	}
	removed, e := s.DeleteInventoryItem(occupied)
	if e != nil {
		t.Fatal(e)
	}
	restored, e = removed.WithInventoryItem(occupied, d.Id, old.Uses, old.RefineLevel)
	if e != nil {
		t.Fatal(e)
	}
	if restored.Length() != s.Length() {
		t.Fatal("variable record length differs")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
	if _, e = s.WithInventoryItem(occupied, d.Id, 0, 0); e == nil {
		t.Fatal("invalid uses accepted")
	}
	if _, e = s.DeleteInventoryItem(len(slots)); e == nil {
		t.Fatal("invalid slot accepted")
	}
}
func TestRosterEditsPreserveOtherFields(t *testing.T) {
	s := loadFixture(t, "roster")
	original := s.Bytes()
	roster, e := s.Roster()
	if e != nil {
		t.Fatal(e)
	}
	if len(roster) != 2 || roster[0].Values != (RosterValue{5, 12, 400}) {
		t.Fatalf("unexpected roster: %+v %v", roster, e)
	}
	edited, e := s.WithRosterValues(0, RosterValue{10, 50, 9999})
	if e != nil {
		t.Fatal(e)
	}
	r, e := edited.Roster()
	if e != nil {
		t.Fatal(e)
	}
	if r[0].Values != (RosterValue{10, 50, 9999}) || r[1].Values != roster[1].Values {
		t.Fatal("values did not round trip")
	}
	restored, e := edited.WithRosterValues(0, roster[0].Values)
	if e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("roster value restoration differs")
	}
	c := s.roster().get(0)
	stat := Strength
	v := c.character.Stats[stat]
	edited, e = s.WithRosterStat(0, stat, v.Value+1, false)
	if e != nil {
		t.Fatal(e)
	}
	expected := bytes.Clone(original)
	expected[c.baseOffset+int(stat)]++
	if !bytes.Equal(edited.Bytes(), verified(expected).Bytes()) {
		t.Fatal("stat edit changed other fields")
	}
	edited, e = s.WithRosterItem(0, 0, "IID_ライブ", 8, 0)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := edited.Roster()
	if e != nil {
		t.Fatal(e)
	}
	if rows[0].Items[0].Item.ItemHash != catalog.Hash("IID_ライブ") || rows[0].Items[0].Item.Flags != roster[0].Items[0].Item.Flags {
		t.Fatal("character item lost metadata")
	}
	restored, e = edited.WithRosterItem(0, 0, "IID_リカバー", 4, 0)
	if e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("item restoration changed unrelated bytes")
	}
	for _, v := range []RosterValue{{0, 0, 0}, {99, 0, 0}, {10, 100, 0}, {10, 0, 10000}} {
		if _, e = s.WithRosterValues(0, v); e == nil {
			t.Fatalf("invalid values accepted: %+v", v)
		}
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestRosterSkillAndClassProgression(t *testing.T) {
	s := loadFixture(t, "roster")
	original := s.Bytes()
	var skill catalog.SkillDefinition
	for _, d := range catalog.Skills {
		if d.Inheritable {
			skill = d
			break
		}
	}
	edited, e := s.WithRosterSkill(0, skill.Id, true)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := edited.Roster()
	if e != nil {
		t.Fatal(e)
	}
	if len(rows[0].Progress.InheritedSkills) != 1 || rows[0].Progress.InheritedSkills[0].Hash != skill.Hash() {
		t.Fatal("skill not unlocked")
	}
	hash := skill.Hash()
	equipped, e := edited.WithRosterEquippedSkills(0, &hash, nil)
	if e != nil {
		t.Fatal(e)
	}
	if _, e = equipped.WithRosterEquippedSkills(0, &hash, &hash); e == nil {
		t.Fatal("duplicate equipped skill accepted")
	}
	removed, e := equipped.WithRosterSkill(0, skill.Id, false)
	if e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(removed.Bytes(), original) {
		t.Fatal("skill removal did not restore original")
	}
	classes, e := s.RosterClasses(0)
	if e != nil {
		t.Fatal(e)
	}
	old := s.roster().get(0)
	for _, job := range classes {
		if job.Hash() == old.character.ClassHash {
			continue
		}
		edited, e = s.WithRosterClass(0, job.Id, job.WeaponVariants()[0])
		if e != nil {
			t.Fatal(e)
		}
		after, e := edited.Roster()
		if e != nil {
			t.Fatal(e)
		}
		if after[0].ClassHash != job.Hash() || after[0].Values.Experience != 0 || after[0].Values.SkillPoints != old.character.Values.SkillPoints {
			t.Fatal("class change mismatch")
		}
		break
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
