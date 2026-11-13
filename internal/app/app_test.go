package app

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"hash/crc32"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"runtime"
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func fixture(t *testing.T) *App {
	t.Helper()
	base, err := save.Load("../save/testdata/creation.bin")
	if err != nil {
		t.Fatal(err)
	}
	inventory, err := save.Load("../save/testdata/inventory.bin")
	if err != nil {
		t.Fatal(err)
	}
	bytesBase, bytesInventory := base.Bytes(), inventory.Bytes()
	body := append([]byte{}, bytesBase[:128]...)
	body = append(body, make([]byte, 132)...)
	copy(body[128:], []byte("EDNI"))
	sections := [][]byte{}
	for _, sec := range base.Sections {
		sections = append(sections, bytesBase[sec.Offset:sec.PayloadOffset()+sec.Length])
	}
	sec := inventory.Section("TRAN")
	sections = append(sections, bytesInventory[sec.Offset:sec.PayloadOffset()+sec.Length])
	for i, section := range sections {
		binary.LittleEndian.PutUint32(body[132+i*4:], uint32(len(body)))
		body = append(body, section...)
	}
	binary.LittleEndian.PutUint32(body[132+len(sections)*4:], uint32(len(body)))
	body = append(body, []byte("LVRC")...)
	body = binary.LittleEndian.AppendUint32(body, crc32.ChecksumIEEE(body))
	path := filepath.Join(t.TempDir(), "source")
	if err := os.WriteFile(path, body, 0600); err != nil {
		t.Fatal(err)
	}
	a := New()
	if err := a.Load(path); err != nil {
		t.Fatal(err)
	}
	return a
}
func testViews() []struct {
	page, tab int
	key       string
} {
	var out []struct {
		page, tab int
		key       string
	}
	for page, name := range pages {
		key := ""
		count := 1
		switch name {
		case "Items":
			key = "items"
			count = 6
		case "Roster":
			key = "roster-tabs"
			count = 6
		case "Emblems":
			key = "emblem-pages"
			count = 2
		case "Inspector":
			key = "inspector"
			count = 2
		}
		for tab := 0; tab < count; tab++ {
			out = append(out, struct {
				page, tab int
				key       string
			}{page, tab, key})
		}
	}
	return out
}
func checkBounds(t *testing.T, tt *ui.Tester, width, height int) {
	t.Helper()
	for _, n := range tt.AccessAll() {
		if n.Offscreen || n.Bounds.W <= 0 || n.Bounds.H <= 0 {
			continue
		}
		switch n.Role {
		case ui.RoleText, ui.RoleTextField, ui.RoleComboBox, ui.RoleButton, ui.RolePopUpButton, ui.RoleCheckBox:
			if n.Bounds.X < -0.5 || n.Bounds.X+n.Bounds.W > float32(width)+0.5 {
				t.Errorf("horizontal overflow %q: %+v", n.Name, n.Bounds)
			}
		}
	}
	for _, list := range tt.AccessAll() {
		if list.Role != ui.RoleList || list.Offscreen {
			continue
		}
		for _, search := range tt.AccessAll() {
			if search.Role == ui.RoleTextField && !search.Offscreen {
				gap := list.Bounds.Y - search.Bounds.Y - search.Bounds.H
				if gap >= 0 && gap <= 20 && math.Abs(float64(search.Bounds.X-list.Bounds.X)) < 20 {
					if search.Bounds.X != list.Bounds.X || search.Bounds.W != list.Bounds.W {
						t.Errorf("list and search are not aligned: %+v / %+v", search.Bounds, list.Bounds)
					}
				}
			}
		}
	}
}
func TestPageLayouts(t *testing.T) {
	for _, loaded := range []bool{false, true} {
		for _, dark := range []bool{false, true} {
			for _, size := range [][2]int{{1120, 780}, {860, 600}} {
				t.Run(fmt.Sprintf("loaded=%v/dark=%v/%dx%d", loaded, dark, size[0], size[1]), func(t *testing.T) {
					a := New()
					if loaded {
						a = fixture(t)
					}
					a.dark = dark
					tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
					for _, v := range testViews() {
						t.Run(fmt.Sprintf("%s-%d", pages[v.page], v.tab), func(t *testing.T) {
							a.Page = v.page
							a.revision++
							if v.key != "" {
								*a.tab(v.key) = v.tab
							}
							tt.Frame()
							checkBounds(t, tt, size[0], size[1])
							for _, area := range tt.AccessAll() {
								if area.Role == ui.RoleScroll && !area.Offscreen && area.Bounds.H > 100 {
									tt.Scroll(area.Bounds.X+area.Bounds.W/2, area.Bounds.Y+area.Bounds.H/2, 0, 10000)
								}
							}
							tt.Frame()
							checkBounds(t, tt, size[0], size[1])
						})
					}
				})
			}
		}
	}
}
func TestProvidedSaveViews(t *testing.T) {
	path := os.Getenv("FEE_TEST_SAVE")
	if path == "" {
		t.Skip("set FEE_TEST_SAVE to check a local save without modifying it")
	}
	a := New()
	if e := a.Load(path); e != nil {
		t.Fatal(e)
	}
	before := a.Save.Bytes()
	for _, dark := range []bool{false, true} {
		a.dark = dark
		theme := "light"
		if dark {
			theme = "dark"
		}
		for _, size := range [][2]int{{1120, 780}, {860, 600}} {
			tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
			for _, v := range testViews() {
				t.Run(fmt.Sprintf("%s/%dx%d/%s-%d", theme, size[0], size[1], pages[v.page], v.tab), func(t *testing.T) {
					a.Page = v.page
					a.revision++
					if v.key != "" {
						*a.tab(v.key) = v.tab
					}
					tt.Frame()
					checkBounds(t, tt, size[0], size[1])
					if dir := os.Getenv("FEE_SCREENSHOT_DIR"); dir != "" && size[0] == 1120 {
						p := filepath.Join(dir, theme, fmt.Sprintf("%s-%d.png", pages[v.page], v.tab))
						os.MkdirAll(filepath.Dir(p), 0755)
						f, e := os.Create(p)
						if e != nil {
							t.Fatal(e)
						}
						e = png.Encode(f, tt.Image())
						f.Close()
						if e != nil {
							t.Fatal(e)
						}
					}
				})
			}
		}
	}
	if !bytes.Equal(a.Save.Bytes(), before) {
		t.Fatal("viewing save changed its data")
	}
}
func modifier() ui.Modifiers {
	if runtime.GOOS == "darwin" {
		return ui.Cmd
	}
	return ui.Ctrl
}
func TestStagedResourceCopy(t *testing.T) {
	a := fixture(t)
	before := a.Save.Bytes()
	tt := ui.NewTester(a.View, 1120, 780)
	if err := tt.Click(a.T("Money")); err != nil {
		t.Fatal(err)
	}
	tt.Key(modifier(), ui.KeyA)
	tt.Type("12345")
	tt.Key(0, ui.KeyTab)
	v, _ := a.Save.Main()
	if v.Money == 12345 {
		t.Fatal("resource applied before save")
	}
	output := filepath.Join(t.TempDir(), "edited")
	if err := a.SaveCopy(output); err != nil {
		t.Fatal(err)
	}
	s, e := save.Load(output)
	if e != nil {
		t.Fatal(e)
	}
	v, _ = s.Main()
	if v.Money != 12345 {
		t.Fatalf("resource copy failed: %+v", v)
	}
	original, e := os.ReadFile(a.Path)
	if e != nil || !bytes.Equal(original, before) {
		t.Fatal("source was changed")
	}
}
func TestActionFailureRetainsPendingEdits(t *testing.T) {
	a := fixture(t)
	before := a.Save.Bytes()
	a.mainEdit("main/money", func(v *save.MainValues) { v.Money = 123 })
	if a.perform("main", func(s *save.Save) (*save.Save, error) { return nil, fmt.Errorf("failed action") }) {
		t.Fatal("failed action succeeded")
	}
	if !bytes.Equal(before, a.Save.Bytes()) || a.pending["main/money"] == nil {
		t.Fatal("failed action changed the save or lost edits")
	}
}

