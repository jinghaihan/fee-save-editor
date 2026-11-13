package app

import (
	"bytes"
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"os"
	"path/filepath"
	"testing"
)

func TestDisplayedStatsEditAndRoundTrip(t *testing.T) {
	for _, stat := range []save.RosterStat{save.HP, save.Strength, save.Movement} {
		for _, atLimit := range []bool{false, true} {
			t.Run(fmt.Sprintf("%s/limit=%v", save.StatNames[stat], atLimit), func(t *testing.T) {
				a := fixture(t)
				a.Page = 2
				*a.tab("roster-tabs") = 1
				source, err := os.ReadFile(a.Path)
				if err != nil {
					t.Fatal(err)
				}
				rows, err := a.Save.Roster()
				if err != nil {
					t.Fatal(err)
				}
				limit := *rows[0].Stats[stat].Maximum
				want := limit - 1
				if atLimit {
					want = limit
				}
				expected, err := a.Save.WithRosterStat(0, stat, want, false)
				if err != nil {
					t.Fatal(err)
				}
				tt := ui.NewTester(a.View, 1120, 780)
				group, ok := tt.AccessByRole(ui.RoleGroup, a.T(save.StatNames[stat]))
				if !ok {
					t.Fatal("missing stat control")
				}
				focused := false
				for _, node := range tt.AccessAll() {
					if node.Role == ui.RoleTextField && node.Bounds.X >= group.Bounds.X && node.Bounds.Y >= group.Bounds.Y && node.Bounds.X+node.Bounds.W <= group.Bounds.X+group.Bounds.W && node.Bounds.Y+node.Bounds.H <= group.Bounds.Y+group.Bounds.H {
						tt.ClickAt(node.Bounds.X+node.Bounds.W/2, node.Bounds.Y+node.Bounds.H/2)
						focused = true
						break
					}
				}
				if !focused {
					t.Fatal("missing stat input")
				}
				tt.Key(modifier(), ui.KeyA)
				tt.Type(fmt.Sprint(want))
				if err := tt.Click(a.T("CurrentHP")); err != nil {
					t.Fatal(err)
				}
				updated, err := a.Save.Roster()
				if err != nil {
					t.Fatal(err)
				}
				if updated[0].Stats[stat].Value != want {
					t.Fatalf("displayed stat = %d, want %d (%s)", updated[0].Stats[stat].Value, want, a.Status)
				}
				if !bytes.Equal(expected.Bytes(), a.Save.Bytes()) {
					t.Fatal("stat edit changed unrelated data")
				}
				out := filepath.Join(t.TempDir(), "edited")
				if err := a.SaveCopy(out); err != nil {
					t.Fatal(err)
				}
				loaded, err := save.Load(out)
				if err != nil {
					t.Fatal(err)
				}
				if !bytes.Equal(expected.Bytes(), loaded.Bytes()) {
					t.Fatal("displayed stat did not round trip")
				}
				after, err := os.ReadFile(a.Path)
				if err != nil || !bytes.Equal(source, after) {
					t.Fatal("editor changed source file")
				}
			})
		}
	}
}
