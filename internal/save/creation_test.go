package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"testing"
)

func TestCharacterCreationMovementDeletion(t *testing.T) {
	s := loadFixture(t, "creation")
	original := s.Bytes()
	missing, e := s.MissingRosterCharacters()
	if e != nil || len(missing) != 39 {
		t.Fatalf("missing roster: %d %v", len(missing), e)
	}
	added, e := s.WithAddedRosterCharacter("PID_フラン")
	if e != nil {
		t.Fatal(e)
	}
	rows, e := added.Roster()
	if e != nil || len(rows) != 3 || rows[2].PersonHash != catalog.Hash("PID_フラン") {
		t.Fatalf("created character: %+v %v", rows, e)
	}
	for _, holder := range added.emblems().emblems {
		if holder.EmblemId == "GID_リュール" {
			continue
		}
		found := false
		for _, bond := range holder.Bonds {
			found = found || bond.PersonId == "PID_フラン"
		}
		if !found {
			t.Fatal("new character bond missing")
		}
	}
	moved, e := added.WithRosterForce(2, Lost)
	if e != nil {
		t.Fatal(e)
	}
	deleted, e := moved.WithoutRosterCharacter(2)
	if e != nil {
		t.Fatal(e)
	}
	unitA := s.Section("UNIT")
	unitB := deleted.Section("UNIT")
	if !bytes.Equal(s.data[unitA.PayloadOffset():unitA.PayloadOffset()+unitA.Length], deleted.data[unitB.PayloadOffset():unitB.PayloadOffset()+unitB.Length]) {
		t.Fatal("delete failed to restore original roster pool")
	}
	if _, e = s.WithAddedRosterCharacter("PID_リュール"); e == nil {
		t.Fatal("protagonist duplication accepted")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestEmblemRemovalRetainsBonds(t *testing.T) {
	s := loadFixture(t, "creation")
	original := s.Bytes()
	removed, e := s.WithoutEmblem(1)
	if e != nil {
		t.Fatal(e)
	}
	if len(removed.ownedEmblems()) != 2 || len(removed.emblems().emblems) != 3 {
		t.Fatal("removal destroyed bond holder")
	}
	restored, e := removed.WithAddedEmblem("GID_マルス")
	if e != nil {
		t.Fatal(e)
	}
	if len(restored.ownedEmblems()) != 3 || len(restored.emblems().emblems) != 3 {
		t.Fatal("existing holder not reused")
	}
	missing, e := s.MissingEmblems()
	if e != nil || len(missing) != 17 {
		t.Fatalf("missing emblems: %d %v", len(missing), e)
	}
	added, e := s.WithAddedEmblem(missing[0].Id)
	if e != nil {
		t.Fatal(e)
	}
	if len(added.ownedEmblems()) != 4 || len(added.emblems().emblems) != 4 {
		t.Fatal("new Emblem missing")
	}
	if _, e = s.WithAddedEmblem("GID_リュール"); e == nil {
		t.Fatal("Engage+ creation accepted")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
