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

func TestMinigameCardRows(t *testing.T) {
	for _, dark := range []bool{false, true} {
		for _, size := range [][2]int{{1120, 780}, {860, 600}} {
			a := fixture(t)
			a.Page, a.dark = 5, dark
			tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
			cards := map[string]ui.Rect{}
			for _, g := range catalog.Minigames {
				n, ok := tt.AccessByRole(ui.RoleGroup, g.Names.Name(a.Language))
				if !ok {
					t.Fatalf("missing card %s", g.Id)
				}
				cards[g.Id] = n.Bounds
			}
			for _, pair := range [][2]string{{"PushUps", "SitUps"}, {"Squats", "WyvernRide"}} {
				left, right := cards[pair[0]], cards[pair[1]]
				if left.Y != right.Y || left.H != right.H || left.W != right.W {
					t.Errorf("%v: cards have different bounds: %+v / %+v", pair, left, right)
				}
			}
		}
	}
}

func TestUnloadedFormsStayVisible(t *testing.T) {
	for _, dark := range []bool{false, true} {
		for _, size := range [][2]int{{1120, 780}, {860, 600}} {
			a := New()
			a.dark = dark
			tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
			for _, v := range testViews() {
				a.Page = v.page
				a.revision++
				if v.key != "" {
					*a.tab(v.key) = v.tab
				}
				tt.Frame()
				labels := []string{}
				switch pages[v.page] {
				case "Main":
					for _, d := range catalog.DonationCountries {
						labels = append(labels, replaceFormat(a.T("CountryLevel"), d.Names.Name(a.Language)))
					}
				case "Items":
					if v.tab == 0 {
						labels = []string{"Item", "RemainingUses", "RefineLevel", "Engraving"}
					} else {
						labels = []string{"Amount", "ApplyChanges"}
					}
				case "Roster":
					switch v.tab {
					case 0:
						labels = []string{"Class", "Weapons", "Level", "SkillPoints"}
					case 1:
						for i, name := range save.StatNames {
							if save.RosterStat(i) != save.Sight {
								labels = append(labels, name)
							}
						}
					case 2:
						labels = []string{"Item", "RemainingUses", "RefineLevel", "Engraving"}
					case 3:
						labels = []string{"EquippedSkills", "ClassSkill", "InheritedSkills", "UnlockSkill"}
					case 4:
						labels = []string{"Sword", "Lance", "Arts"}
					case 5:
						labels = []string{"Kind", "Equipment", "Unequip"}
					}
				case "Emblems":
					if v.tab == 0 {
						labels = []string{"BondLevel", "Experience", "Dirtiness"}
					} else {
						labels = []string{"Amount", "EquippedBy"}
					}
				case "Support":
					labels = []string{"Rank", "Points"}
				case "Achievements":
					labels = []string{"Category", "Status", "BondFragments", "UnlockAchievement"}
				case "Animals":
					labels = []string{"Amount", "MaxAmount"}
				case "Amiibo":
					count := 0
					for _, n := range tt.AccessAll() {
						if n.Role == ui.RoleCheckBox {
							count++
						}
					}
					if count != len(catalog.AmiiboRewards) {
						t.Errorf("unloaded amiibo has %d checkboxes, want %d", count, len(catalog.AmiiboRewards))
					}
				case "Minigames":
					labels = []string{"BestScore", "Caught", "BestSize", "BestRank"}
				case "Inspector":
					if v.tab == 0 {
						labels = []string{"FormatVersion", "GameVersion", "Checksum", "PayloadSize"}
					} else {
						labels = []string{"Value", "ApplyChanges"}
					}
				}
				for _, label := range labels {
					if _, ok := tt.Find(a.T(label)); !ok {
						t.Errorf("%s-%d: unloaded control %s is missing", pages[v.page], v.tab, label)
					}
				}
				for _, n := range tt.AccessAll() {
					switch n.Role {
					case ui.RoleButton, ui.RoleTextField, ui.RoleComboBox, ui.RolePopUpButton, ui.RoleCheckBox:
						if !n.Disabled {
							t.Errorf("%s-%d: unloaded control %s is enabled", pages[v.page], v.tab, n.Name)
						}
					}
				}
				if dir := os.Getenv("FEE_AUDIT_DIR"); dir != "" && size[0] == 1120 {
					theme := "light"
					if dark {
						theme = "dark"
					}
					path := filepath.Join(dir, theme, fmt.Sprintf("Unloaded-%s-%d.png", pages[v.page], v.tab))
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
			}
			if a.Save != nil || len(a.pending) != 0 {
				t.Fatal("unloaded views created save data or staged edits")
			}
		}
	}
}
