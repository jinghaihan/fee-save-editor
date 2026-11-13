package save

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func TestCharacterTransferRoundTrip(t *testing.T) {
	s := loadFixture(t, "roster")
	original := s.Bytes()
	exported, e := s.ExportRosterCharacter(0)
	if e != nil {
		t.Fatal(e)
	}
	unchanged, e := s.ImportRosterCharacter(0, exported)
	if e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(unchanged.Bytes(), original) {
		t.Fatal("unchanged import changed save bytes")
	}
	transfer, e := ReadRosterTransfer(exported)
	if e != nil {
		t.Fatal(e)
	}
	transfer.Values.SkillPoints = 9876
	transfer.PersonalStats[1]++
	changed, _ := json.Marshal(transfer)
	edited, e := s.ImportRosterCharacter(0, changed)
	if e != nil {
		t.Fatal(e)
	}
	after, e := edited.ExportRosterCharacter(0)
	if e != nil {
		t.Fatal(e)
	}
	actual, _ := ReadRosterTransfer(after)
	if actual.Values.SkillPoints != 9876 || actual.PersonalStats[1] != transfer.PersonalStats[1] {
		t.Fatal("imported values mismatch")
	}
	restored, e := edited.ImportRosterCharacter(0, exported)
	if e != nil || !bytes.Equal(restored.Bytes(), original) {
		t.Fatal("import restoration differs")
	}
	if _, e = s.ImportRosterCharacter(1, exported); e == nil {
		t.Fatal("cross-character import accepted")
	}
	for _, bad := range [][]byte{[]byte(`{}`), append(exported, []byte(` {}`)...), bytes.Replace(exported, []byte(`"Version": 1`), []byte(`"Version": 1,"Version":1`), 1), bytes.Replace(exported, []byte(`"Level": 5`), []byte(`"Level": null`), 1), bytes.Replace(exported, []byte(`"Version": 1`), []byte(`"Version": 1,"Unknown":1`), 1)} {
		if _, e = s.ImportRosterCharacter(0, bad); e == nil {
			t.Fatalf("malformed transfer accepted: %.60s", bad)
		}
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestPrivateSavesReadAndTransfer(t *testing.T) {
	directory := os.Getenv("FEE_TEST_SAVES")
	if directory == "" {
		t.Skip("private save directory is optional and never checked in")
	}
	for _, name := range []string{"Manual0", "Auto"} {
		t.Run(name, func(t *testing.T) {
			path := filepath.Join(directory, name)
			original, e := os.ReadFile(path)
			if e != nil {
				t.Fatal(e)
			}
			s, e := Parse(original)
			if e != nil {
				t.Fatal(e)
			}
			if _, e = s.Main(); e != nil {
				t.Fatal(e)
			}
			if _, e = s.Inventory(); e != nil {
				t.Fatal(e)
			}
			roster, e := s.Roster()
			if e != nil {
				t.Fatal(e)
			}
			if _, e = s.Emblems(); e != nil {
				t.Fatal(e)
			}
			if _, e = s.BondRings(); e != nil {
				t.Fatal(e)
			}
			if _, e = s.Supports(); e != nil {
				t.Fatal(e)
			}
			if _, e = s.Variables(); e != nil {
				t.Fatal(e)
			}
			for _, c := range roster {
				if !equipmentCharacter(c) {
					continue
				}
				exported, e := s.ExportRosterCharacter(c.Index)
				if e != nil {
					t.Fatal(e)
				}
				copy, e := s.ImportRosterCharacter(c.Index, exported)
				if e != nil {
					t.Fatalf("character %d: %v", c.Index, e)
				}
				if !bytes.Equal(copy.Bytes(), original) {
					t.Fatalf("character %d import changed bytes", c.Index)
				}
				break
			}
			after, e := os.ReadFile(path)
			if e != nil || !bytes.Equal(after, original) {
				t.Fatal("private save file changed")
			}
		})
	}
}
