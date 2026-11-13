package save

import (
	"bytes"
	"testing"
)

func TestRecoveryAndRepair(t *testing.T) {
	s := loadFixture(t, "recovery")
	original := s.Bytes()
	if !s.CanRestoreRosterCharacter(1) {
		t.Fatal("recoverable character rejected")
	}
	edited, e := s.RestoreRosterCharacter(1)
	if e != nil {
		t.Fatal(e)
	}
	rows, _ := edited.Roster()
	found := false
	for _, c := range rows {
		if c.PersonHash == s.roster().get(1).character.PersonHash {
			found = true
			if c.Force != Absent || c.Availability() != "Available" || c.Progress.CurrentHP == 0 {
				t.Fatal("recovery failed")
			}
		}
	}
	if !found {
		t.Fatal("character disappeared")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
	active := loadFixture(t, "equipment")
	if _, e = active.RestoreRosterCharacter(1); e == nil {
		t.Fatal("alive character restored")
	}
	broken := s.Bytes()
	c := s.roster().get(1)
	put32(broken, c.classStart+2, 0xdeadbeef)
	invalid := verified(broken)
	if !invalid.CanRepairRosterClass(1) {
		t.Fatal("repairable class rejected")
	}
	fixed, e := invalid.RepairRosterClass(1)
	if e != nil {
		t.Fatal(e)
	}
	if fixed.roster().get(1).character.ClassHash != c.character.ClassHash {
		t.Fatal("class not repaired")
	}
}