func TestGUIEditorsSaveAndReload(t *testing.T) {
	cases := []struct {
		name              string
		page, tab         int
		key, label, value string
		check             func(*testing.T, *save.Save)
	}{
		{"roster SP", 2, 0, "roster-tabs", "SkillPoints", "555", func(t *testing.T, s *save.Save) {
			r, e := s.Roster()
			if e != nil || r[0].Values.SkillPoints != 555 {
				t.Fatalf("roster edit: %v %v", r, e)
			}
		}},
		{"quantity", 1, 1, "items", "Amount", "3", func(t *testing.T, s *save.Save) {
			r, e := s.Quantities()
			if e != nil {
				t.Fatal(e)
			}
			for _, v := range r {
				if v.Definition.Category == "ReclassItems" {
					if v.Amount != 3 {
						t.Fatalf("quantity edit: %d", v.Amount)
					}
					return
				}
			}
			t.Fatal("no reclass item")
		}},
		{"support", 4, 0, "", "Points", "5", func(t *testing.T, s *save.Save) {
			r, e := s.Supports()
			if e != nil || r[0].Points != 5 {
				t.Fatalf("support edit: %v %v", r, e)
			}
		}},
		{"bond EXP", 3, 0, "emblem-pages", "Experience", "25", func(t *testing.T, s *save.Save) {
			r, e := s.Emblems()
			if e != nil || r[0].Bonds[0].Experience != 25 {
				t.Fatalf("bond edit: %v %v", r, e)
			}
		}},
	}
	for _, test := range cases {
		t.Run(test.name, func(t *testing.T) {
			a := fixture(t)
			a.Page = test.page
			if test.key != "" {
				*a.tab(test.key) = test.tab
			}
			tt := ui.NewTester(a.View, 1120, 780)
			if e := tt.Click(a.T(test.label)); e != nil {
				t.Fatal(e)
			}
			tt.Key(modifier(), ui.KeyA)
			tt.Type(test.value)
			tt.Key(0, ui.KeyTab)
			output := filepath.Join(t.TempDir(), "copy")
			if e := a.SaveCopy(output); e != nil {
				t.Fatal(e)
			}
			s, e := save.Load(output)
			if e != nil {
				t.Fatal(e)
			}
			test.check(t, s)
		})
	}
}

