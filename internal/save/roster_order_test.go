package save

import (
	"bytes"
	"os"
	"path/filepath"
	"reflect"
	"slices"
	"testing"

	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

func rosterOrderFixture(t *testing.T) *Save {
	t.Helper()
	s := loadFixture(t, "creation")
	for _, id := range []string{"PID_フラン", "PID_クラン", "PID_アンナ"} {
		var err error
		s, err = s.WithAddedRosterCharacter(id)
		if err != nil {
			t.Fatal(err)
		}
	}
	return s
}

func assertRosterPermutation(t *testing.T, before, after *Save) {
	t.Helper()
	if before.Length() != after.Length() || !reflect.DeepEqual(before.Sections, after.Sections) {
		t.Fatal("reordering changed the container layout")
	}
	sec := before.Section("UNIT")
	p, end := sec.PayloadOffset(), sec.PayloadOffset()+sec.Length
	if !bytes.Equal(before.data[:p], after.data[:p]) || !bytes.Equal(before.data[end:len(before.data)-4], after.data[end:len(after.data)-4]) {
		t.Fatal("reordering changed data outside UNIT and its checksum")
	}
	old, next := before.roster(), after.roster()
	if len(old.characters) != len(next.characters) {
		t.Fatal("character count changed")
	}
	oldLinks, nextLinks := before.equipmentLocations(), after.equipmentLocations()
	validateEquipmentLinks(nextLinks, after.ownedEmblems())
	if _, err := after.BondRings(); err != nil {
		t.Fatal(err)
	}
	for _, c := range next.characters {
		found := false
		for _, original := range old.characters {
			if original.character.PersonHash != c.character.PersonHash {
				continue
			}
			found = true
			if !bytes.Equal(before.data[original.start:original.end], after.data[c.start:c.end]) {
				t.Fatal("a character's raw data changed")
			}
			a, b := original.character, c.character
			a.Index, b.Index = 0, 0
			if !reflect.DeepEqual(a, b) {
				t.Fatal("a character's state changed")
			}
			x, y := oldLinks[original.character.Index].links, nextLinks[c.character.Index].links
			x.CharacterIndex, y.CharacterIndex = 0, 0
			if x != y {
				t.Fatal("a ring or Emblem changed owner")
			}
		}
		if !found {
			t.Fatal("unknown character added")
		}
	}
	if _, err := Parse(after.Bytes()); err != nil {
		t.Fatal(err)
	}
}

func checkRosterOrdering(t *testing.T, s *Save) {
	t.Helper()
	original := s.Bytes()
	order, err := s.RosterOrder()
	if err != nil || len(order) < 3 {
		t.Fatalf("order: %v %v", order, err)
	}
	for _, direction := range [][2]int{{0, len(order) - 1}, {len(order) - 1, 0}, {1, 2}, {1, 1}} {
		from, to := direction[0], direction[1]
		moved, err := s.MoveRosterCharacter(order[from], order[to])
		if err != nil {
			t.Fatal(err)
		}
		assertRosterPermutation(t, s, moved)
		rows, _ := moved.Roster()
		old, _ := s.Roster()
		if rows[order[to]].PersonHash != old[order[from]].PersonHash {
			t.Fatal("character moved to the wrong position")
		}
		back, err := moved.MoveRosterCharacter(order[to], order[from])
		if err != nil || !bytes.Equal(back.Bytes(), original) {
			t.Fatal("inverse move did not restore the exact original file")
		}
	}
	reverse := slices.Clone(order)
	slices.Reverse(reverse)
	reversed, err := s.WithRosterOrder(reverse)
	if err != nil {
		t.Fatal(err)
	}
	assertRosterPermutation(t, s, reversed)
	sorted, err := reversed.SortRosterByRecruitment()
	if err != nil {
		t.Fatal(err)
	}
	assertRosterPermutation(t, s, sorted)
	rows, _ := sorted.Roster()
	rank := map[uint32]int{}
	for i, id := range catalog.RecruitmentOrder {
		rank[catalog.Hash(id)] = i
	}
	last := -1
	for _, index := range order {
		if rank[rows[index].PersonHash] <= last {
			t.Fatal("recruitment order was not restored")
		}
		last = rank[rows[index].PersonHash]
	}
	again, err := sorted.SortRosterByRecruitment()
	if err != nil || !bytes.Equal(again.Bytes(), sorted.Bytes()) {
		t.Fatal("sorting twice changed the file")
	}
	path := filepath.Join(t.TempDir(), "reordered")
	if err := sorted.WriteCopy(path); err != nil {
		t.Fatal(err)
	}
	reloaded, err := Load(path)
	if err != nil || !bytes.Equal(reloaded.Bytes(), sorted.Bytes()) {
		t.Fatal("saved order failed to reload")
	}
	if !bytes.Equal(original, s.Bytes()) {
		t.Fatal("input save changed")
	}
}

func TestRosterOrdering(t *testing.T) {
	checkRosterOrdering(t, rosterOrderFixture(t))
}

func TestRosterOrderingLimitsAndExcludedUnits(t *testing.T) {
	s := rosterOrderFixture(t)
	order, _ := s.RosterOrder()
	for _, invalid := range [][]int{nil, order[:len(order)-1], append(slices.Clone(order), 99), {0, 0, 2, 3, 4}, {0, 1, 2, 3, -1}, {0, 1, 2, 3, 99}} {
		if _, err := s.WithRosterOrder(invalid); err == nil {
			t.Fatalf("accepted invalid permutation %v", invalid)
		}
	}
	for _, pair := range [][2]int{{-1, 0}, {0, -1}, {99, 0}, {0, 99}} {
		if _, err := s.MoveRosterCharacter(pair[0], pair[1]); err == nil {
			t.Fatal("accepted an out-of-bounds destination")
		}
	}
	lost, err := s.WithRosterForce(1, Lost)
	if err != nil {
		t.Fatal(err)
	}
	rows, _ := lost.Roster()
	if _, err := lost.MoveRosterCharacter(0, len(rows)-1); err == nil {
		t.Fatal("allowed a move into another force")
	}
	sorted, err := lost.SortRosterByRecruitment()
	if err != nil {
		t.Fatal(err)
	}
	assertRosterPermutation(t, lost, sorted)
	if sorted.roster().get(len(rows)-1).character.Force != Lost {
		t.Fatal("sorting restored a lost unit")
	}
	unknown := s.Bytes()
	put32(unknown, s.roster().get(2).classStart-4, 0xdeadbeef)
	u := verified(unknown)
	before := u.roster().get(2)
	sorted, err = u.SortRosterByRecruitment()
	if err != nil {
		t.Fatal(err)
	}
	assertRosterPermutation(t, u, sorted)
	after := sorted.roster().get(2)
	if !bytes.Equal(u.data[before.start:before.end], sorted.data[after.start:after.end]) {
		t.Fatal("unknown unit did not keep its position")
	}
	for _, name := range []string{"global", "roster"} {
		// roster.bin deliberately has an unsupported USER context.
		if _, err := loadFixture(t, name).SortRosterByRecruitment(); err == nil {
			t.Fatalf("accepted unsupported %s context", name)
		}
	}
	duplicate := s.Bytes()
	put32(duplicate, s.roster().get(1).classStart-4, s.roster().get(0).character.PersonHash)
	if _, err := verified(duplicate).SortRosterByRecruitment(); err == nil {
		t.Fatal("duplicate playable units accepted")
	}
}

func TestProvidedRosterOrdering(t *testing.T) {
	path := os.Getenv("FEE_TEST_SAVE")
	if path == "" {
		t.Skip("set FEE_TEST_SAVE to test a real save without changing it")
	}
	b, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	s, err := Parse(b)
	if err != nil {
		t.Fatal(err)
	}
	checkRosterOrdering(t, s)
	after, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(b, after) {
		t.Fatal("real source file changed")
	}
}
