package app

import (
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func (a *App) personName(id string) string {
	if p := catalog.Person(catalog.Hash(id)); p != nil {
		return p.Name(a.Language)
	}
	return id
}
func (a *App) supportPage(c *ui.Context) {
	supports, ok := data(c, a, "supports", a.Save.Supports)
	if !ok {
		return
	}
	rows := []row{}
	complete, known := 0, 0
	for _, r := range supports {
		name := r.Key
		if p := catalog.SupportPair(r.Key); p != nil {
			name = a.personName(p.FirstPersonId) + " — " + a.personName(p.SecondPersonId)
			known++
			cap, _ := a.Save.MaximumSupportRank(r.Key)
			if r.Rank >= cap {
				complete++
			}
		}
		rows = append(rows, row{r.Key, name, []string{a.T("None"), "C", "B", "A", "A+"}[r.Rank]})
	}
	a.split(c, "supports", "SupportList", rows, listHeader{summary: fmt.Sprintf("%d/%d", complete, known)}, func() {
		a.button(c, "MaxAllSupports", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeSupports("") }) })
	}, func(key string) {
		var current save.CharacterSupport
		for _, r := range supports {
			if r.Key == key {
				current = r
			}
		}
		pair := catalog.SupportPair(key)
		ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Disabled(pair == nil).Children(func() {
			cap := 3
			if a.Save != nil {
				cap, _ = a.Save.MaximumSupportRank(key)
			}
			type values struct {
				Rank   string
				Points float64
			}
			d := draft(a, "supports/"+key, func() values { return values{fmt.Sprint(current.Rank), float64(current.Points)} })
			options := []option{}
			for i := 0; i <= cap; i++ {
				options = append(options, option{fmt.Sprint(i), []string{a.T("None"), "C", "B", "A", "A+"}[i]})
			}
			stage := func() {
				rank := 0
				fmt.Sscan(d.Rank, &rank)
				points := int(d.Points)
				a.stage("supports/"+key, func(s *save.Save) (*save.Save, error) { return s.WithSupport(key, rank, &points) })
			}
			if a.combo(c, "Rank", "support-rank", &d.Rank, options) {
				rank := 0
				fmt.Sscan(d.Rank, &rank)
				if pair != nil {
					d.Points = float64(pair.PointsForRank(rank))
				}
				stage()
			}
			rank := 0
			fmt.Sscan(d.Rank, &rank)
			maximum := 99
			if pair != nil {
				maximum = max(current.Points, pair.MaximumPoints(rank))
			}
			if a.formNumber(c, "Points", "support-points", &d.Points, float64(min(0, current.Points)), float64(maximum)) {
				if pair != nil && int(d.Points) < 99 {
					for i := 3; i >= 0; i-- {
						if int(d.Points) >= pair.PointsForRank(i) {
							d.Rank = fmt.Sprint(i)
							break
						}
					}
				}
				stage()
			}
			a.button(c, "MaxSupport", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeSupports(key) }) })
			a.applyButton(c, "supports")
		})
	})
}