func TestExportSnapshotIncludesPendingEdits(t *testing.T) {
	a := fixture(t)
	before := a.Save.Bytes()
	a.stage("roster/0/values", func(s *save.Save) (*save.Save, error) {
		rows, e := s.Roster()
		if e != nil {
			return nil, e
		}
		v := rows[0].Values
		v.SkillPoints = 1234
		return s.WithRosterValues(0, v)
	})
	snapshot, e := a.snapshot("roster/0")
	if e != nil {
		t.Fatal(e)
	}
	b, e := snapshot.ExportRosterCharacter(0)
	if e != nil {
		t.Fatal(e)
	}
	transfer, e := save.ReadRosterTransfer(b)
	if e != nil || transfer.Values.SkillPoints != 1234 {
		t.Fatalf("pending edits missing from export: %v", e)
	}
	if !bytes.Equal(before, a.Save.Bytes()) || a.pending["roster/0/values"] == nil {
		t.Fatal("export snapshot modified the loaded editor")
	}
}
func TestLanguagesAndFilters(t *testing.T) {
	a := fixture(t)
	*a.tab("achievement-category") = 2
	*a.tab("achievement-status") = 1
	for _, language := range []string{"en", "zh-Hans", "zh-Hant", "ja", "ko", "de", "fr", "es", "it"} {
		if e := a.SetLanguage(language); e != nil {
			t.Fatal(e)
		}
		tt := ui.NewTester(a.View, 860, 600)
		for _, v := range testViews() {
			a.Page = v.page
			if v.key != "" {
				*a.tab(v.key) = v.tab
			}
			a.revision++
			tt.Frame()
			checkBounds(t, tt, 860, 600)
		}
		if *a.tab("achievement-category") != 2 || *a.tab("achievement-status") != 1 {
			t.Fatal("language change reset the filter")
		}
	}
}

func TestUnlockedAmiiboIsChecked(t *testing.T) {
	a := fixture(t)
	rewards, e := a.Save.Amiibo()
	if e != nil {
		t.Fatal(e)
	}
	reward := rewards[0]
	if !a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAmiibo([]string{reward.Definition.Key}) }) {
		t.Fatal(a.Status)
	}
	a.Page = 8
	tt := ui.NewTester(a.View, 1120, 780)
	node, ok := tt.AccessByRole(ui.RoleCheckBox, reward.Definition.Names.Name(a.Language))
	if !ok || !node.Checked || !node.Disabled {
		t.Fatalf("unlocked reward is not checked and disabled: %+v", node)
	}
}
func TestEmblemChoiceSurvivesEditing(t *testing.T) {
	a := fixture(t)
	emblems, e := a.Save.Emblems()
	if e != nil || len(emblems) < 2 {
		t.Fatal(e)
	}
	a.Page = 3
	selected := fmt.Sprint(emblems[1].InstanceId)
	a.choices["emblem-choice"] = &selected
	tt := ui.NewTester(a.View, 1120, 780)
	a.apply(func(s *save.Save) (*save.Save, error) { return s.WithEmblemDirtiness(nil, 0) })
	tt.Frame()
	if *a.choices["emblem-choice"] != fmt.Sprint(emblems[1].InstanceId) {
		t.Fatal("editing reset the selected Emblem")
	}
}
