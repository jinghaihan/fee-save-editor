package app

import (
	"bytes"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"os"
	"path/filepath"
	"testing"
)

func TestAmiiboDirectUnlockAndGroupUnlock(t *testing.T) {
	a := fixture(t)
	original, err := os.ReadFile(a.Path)
	if err != nil {
		t.Fatal(err)
	}
	rewards, err := a.Save.Amiibo()
	if err != nil {
		t.Fatal(err)
	}
	var reward save.AmiiboReward
	found := false
	for _, r := range rewards {
		if !r.Unlocked {
			reward = r
			found = true
			break
		}
	}
	if !found {
		t.Fatal("fixture has no locked reward")
	}
	expected, err := a.Save.UnlockAmiibo([]string{reward.Definition.Key})
	if err != nil {
		t.Fatal(err)
	}
	a.Page = 0
	tt := ui.NewTester(a.View, 1120, 780)
	if err := tt.Click(reward.Definition.Names.Name(a.Language)); err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(expected.Bytes(), a.Save.Bytes()) {
		t.Fatal("checkbox did not unlock only its own reward")
	}
	group, ok := tt.AccessByRole(ui.RoleGroup, a.T("Amiibo"+reward.Definition.Group))
	if !ok {
		t.Fatal("missing reward group")
	}
	keys := []string{}
	for _, r := range rewards {
		if r.Definition.Group == reward.Definition.Group {
			keys = append(keys, r.Definition.Key)
		}
	}
	expected, err = expected.UnlockAmiibo(keys)
	if err != nil {
		t.Fatal(err)
	}
	found = false
	for _, node := range tt.AccessAll() {
		if node.Role == ui.RoleButton && node.Name == a.T("UnlockAll") && node.Bounds.Y >= group.Bounds.Y && node.Bounds.Y+node.Bounds.H <= group.Bounds.Y+group.Bounds.H {
			tt.ClickAt(node.Bounds.X+node.Bounds.W/2, node.Bounds.Y+node.Bounds.H/2)
			found = true
			break
		}
	}
	if !found {
		t.Fatal("missing group unlock button")
	}
	if !bytes.Equal(expected.Bytes(), a.Save.Bytes()) {
		t.Fatal("group unlock changed the wrong rewards")
	}
	path := filepath.Join(t.TempDir(), "copy")
	if err := a.SaveCopy(path); err != nil {
		t.Fatal(err)
	}
	reloaded, err := save.Load(path)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(expected.Bytes(), reloaded.Bytes()) {
		t.Fatal("unlock edits did not round trip")
	}
	after, err := os.ReadFile(a.Path)
	if err != nil || !bytes.Equal(original, after) {
		t.Fatal("unlocking wrote to the source")
	}
}

func TestHomeAmiiboCardsAndMusicUnlock(t *testing.T) {
	a := fixture(t)
	tt := ui.NewTester(a.View, 1120, 780)
	for _, name := range pages {
		if name == "Amiibo" {
			t.Fatal("amiibo still has a navigation page")
		}
	}
	donation, ok := tt.AccessByRole(ui.RoleGroup, a.T("Donations"))
	if !ok {
		t.Fatal("missing donation card")
	}
	outfits, ok := tt.AccessByRole(ui.RoleGroup, a.T("AmiiboOutfits"))
	if !ok {
		t.Fatal("missing outfit card")
	}
	music, ok := tt.AccessByRole(ui.RoleGroup, a.T("AmiiboMusic"))
	if !ok {
		t.Fatal("missing music card")
	}
	if donation.Bounds.X != outfits.Bounds.X || outfits.Bounds.X != music.Bounds.X || outfits.Bounds.Y < donation.Bounds.Y+donation.Bounds.H || music.Bounds.Y < outfits.Bounds.Y+outfits.Bounds.H {
		t.Fatal("amiibo cards are not stacked beneath donations")
	}
	rewards, err := a.Save.Amiibo()
	if err != nil {
		t.Fatal(err)
	}
	keys := []string{}
	for _, r := range rewards {
		if r.Definition.Group == "Music" {
			keys = append(keys, r.Definition.Key)
		}
	}
	expected, err := a.Save.UnlockAmiibo(keys)
	if err != nil {
		t.Fatal(err)
	}
	tt.Scroll(1000, 500, 0, 10000)
	music, _ = tt.AccessByRole(ui.RoleGroup, a.T("AmiiboMusic"))
	found := false
	for _, node := range tt.AccessAll() {
		if node.Role == ui.RoleButton && node.Name == a.T("UnlockAll") && node.Bounds.Y >= music.Bounds.Y && node.Bounds.Y+node.Bounds.H <= music.Bounds.Y+music.Bounds.H {
			if node.Offscreen {
				t.Fatal("music action cannot be reached by scrolling")
			}
			tt.ClickAt(node.Bounds.X+node.Bounds.W/2, node.Bounds.Y+node.Bounds.H/2)
			found = true
			break
		}
	}
	if !found || !bytes.Equal(expected.Bytes(), a.Save.Bytes()) {
		t.Fatal("music unlock action did not apply correctly")
	}
}
