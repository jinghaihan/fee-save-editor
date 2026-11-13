package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"os"
	"testing"
)

func progressFixture(t *testing.T) *Save {
	t.Helper()
	b, e := os.ReadFile("testdata/main.bin")
	if e != nil {
		t.Fatal(e)
	}
	s, e := Parse(b)
	if e != nil {
		t.Fatal(e)
	}
	return s
}
func TestQuantityInsertionCanBeRestored(t *testing.T) {
	s := progressFixture(t)
	original := s.Bytes()
	d := catalog.QuantityItems[0]
	rows, e := s.Quantities()
	if e != nil {
		t.Fatal(e)
	}
	edited, e := s.WithQuantity(d.Id, d.Maximum)
	if e != nil {
		t.Fatal(e)
	}
	after, e := edited.Quantities()
	if e != nil {
		t.Fatal(e)
	}
	if after[0].Amount != d.Maximum {
		t.Fatal("quantity not stored")
	}
	if _, e = edited.WithQuantity(d.Id, d.Maximum+1); e == nil {
		t.Fatal("invalid quantity accepted")
	}
	restored, e := edited.WithQuantity(d.Id, rows[0].Amount)
	if e != nil {
		t.Fatal(e)
	}
	r, e := restored.Quantities()
	if e != nil || r[0].Amount != rows[0].Amount {
		t.Fatal("quantity restore failed")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
	a, e := s.Animals()
	if e != nil || len(a) != 24 {
		t.Fatalf("animals: %d %v", len(a), e)
	}
	ed, e := s.WithAnimal(a[0].Definition.Id, 99, false)
	if e != nil {
		t.Fatal(e)
	}
	a, e = ed.Animals()
	if e != nil || a[0].Amount != 99 {
		t.Fatal("animal amount not stored")
	}
}

func TestProgressCatalogEdits(t *testing.T) {
	s := progressFixture(t)
	original := s.Bytes()
	donations, e := s.Donations()
	if e != nil || len(donations) != 4 {
		t.Fatalf("donations: %v", e)
	}
	edited, e := s.WithDonation(donations[0].Country.Id, 90000)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := edited.Donations()
	if e != nil || rows[0].Level != 5 {
		t.Fatalf("donation level: %+v %v", rows, e)
	}
	if _, e = s.WithDonation(donations[0].Country.Id, 10000000); e == nil {
		t.Fatal("donation limit ignored")
	}
	rewards, e := s.Amiibo()
	if e != nil || len(rewards) != 24 {
		t.Fatalf("rewards: %v", e)
	}
	edited, e = s.UnlockAmiibo([]string{rewards[0].Definition.Key})
	if e != nil {
		t.Fatal(e)
	}
	rewards, e = edited.Amiibo()
	if e != nil || !rewards[0].Unlocked {
		t.Fatal("reward not unlocked")
	}
	achievements, e := s.Achievements()
	if e != nil || len(achievements) != 765 {
		t.Fatalf("achievements: %v", e)
	}
	id := achievements[0].Definition.Id
	edited, e = s.UnlockAchievements(id)
	if e != nil {
		t.Fatal(e)
	}
	a, e := edited.Achievements()
	if e != nil || !a[0].RewardAvailable || a[0].RewardClaimed || a[0].Status != 1 {
		t.Fatal("achievement reward state changed")
	}
	again, e := edited.UnlockAchievements(id)
	if e != nil || !bytes.Equal(edited.Bytes(), again.Bytes()) {
		t.Fatal("unlock is not idempotent")
	}
	if _, e = s.WithAnimal(catalog.Animals[0].Id, 100, false); e == nil {
		t.Fatal("animal limit ignored")
	}
	if !bytes.Equal(s.Bytes(), original) {
		t.Fatal("source mutated")
	}
}
func TestMinigameAndActivityCounters(t *testing.T) {
	s := progressFixture(t)
	rows, e := s.MinigameRecords()
	if e != nil || len(rows) != 35 {
		t.Fatalf("minigames: %d %v", len(rows), e)
	}
	var fish MinigameRecord
	for _, row := range rows {
		if row.BestSize != nil {
			fish = row
			break
		}
	}
	rank, size := 5, 15
	edited, e := s.WithMinigame(fish.Definition.Key, MinigameValues{100, &rank, &size})
	if e != nil {
		t.Fatal(e)
	}
	updated, e := edited.MinigameRecords()
	if e != nil {
		t.Fatal(e)
	}
	for i, row := range updated {
		if row.Definition.Key == fish.Definition.Key {
			if row.Value != 100 || *row.Rank != rank || *row.BestSize != size {
				t.Fatal("fishing record mismatch")
			}
		} else if row.Value != rows[i].Value {
			t.Fatal("other minigame changed")
		}
	}
	rank = 6
	if _, e = s.WithMinigame(fish.Definition.Key, MinigameValues{100, &rank, &size}); e == nil {
		t.Fatal("invalid fishing rank accepted")
	}
	activityBytes := s.Bytes()
	money := s.mainLayout().MoneyOffset
	put32(activityBytes, money+8, 1)
	put32(activityBytes, money+12, 2)
	s = verified(activityBytes)
	old, e := s.Activities()
	if e != nil {
		t.Fatal(e)
	}
	edited, e = s.WithActivities(ActivityValues{1, 3})
	if e != nil {
		t.Fatal(e)
	}
	restored, e := edited.WithActivities(old)
	if e != nil || !bytes.Equal(restored.Bytes(), s.Bytes()) {
		t.Fatal("activity restoration mismatch")
	}
	if _, e = s.WithActivities(ActivityValues{2, 3}); e == nil {
		t.Fatal("training limit accepted")
	}
}
