package app

import (
	"bytes"
	"fmt"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"slices"
	"testing"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func chapterFixture(t *testing.T) *App {
	t.Helper()
	a := New()
	b, err := os.ReadFile("../save/testdata/chapter-records.bin")
	if err != nil {
		t.Fatal(err)
	}
	p := filepath.Join(t.TempDir(), "source")
	if err := os.WriteFile(p, b, 0600); err != nil {
		t.Fatal(err)
	}
	if err := a.Load(p); err != nil {
		t.Fatal(err)
	}
	a.Page = slices.Index(pages, "ChapterRecords")
	return a
}
func chapterRows(t *testing.T, a *App) []save.ChapterRecord {
	t.Helper()
	rows, err := a.Save.ChapterRecords()
	if err != nil {
		t.Fatal(err)
	}
	return rows
}
func enterChapterValue(t *testing.T, tt *ui.Tester, label, text string) {
	t.Helper()
	if err := tt.Click(label); err != nil {
		t.Fatal(err)
	}
	tt.Key(modifier(), ui.KeyA)
	tt.Type(text)
}

func TestChapterRecordEditingAndMoves(t *testing.T) {
	a := chapterFixture(t)
	before := a.Save.Bytes()
	original := chapterRows(t, a)
	tt := ui.NewTester(a.View, 1120, 780)
	// Typing updates the in-memory save on blur; there is no Apply button.
	enterChapterValue(t, tt, a.T("ClearTime"), "01:02:03.125")
	tt.Key(0, ui.KeyTab)
	if got := chapterRows(t, a)[0].ClearTime; got != 3723.125 {
		t.Fatalf("blur lost time edit: %v status=%s", got, a.Status)
	}
	enterChapterValue(t, tt, a.T("ClearTurns"), "9")
	// Reordering while a field is still focused must retain its pending value.
	if err := tt.Click(a.T("MoveDown")); err != nil {
		t.Fatal(err)
	}
	got := chapterRows(t, a)
	if got[1].ChapterID != original[0].ChapterID || got[1].ClearTurn != 9 || got[1].ClearTime != 3723.125 {
		t.Fatalf("move lost pending fields: %+v %s", got, a.Status)
	}
	if table := a.table("chapter-records"); table.Rows[table.Selected].ID != original[0].ChapterID {
		t.Fatal("move changed selection")
	}
	// Verify that the details did not keep a stale draft after the move.
	enterChapterValue(t, tt, a.T("ClearTime"), "00:05:06.5")
	tt.Key(0, ui.KeyTab)
	if chapterRows(t, a)[1].ClearTurn != 9 {
		t.Fatal("stale detail reset turn count")
	}
	enterChapterValue(t, tt, a.T("Position"), "4")
	if err := tt.Click(a.T("MoveToPosition")); err != nil {
		t.Fatal(err)
	}
	if chapterRows(t, a)[3].ChapterID != original[0].ChapterID {
		t.Fatal("destination input did not retain its value")
	}
	if err := tt.Click(a.T("MoveUp")); err != nil {
		t.Fatal(err)
	}
	if chapterRows(t, a)[2].ChapterID != original[0].ChapterID {
		t.Fatal("upward move failed")
	}
	// Native searchable selectors edit historical IDs, not current roster data.
	chooseOption(t, tt, a.T("MVP"), catalog.RecordPersonName("パネトネ", "en"))
	chooseOption(t, tt, a.T("RecordedClass"), catalog.RecordClassName("ウォーリアー", "en"))
	chooseOption(t, tt, a.T("RecordedEmblem"), catalog.RecordEmblemName("ディミトリ", "en"))
	output := filepath.Join(t.TempDir(), "copy")
	if err := a.SaveCopy(output); err != nil {
		t.Fatal(err)
	}
	reloaded, err := save.Load(output)
	if err != nil {
		t.Fatal(err)
	}
	rows, err := reloaded.ChapterRecords()
	if err != nil {
		t.Fatal(err)
	}
	r := rows[2]
	if r.ChapterID != original[0].ChapterID || r.Person != "パネトネ" || r.Class != "ウォーリアー" || r.Emblem != "ディミトリ" || r.ClearTurn != 9 || r.ClearTime != 306.5 {
		t.Fatalf("GUI fields lost on save: %+v", r)
	}
	source, err := os.ReadFile(a.Path)
	if err != nil || !bytes.Equal(source, before) {
		t.Fatal("GUI changed source file")
	}
}

func TestChapterRecordInvalidTime(t *testing.T) {
	a := chapterFixture(t)
	before := a.Save.Bytes()
	tt := ui.NewTester(a.View, 1120, 780)
	enterChapterValue(t, tt, a.T("ClearTime"), "00:99:00")
	tt.Key(0, ui.KeyTab)
	if !bytes.Equal(before, a.Save.Bytes()) || a.Status == "" {
		t.Fatal("invalid time was applied")
	}
	if err := a.SaveCopy(filepath.Join(t.TempDir(), "bad")); err == nil {
		t.Fatal("invalid pending time saved")
	}
	enterChapterValue(t, tt, a.T("ClearTime"), "00:00:12.5")
	tt.Key(0, ui.KeyTab)
	if chapterRows(t, a)[0].ClearTime != 12.5 || a.Status != "" {
		t.Fatal("corrected time failed")
	}
}

func TestChapterRecordDuration(t *testing.T) {
	for _, value := range []float32{0, 0.125, 0.0000001, 42.1, 60, 3600, 5341.7890625, 1e20, math.MaxFloat32} {
		text := recordDuration(value)
		got, err := parseRecordDuration(text)
		if err != nil || math.Abs(float64(got)-float64(value)) > 0.00051 {
			t.Fatalf("time %v -> %s -> %v: %v", value, text, got, err)
		}
	}
	for _, text := range []string{"", "1", "00:60:00", "00:00:60", "-1:00:00", "00:00:NaN", "00:00:+1", "1e2:00:00", "0:0:1.2.3"} {
		if _, err := parseRecordDuration(text); err == nil {
			t.Fatalf("accepted %q", text)
		}
	}
}

func TestChapterRecordLayouts(t *testing.T) {
	for _, loaded := range []bool{false, true} {
		for _, dark := range []bool{false, true} {
			for _, size := range [][2]int{{1120, 780}, {860, 600}} {
				t.Run(fmt.Sprintf("loaded=%v/dark=%v/%dx%d", loaded, dark, size[0], size[1]), func(t *testing.T) {
					a := New()
					if loaded {
						a = chapterFixture(t)
					}
					a.Page = slices.Index(pages, "ChapterRecords")
					a.dark = dark
					tt := ui.NewTesterWith(a.View, size[0], size[1], ui.TesterOptions{Dark: dark})
					checkBounds(t, tt, size[0], size[1])
					for _, label := range []string{"MVP", "RecordedClass", "RecordedEmblem", "ClearTime", "MoveUp", "MoveDown", "MoveToPosition"} {
						n, ok := tt.Access(a.T(label))
						if !ok {
							t.Fatalf("missing %s", label)
						}
						if !loaded && !n.Disabled {
							t.Fatalf("unloaded %s enabled", label)
						}
					}
				})
			}
		}
	}
}

func TestProvidedChapterRecordScreenshots(t *testing.T) {
	path := os.Getenv("FEE_TEST_SAVE")
	dir := os.Getenv("FEE_CHAPTER_SCREENSHOT_DIR")
	if path == "" || dir == "" {
		t.Skip("set FEE_TEST_SAVE and FEE_CHAPTER_SCREENSHOT_DIR")
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
		a.Language = "en"
		a.dark = dark
		a.Page = slices.Index(pages, "ChapterRecords")
		for i, r := range chapterRows(t, a) {
			if r.ChapterID == "CID_M014" {
				a.table("chapter-records").Selected = i
			}
		}
		tt := ui.NewTesterWith(a.View, 1120, 780, ui.TesterOptions{Dark: dark})
		checkBounds(t, tt, 1120, 780)
		tt.Frame()
		selectedName := a.table("chapter-records").Rows[a.table("chapter-records").Selected].Name
		if n, ok := tt.Access(selectedName); !ok || n.Offscreen {
			t.Fatalf("selected record is not visible: %s", selectedName)
		}
		theme := "light"
		if dark {
			theme = "dark"
		}
		if err := os.MkdirAll(dir, 0755); err != nil {
			t.Fatal(err)
		}
		f, err := os.Create(filepath.Join(dir, "chapter-records-"+theme+".png"))
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
		t.Fatal("screenshot changed source")
	}
}
