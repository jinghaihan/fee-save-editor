package app

import (
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func (a *App) View(c *ui.Context) {
	c.SetTheme(a.currentTheme())
	c = c.Key(a.revision)
	root := ui.Column(c).Fill().Background(c.Theme().Background)
	root.Children(func() {
		labels := make([]string, len(pages))
		for i, page := range pages {
			labels[i] = a.T(page)
		}
		ui.ScrollHorizontal(c).Children(func() {
			old := a.Page
			if ui.Tabs(c, &a.Page, labels...).Changed() {
				if a.Save != nil && a.ApplyPending("") != nil {
					a.Page = old
				}
			}
		})
		ui.Column(c.Key(pages[a.Page])).Grow(1).Basis(0).MinHeight(0).Padding(24).Gap(12).Disabled(a.Save == nil || a.Save.Kind == save.Global && pages[a.Page] != "Main" && pages[a.Page] != "Inspector").Children(func() {
			switch pages[a.Page] {
			case "Main":
				a.mainPage(c)
			case "Items":
				a.itemsPage(c)
			case "Roster":
				a.rosterPage(c)
			case "Emblems":
				a.emblemsPage(c)
			case "Support":
				a.supportPage(c)
			case "Minigames":
				a.minigamesPage(c)
			case "Achievements":
				a.achievementsPage(c)
			case "Animals":
				a.animalsPage(c)
			case "Amiibo":
				a.amiiboPage(c)
			case "Inspector":
				a.inspectorPage(c)
			}
		})
		if a.Status != "" {
			ui.Text(c, a.Status).Padding(0, 24, 16)
		}
	})
	if files := root.DroppedFiles(); len(files) > 0 {
		a.openPath(files[0])
	}
	if ui.AlertDialog(c, &a.confirm, a.confirmLabel, "", a.T("Cancel"), a.T("Confirm")) == 1 && a.confirmAction != nil {
		fn := a.confirmAction
		a.confirmAction = nil
		fn()
	}
}
