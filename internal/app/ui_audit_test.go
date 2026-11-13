package app

import (
	"bytes"
	"fmt"
	"image/png"
	"os"
	"path/filepath"
	"reflect"
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func TestCreationPopoversAndCancel(t *testing.T) {
	for _, dark := range []bool{false, true} {
		for _, size := range [][2]int{{1120, 780}, {860, 600}} {
			for _, popup := range []struct {
				page          int
				anchor, title string
			}{{2, "Add", "AddCharacter"}, {3, "AddEmblem", "AddEmblem"}} {
				t.Run(fmt.Sprintf("%s/dark=%v/%dx%d", popup.title, dark, size[0], size[1]), func(t *testing.T) {
					a := fixture(t)
					a.Page, a.dark = popup.page, dark
					before := a.Save.Bytes()
					tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
					if err := tt.Click(a.T(popup.anchor)); err != nil {
						t.Fatal(err)
					}
					if _, ok := tt.AccessByRole(ui.RoleText, a.T(popup.title)); !ok {
						t.Fatal("creation popover did not open")
					}
					checkBounds(t, tt, size[0], size[1])
					if dir := os.Getenv("FEE_AUDIT_DIR"); dir != "" && size[0] == 1120 {
						theme := "light"
						if dark {
							theme = "dark"
						}
						path := filepath.Join(dir, theme, popup.title+".png")
						if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
							t.Fatal(err)
						}
						f, err := os.Create(path)
						if err != nil {
							t.Fatal(err)
						}
						err = png.Encode(f, tt.Image())
						f.Close()
						if err != nil {
							t.Fatal(err)
						}
					}
					tt.Key(0, ui.KeyEscape)
					if _, ok := tt.AccessByRole(ui.RoleText, a.T(popup.title)); ok {
						t.Fatal("Escape left the popover open")
					}
					a.ask("ConfirmDeleteCharacter", func() { t.Error("cancel executed the action") })
					tt.Frame()
					if err := tt.Click(a.T("Cancel")); err != nil {
						t.Fatal(err)
					}
					if a.confirm || !bytes.Equal(before, a.Save.Bytes()) {
						t.Fatal("cancel changed the save or left the dialog open")
					}
				})
			}
		}
	}
}

func TestMinigameTableEditing(t *testing.T) {
	for _, edit := range []struct{ group, field, text string }{{"PushUps", "value", "123"}, {"WyvernRide", "value", "456"}, {"WyvernRide", "rank", "SSS"}, {"Fishing", "value", "7"}, {"Fishing", "size", "25"}, {"Fishing", "rank", "Large"}} {
		t.Run(edit.group+"/"+edit.field, func(t *testing.T) {
			a := fixture(t)
			a.Page = 5
			before, err := a.Save.MinigameRecords()
			if err != nil {
				t.Fatal(err)
			}
			source := a.Save.Bytes()
			var group catalog.MinigameGroup
			for _, g := range catalog.Minigames {
				if g.Id == edit.group {
					group = g
				}
			}
			record := group.Records[0]
			field := map[string]string{"value": "BestScore", "size": "BestSize", "rank": "BestRank"}[edit.field]
			if edit.group == "Fishing" && edit.field == "value" {
				field = "Caught"
			}
			label := group.Names.Name(a.Language) + " — " + record.Names.Name(a.Language) + " — " + a.T(field)
			tt := ui.NewTester(a.View, 1120, 780)
			if edit.field == "rank" {
				if err := tt.Click(label); err != nil {
					t.Fatal(err)
				}
				if err := tt.Click(a.T(edit.text)); err != nil {
					t.Fatal(err)
				}
			} else {
				group, ok := tt.AccessByRole(ui.RoleGroup, label)
				if !ok {
					t.Fatal("missing numeric control group")
				}
				found := false
				for _, n := range tt.AccessAll() {
					if n.Role == ui.RoleTextField && n.Bounds.X >= group.Bounds.X && n.Bounds.Y >= group.Bounds.Y && n.Bounds.X+n.Bounds.W <= group.Bounds.X+group.Bounds.W && n.Bounds.Y+n.Bounds.H <= group.Bounds.Y+group.Bounds.H {
						tt.ClickAt(n.Bounds.X+n.Bounds.W/2, n.Bounds.Y+n.Bounds.H/2)
						found = true
						break
					}
				}
				if !found {
					t.Fatal("numeric group has no input")
				}
				tt.Key(modifier(), ui.KeyA)
				tt.Type(edit.text)
				tt.Key(0, ui.KeyTab)
			}
			if !bytes.Equal(source, a.Save.Bytes()) {
				t.Fatal("input applied before saving")
			}
			output := filepath.Join(t.TempDir(), "copy")
			if err := a.SaveCopy(output); err != nil {
				t.Fatal(err)
			}
			saved, err := save.Load(output)
			if err != nil {
				t.Fatal(err)
			}
			after, err := saved.MinigameRecords()
			if err != nil {
				t.Fatal(err)
			}
			for i, old := range before {
				want := old
				if old.Definition.Key == record.Key {
					switch edit.field {
					case "value":
						fmt.Sscan(edit.text, &want.Value)
					case "size":
						n := 25
						want.BestSize = &n
					case "rank":
						n := 1
						if edit.text == "Large" {
							n = 3
						}
						want.Rank = &n
					}
				}
				if !reflect.DeepEqual(after[i], want) {
					t.Fatalf("unexpected record update: got %+v, want %+v", after[i], want)
				}
			}
		})
	}
}
