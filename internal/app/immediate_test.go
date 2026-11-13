package app

import (
	"bytes"
	"os"
	"path/filepath"
	"reflect"
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func chooseOption(t *testing.T, tt *ui.Tester, label, value string) {
	t.Helper()
	if err := tt.Click(label); err != nil {
		t.Fatal(err)
	}
	tt.Key(modifier(), ui.KeyA)
	tt.Type(value)
	tt.Key(0, ui.KeyEnter)
}

func TestOrdinaryEditorsHaveNoConfirmationButton(t *testing.T) {
	a := fixture(t)
	tt := ui.NewTester(a.View, 1120, 780)
	for _, v := range testViews() {
		a.Page = v.page
		if v.key != "" {
			*a.tab(v.key) = v.tab
		}
		a.revision++
		tt.Frame()
		for _, label := range []string{"ApplyChanges", "ChangeClass", "MoveCharacter"} {
			if _, ok := tt.AccessByRole(ui.RoleButton, a.T(label)); ok {
				t.Errorf("%s-%d still has %s", pages[v.page], v.tab, label)
			}
		}
	}
}

func TestFocusedInputSavesAndBulkActionWins(t *testing.T) {
	for _, bulk := range []bool{false, true} {
		a := fixture(t)
		a.Page = 4
		before, err := os.ReadFile(a.Path)
		if err != nil {
			t.Fatal(err)
		}
		tt := ui.NewTester(a.View, 1120, 780)
		if err := tt.Click(a.T("Points")); err != nil {
			t.Fatal(err)
		}
		tt.Key(modifier(), ui.KeyA)
		tt.Type("5")
		if bulk {
			if err := tt.Click(a.T("MaxSupport")); err != nil {
				t.Fatal(err)
			}
		}
		output := filepath.Join(t.TempDir(), "edited")
		if err := a.SaveCopy(output); err != nil {
			t.Fatal(err)
		}
		reloaded, err := save.Load(output)
		if err != nil {
			t.Fatal(err)
		}
		rows, err := reloaded.Supports()
		if err != nil || len(rows) == 0 {
			t.Fatalf("supports: %v", err)
		}
		if !bulk && rows[0].Points != 5 || bulk && rows[0].Points <= 5 {
			t.Fatalf("pending input replaced operation: %+v", rows[0])
		}
		source, err := os.ReadFile(a.Path)
		if err != nil || !bytes.Equal(before, source) {
			t.Fatal("editing changed the source")
		}
	}
}

func TestEquipmentSelectionAppliesWithoutConfirmation(t *testing.T) {
	a := fixture(t)
	a.Page = 2
	*a.tab("roster-tabs") = 5
	options, err := a.Save.RosterEquipmentOptions(0)
	if err != nil {
		t.Fatal(err)
	}
	var want save.EquipmentSelection
	for _, o := range options {
		if o.Selection.Kind == save.EquipmentEmblem {
			want = o.Selection
			break
		}
	}
	if want.Kind == save.EquipmentNone {
		t.Fatal("fixture has no emblem equipment")
	}
	tt := ui.NewTester(a.View, 1120, 780)
	for _, unequip := range []bool{false, true} {
		label := a.T("Emblem")
		if unequip {
			label = a.T("None")
			want = save.EquipmentSelection{}
		}
		chooseOption(t, tt, a.T("Kind"), label)
		got, err := a.Save.RosterEquipment(0)
		if err != nil || got != want {
			t.Fatalf("memory equipment: %+v, want %+v: %v (%s)", got, want, err, a.Status)
		}
		path := filepath.Join(t.TempDir(), "equipment")
		if err := a.SaveCopy(path); err != nil {
			t.Fatal(err)
		}
		reloaded, err := save.Load(path)
		if err != nil {
			t.Fatal(err)
		}
		got, err = reloaded.RosterEquipment(0)
		if err != nil || got != want {
			t.Fatalf("saved equipment: %+v: %v", got, err)
		}
	}
}

func TestForceSelectionAppliesWithoutConfirmation(t *testing.T) {
	a := fixture(t)
	edited, err := a.Save.WithAddedRosterCharacter("PID_フラン")
	if err != nil {
		t.Fatal(err)
	}
	a.Save = edited
	a.Page = 2
	a.table("roster").Selected = 2
	tt := ui.NewTester(a.View, 1120, 780)
	chooseOption(t, tt, a.T("Force"), a.T("ForceLost"))
	check := func(s *save.Save) {
		rows, err := s.Roster()
		if err != nil {
			t.Fatal(err)
		}
		for _, r := range rows {
			if r.Force == save.Lost {
				return
			}
		}
		t.Fatalf("force selection not applied: %s", a.Status)
	}
	check(a.Save)
	output := filepath.Join(t.TempDir(), "force")
	if err := a.SaveCopy(output); err != nil {
		t.Fatal(err)
	}
	saved, err := save.Load(output)
	if err != nil {
		t.Fatal(err)
	}
	check(saved)
}

func TestRosterNumericEditPreservesOtherFields(t *testing.T) {
	a := fixture(t)
	a.Page = 2
	before, err := a.Save.Roster()
	if err != nil {
		t.Fatal(err)
	}
	tt := ui.NewTester(a.View, 1120, 780)
	if err := tt.Click(a.T("SkillPoints")); err != nil {
		t.Fatal(err)
	}
	tt.Key(modifier(), ui.KeyA)
	tt.Type("555")
	if err := tt.Click(a.T("InternalLevel")); err != nil {
		t.Fatal(err)
	}
	after, err := a.Save.Roster()
	if err != nil {
		t.Fatal(err)
	}
	before[0].Values.SkillPoints = 555
	if !reflect.DeepEqual(before, after) {
		t.Fatal("editing SP changed unrelated roster fields")
	}
}
