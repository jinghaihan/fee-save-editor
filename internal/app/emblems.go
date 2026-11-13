package app

import (
	"fmt"
	"strconv"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func (a *App) emblemName(id string) string {
	if d := catalog.Emblem(id); d != nil {
		return d.Name(a.Language)
	}
	return id
}
func (a *App) emblemsPage(c *ui.Context) {
	if a.tabsView(c, "emblem-pages", "Bonds", "BondRings") == 1 {
		a.bondRingsPage(c)
		return
	}
	emblems, ok := data(c, a, "emblems", a.Save.Emblems)
	if !ok {
		return
	}
	conditions, _ := read(a, "emblem-conditions", a.Save.EmblemConditions)
	selected := a.choices["emblem-choice"]
	if selected == nil {
		selected = new(string)
		a.choices["emblem-choice"] = selected
	}
	found := false
	for _, e := range emblems {
		if *selected == fmt.Sprint(e.InstanceId) {
			found = true
		}
	}
	if !found && len(emblems) > 0 {
		*selected = fmt.Sprint(emblems[0].InstanceId)
	}
	choices := []option{}
	var emblem save.SavedEmblem
	for _, e := range emblems {
		choices = append(choices, option{fmt.Sprint(e.InstanceId), a.emblemName(e.EmblemId)})
		if fmt.Sprint(e.InstanceId) == *selected {
			emblem = e
		}
	}
	rows := []row{}
	for _, b := range emblem.Bonds {
		rows = append(rows, row{b.PersonId, a.personName(b.PersonId), fmt.Sprintf("Lv. %d", b.Level)})
	}
	a.split(c, "bonds", "CharacterList", rows, func() {
		if a.combo(c, "Emblem", "emblem-choice", selected, choices) {
			a.ApplyPending("bonds")
		}
	}, func() {
		a.button(c, "MaxAllBonds", func() {
			a.perform("bonds", func(s *save.Save) (*save.Save, error) { return s.MaximizeEmblemBonds(emblem.InstanceId, "") })
		})
		a.button(c, "CleanAll", func() {
			a.perform("bonds", func(s *save.Save) (*save.Save, error) { return s.WithEmblemDirtiness(nil, 0) })
		})
		if ui.Button(c, a.T("RemoveEmblem")).Height(42).Disabled(a.Save == nil || !a.Save.CanRemoveEmblem(emblem.InstanceId)).Clicked() {
			a.ask("ConfirmRemoveEmblem", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.WithoutEmblem(emblem.InstanceId) }) })
		}
		a.addEmblemButton(c)
	}, func(person string) {
		var bond save.EmblemBond
		for _, b := range emblem.Bonds {
			if b.PersonId == person {
				bond = b
			}
		}
		key := fmt.Sprintf("bonds/%d/%s", emblem.InstanceId, person)
		a.columns(c, func() {
			ui.Text(c, a.T("Bonds")).Bold()
			type values struct {
				Level string
				Exp   float64
			}
			d := draft(a, key, func() values { return values{fmt.Sprint(bond.Level), float64(bond.Experience)} })
			options := []option{}
			for _, level := range save.SelectableBondLevels(emblem, person) {
				options = append(options, option{fmt.Sprint(level), fmt.Sprint(level)})
			}
			stage := func() {
				level, _ := strconv.Atoi(d.Level)
				experience := int(d.Exp)
				a.stage(key, func(s *save.Save) (*save.Save, error) {
					return s.WithEmblemBond(emblem.InstanceId, person, level, &experience)
				})
			}
			if a.combo(c, "BondLevel", key+"/level", &d.Level, options) {
				level, _ := strconv.Atoi(d.Level)
				d.Exp = float64(save.BondExperienceForLevel(emblem, person, level))
				stage()
			}
			cap := 208
			if save.MaximumBondLevel(emblem, person) == 21 {
				cap = 209
			}
			changed := false
			ui.Column(c).Disabled(emblem.EmblemId == "GID_リュール").Children(func() { changed = a.formNumber(c, "Experience", key+"/experience", &d.Exp, 0, float64(cap)) })
			if changed {
				if int(d.Exp) <= 208 {
					d.Level = fmt.Sprint(save.BondLevelForExperience(int(d.Exp)))
				} else {
					d.Level = "21"
				}
				stage()
			}
			a.button(c, "MaxBond", func() {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeEmblemBonds(emblem.InstanceId, person) })
			})
			a.applyButton(c, key)
		}, func() {
			ui.Text(c, a.T("RingCare")).Bold()
			var condition *save.EmblemCondition
			for _, r := range conditions {
				if r.BondHolderId == emblem.InstanceId {
					v := r
					condition = &v
					break
				}
			}
			ui.Column(c).Gap(16).Disabled(condition == nil).Children(func() {
				dirt := 0
				if condition != nil {
					dirt = condition.Dirtiness
				}
				a.numberEdit(c, "Dirtiness", fmt.Sprintf("bonds/condition/%d", emblem.InstanceId), dirt, 0, 255, func(s *save.Save, n int) (*save.Save, error) {
					id := condition.InstanceId
					return s.WithEmblemDirtiness(&id, n)
				})
				a.button(c, "Clean", func() {
					if condition != nil {
						id := condition.InstanceId
						a.apply(func(s *save.Save) (*save.Save, error) { return s.WithEmblemDirtiness(&id, 0) })
					}
				})
				a.applyButton(c, "bonds/condition")
			})
		})
	})
}
func (a *App) addEmblemButton(c *ui.Context) {
	missing, _ := read(a, "missing-emblems", a.Save.MissingEmblems)
	anchor := ui.Button(c, a.T("AddEmblem")).Height(42).Disabled(len(missing) == 0)
	open := ui.Local(anchor, "open", func() bool { return false })
	if anchor.Clicked() {
		*open = !*open
	}
	ui.Popover(c, anchor, open, func() {
		ui.Column(c).Width(280).Gap(16).Children(func() {
			ui.Text(c, a.T("AddEmblem")).Bold()
			options := []option{}
			for _, d := range missing {
				options = append(options, option{d.Id, d.Name(a.Language)})
			}
			selected := draft(a, "add-emblem-choice", func() string {
				if len(options) > 0 {
					return options[0].ID
				}
				return ""
			})
			a.combo(c, "Emblem", "add-emblem", selected, options)
			a.button(c, "Add", func() {
				id := *selected
				if a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAddedEmblem(id) }) {
					*open = false
				}
			})
		})
	})
}
func (a *App) bondRingsPage(c *ui.Context) {
	rings, ok := data(c, a, "bond-rings", a.Save.BondRings)
	if !ok {
		return
	}
	rows := []row{}
	for _, r := range rings {
		name := fmt.Sprintf("%s (0x%08X)", a.T("UnknownRing"), r.RingHash)
		if d := catalog.BondRing(r.RingHash); d != nil {
			name = d.Name(a.Language) + " · " + d.RankName()
		}
		rows = append(rows, row{fmt.Sprint(r.InstanceId), name, fmt.Sprint(r.StockCount)})
	}
	a.split(c, "rings", "BondRings", rows, nil, func() {
		a.button(c, "FillSBondRings", func() {
			a.perform("rings", func(s *save.Save) (*save.Save, error) { return s.WithMissingSBondRings() })
		})
	}, func(id string) {
		var ring save.BondRing
		for _, r := range rings {
			if fmt.Sprint(r.InstanceId) == id {
				ring = r
			}
		}
		ui.Column(c).Width(340).Gap(16).Children(func() {
			ui.Text(c, a.T("BondRings")).Bold()
			maximum := 99
			if a.Save != nil {
				maximum, _ = a.Save.MaximumBondRingStock(ring.InstanceId)
			}
			a.numberEdit(c, "Amount", "rings/"+id, ring.StockCount, func() int {
				if ring.OwnerIndex != nil {
					return 1
				}
				return 0
			}(), maximum, func(s *save.Save, n int) (*save.Save, error) { return s.WithBondRingStock(ring.InstanceId, n) })
			owner := a.T("None")
			if ring.OwnerIndex != nil {
				roster, _ := a.Save.Roster()
				if *ring.OwnerIndex < len(roster) {
					owner = a.characterName(roster[*ring.OwnerIndex])
				}
			}
			a.staticField(c, "EquippedBy", owner)
			a.applyButton(c, "rings")
			def := catalog.BondRing(ring.RingHash)
			if def == nil {
				return
			}
			if result := catalog.MeldResult(*def); result != nil {
				for _, rule := range catalog.MeldingRules {
					if rule.SourceRank == def.Rank {
						text := a.T("BondRingMeldCost")
						text = replaceFormat(text, def.Name(a.Language)+" "+def.RankName(), fmt.Sprint(rule.RequiredRings), fmt.Sprint(rule.BondFragments))
						ui.Text(c, text)
						if ui.Button(c, replaceFormat(a.T("MeldBondRing"), result.RankName())).Height(42).Disabled(ring.StockCount < rule.RequiredRings).Clicked() {
							a.perform("rings", func(s *save.Save) (*save.Save, error) { return s.WithMeldedBondRing(ring.InstanceId) })
						}
					}
				}
			}
			ui.Text(c, a.T("Stats")).Bold()
			ui.Grid(c).Columns(3).Gap(12).Children(func() {
				for i, n := range def.StatBonuses {
					if n != 0 {
						ui.Textf(c, "%s %s", a.T(save.StatNames[i]), signed(n))
					}
				}
			})
			if len(def.Skills) > 0 {
				ui.Text(c, a.T("Skills")).Bold()
				for _, skill := range def.Skills {
					ui.Text(c, skill.Name(a.Language)).Bold()
					ui.Text(c, skill.Descriptions.Name(a.Language))
				}
			}
		})
	})
}
