package app

import (
	"bytes"
	"fmt"
	"image/png"
	"os"
	"path/filepath"
	"slices"
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func orderedRosterFixture(t *testing.T) *App {
	t.Helper()
	a := fixture(t)
	for _, id := range []string{"PID_クラン", "PID_フラン"} {
		s, err := a.Save.WithAddedRosterCharacter(id)
		if err != nil {
			t.Fatal(err)
		}
		a.Save = s
	}
	a.Page = slices.Index(pages, "Roster")
	return a
}

func TestRosterOrderingKeepsSelectionAndFocusedEdits(t *testing.T) {
	a := orderedRosterFixture(t)
	original, err := os.ReadFile(a.Path)
	if err != nil {
		t.Fatal(err)
	}
	tt := ui.NewTester(a.View, 1120, 780)
	id := a.selectedID("roster")
	enterChapterValue(t, tt, a.T("Level"), "9")
	if err := tt.Click(a.T("MoveDown")); err != nil {
		t.Fatal(err)
	}
	current, found := a.selectedRosterCharacter()
	if !found || current.Index != 1 || current.Values.Level != 9 || a.selectedID("roster") != id {
		t.Fatalf("lost selection or focused edit: %+v (%s)", current, a.Status)
	}
	if err := tt.Click(a.T("MoveDown")); err != nil {
		t.Fatal(err)
	}
	current, _ = a.selectedRosterCharacter()
	if current.Index != 2 || a.selectedID("roster") != id {
		t.Fatal("repeated movement switched character")
	}
	enterChapterValue(t, tt, a.T("SP"), "1234")
	if err := tt.Click(a.T("SortByRecruitment")); err != nil {
		t.Fatal(err)
	}
	current, _ = a.selectedRosterCharacter()
	if current.Index != 0 || current.Values.Level != 9 || current.Values.SkillPoints != 1234 || a.selectedID("roster") != id {
		t.Fatalf("sort lost focused input or selected character: %+v (%s)", current, a.Status)
	}
	// Edit again after indices changed to catch stale callbacks or drafts.
	enterChapterValue(t, tt, a.T("SP"), "2345")
	if err := tt.Click(a.T("Search")); err != nil {
		t.Fatal(err)
	}
	current, _ = a.selectedRosterCharacter()
	if current.Values.SkillPoints != 2345 {
		t.Fatalf("post-sort edit failed: %+v pending=%v status=%s", current.Values, a.pendingOrder, a.Status)
	}
	output := filepath.Join(t.TempDir(), "edited")
	if err := a.SaveCopy(output); err != nil {
		t.Fatal(err)
	}
	s, err := save.Load(output)
	if err != nil {
		t.Fatal(err)
	}
	rows, err := s.Roster()
	if err != nil || rows[0].PersonHash != current.PersonHash || rows[0].Values.SkillPoints != 2345 {
		t.Fatal("saved order or character values did not reload")
	}
	after, err := os.ReadFile(a.Path)
	if err != nil || !bytes.Equal(after, original) {
		t.Fatal("GUI changed source file")
	}
}

func TestRosterOrderingWithSearchAndRejectedEdit(t *testing.T) {
	a := orderedRosterFixture(t)
	tt := ui.NewTester(a.View, 1120, 780)
	a.table("roster").Query = "Alear"
	tt.Frame()
	id := a.selectedID("roster")
	if err := tt.Click(a.T("MoveDown")); err != nil {
		t.Fatal(err)
	}
	current, _ := a.selectedRosterCharacter()
	if current.Index != 1 || len(a.table("roster").Rows) != 1 || a.selectedID("roster") != id {
		t.Fatal("search changed the move destination or selection")
	}
	before := a.Save.Bytes()
	a.stage("roster/"+id+"/invalid", func(s *save.Save) (*save.Save, error) {
		return nil, fmt.Errorf("invalid pending value")
	})
	moved := a.moveRosterCharacter(current.Index, 0)
	if moved || !bytes.Equal(before, a.Save.Bytes()) || a.selectedID("roster") != id || a.pending["roster/"+id+"/invalid"] == nil {
		t.Fatal("rejected edit changed order or discarded pending input")
	}
}

func TestRosterOrderControlLayouts(t *testing.T) {
	for _, loaded := range []bool{false, true} {
		for _, dark := range []bool{false, true} {
			for _, size := range [][2]int{{1120, 780}, {860, 600}} {
				a := New()
				if loaded {
					a = orderedRosterFixture(t)
				}
				a.Page = slices.Index(pages, "Roster")
				a.dark = dark
				tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
				checkBounds(t, tt, size[0], size[1])
				for _, label := range []string{"MoveUp", "MoveDown", "SortByRecruitment"} {
					n, ok := tt.Access(a.T(label))
					if !ok || n.Offscreen || !loaded && !n.Disabled {
						t.Fatalf("missing, offscreen or enabled unloaded control: %s", label)
					}
				}
			}
		}
	}
}

func TestProvidedRosterOrderScreenshots(t *testing.T) {
	path, dir := os.Getenv("FEE_TEST_SAVE"), os.Getenv("FEE_ROSTER_SCREENSHOT_DIR")
	if path == "" || dir == "" {
		t.Skip("set FEE_TEST_SAVE and FEE_ROSTER_SCREENSHOT_DIR")
	}
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	for _, dark := range []bool{false, true} {
		a := New()
		if err := a.Load(path); err != nil {
			t.Fatal(err)
		}
		a.Language, a.dark, a.Page = "en", dark, slices.Index(pages, "Roster")
		tt := ui.NewTesterWith(a.View, 1120, 780, ui.TesterOptions{Dark: dark})
		if err := tt.Click(a.T("SortByRecruitment")); err != nil {
			t.Fatal(err)
		}
		tt.Frame()
		checkBounds(t, tt, 1120, 780)
		rows, _ := a.Save.Roster()
		if len(rows) != 41 || rows[1].PersonHash != catalog.Hash("PID_ヴァンドレ") || rows[8].PersonHash != catalog.Hash("PID_クロエ") {
			t.Fatal("real save sort did not follow the configured order")
		}
		theme := "light"
		if dark {
			theme = "dark"
		}
		if err := os.MkdirAll(dir, 0755); err != nil {
			t.Fatal(err)
		}
		f, err := os.Create(filepath.Join(dir, "roster-order-"+theme+".png"))
		if err != nil {
			t.Fatal(err)
		}
		err = png.Encode(f, tt.Image())
		f.Close()
		if err != nil {
			t.Fatal(err)
		}
	}
	after, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(before, after) {
		t.Fatal("screenshots changed source save")
	}
}
