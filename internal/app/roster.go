package app

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"

	"github.com/egoist/mygo"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func (a *App) characterName(r save.RosterCharacter) string {
	if r.Progress.CustomName != nil {
		return *r.Progress.CustomName
	}
	if d := catalog.Person(r.PersonHash); d != nil {
		return d.Name(a.Language)
	}
	return fmt.Sprintf("%s (0x%08X)", a.T("UnknownCharacter"), r.PersonHash)
}
func (a *App) skillName(h uint32) string {
	if d := catalog.Skill(h); d != nil {
		return d.Name(a.Language)
	}
	return fmt.Sprintf("0x%08X", h)
}
func (a *App) rosterPage(c *ui.Context) {
	roster, ok := data(c, a, "roster", a.Save.Roster)
	if !ok {
		return
	}
	rows := []row{}
	for _, r := range roster {
		if r.Force != save.Enemy && r.Force != save.Temporary {
			rows = append(rows, row{strconv.Itoa(r.Index), a.characterName(r), fmt.Sprintf("Lv. %d", r.Values.Level)})
		}
	}
	a.split(c, "roster", "CharacterList", rows, nil, func() {
		a.button(c, "MaximizeAllStats", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeRosterStats(nil) }) })
		id := a.selectedID("roster")
		index, _ := strconv.Atoi(id)
		ui.Row(c).Gap(12).Children(func() {
			for _, export := range []bool{true, false} {
				key := "Import"
				if export {
					key = "Export"
				}
				if ui.Button(c, a.T(key)).Height(42).Grow(1).Basis(0).Disabled(id == "").Clicked() {
					a.transferCharacter(index, export)
				}
			}
		})
		ui.Row(c).Gap(12).Children(func() {
			a.addCharacterButton(c)
			if ui.Button(c, a.T("Delete")).Height(42).Grow(1).Basis(0).Disabled(id == "" || a.Save == nil || !a.Save.CanDeleteRosterCharacter(index)).Clicked() {
				a.ask("ConfirmDeleteCharacter", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.WithoutRosterCharacter(index) }) })
			}
		})
	}, func(id string) {
		var current save.RosterCharacter
		index, _ := strconv.Atoi(id)
		for _, r := range roster {
			if r.Index == index {
				current = r
			}
		}
		scope := "roster/" + id
		ui.Column(c.Key(id)).Gap(24).Children(func() {
			tab := a.tabsView(c, "roster-tabs", "General", "Stats", "Items", "Skills", "Proficiencies", "Equipment")
			switch tab {
			case 0:
				a.rosterGeneral(c, scope, current)
			case 1:
				a.rosterStats(c, scope, current)
			case 2:
				a.rosterItems(c, scope, current)
			case 3:
				a.rosterSkills(c, scope, current)
			case 4:
				a.rosterProficiencies(c, scope, current)
			case 5:
				a.rosterEquipment(c, scope, current)
			}
		})
	})
}
func (a *App) selectedID(key string) string {
	s := a.table(key)
	if s.Selected >= 0 && s.Selected < len(s.Rows) {
		return s.Rows[s.Selected].ID
	}
	return ""
}
func (a *App) addCharacterButton(c *ui.Context) {
	missing, _ := read(a, "missing-characters", a.Save.MissingRosterCharacters)
	anchor := ui.Button(c, a.T("Add")).Height(42).Grow(1).Basis(0).Disabled(len(missing) == 0)
	open := ui.Local(anchor, "open", func() bool { return false })
	if anchor.Clicked() {
		*open = !*open
	}
	ui.Popover(c, anchor, open, func() {
		ui.Column(c).Width(280).Gap(16).Children(func() {
			ui.Text(c, a.T("AddCharacter")).Bold()
			options := []option{}
			for _, p := range missing {
				options = append(options, option{p.Id, p.Name(a.Language)})
			}
			selected := draft(a, "add-character-choice", func() string {
				if len(options) > 0 {
					return options[0].ID
				}
				return ""
			})
			a.combo(c, "CharacterList", "add-character", selected, options)
			a.button(c, "Add", func() {
				id := *selected
				if a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAddedRosterCharacter(id) }) {
					*open = false
				}
			})
		})
	})
}
func (a *App) rosterGeneral(c *ui.Context, key string, r save.RosterCharacter) {
	a.columns(c, func() {
		a.staticField(c, "Status", a.T("Roster"+r.Availability()))
		if ui.Button(c, a.T("RestoreCharacter")).Height(42).Disabled(a.Save == nil || !a.Save.CanRestoreRosterCharacter(r.Index)).Clicked() {
			a.apply(func(s *save.Save) (*save.Save, error) { return s.RestoreRosterCharacter(r.Index) })
		}
		forces := []option{{"3", a.T("ForceAbsent")}, {"4", a.T("ForceDead")}, {"5", a.T("ForceLost")}}
		force := draft(a, key+"/force-choice", func() string { return fmt.Sprint(r.Force) })
		a.combo(c, "Force", key+"/force", force, forces)
		if ui.Button(c, a.T("MoveCharacter")).Height(42).Disabled(a.Save == nil || !a.Save.CanMoveRosterCharacter(r.Index)).Clicked() {
			n, _ := strconv.Atoi(*force)
			a.apply(func(s *save.Save) (*save.Save, error) { return s.WithRosterForce(r.Index, save.UnitForce(n)) })
		}
		classes, _ := read(a, fmt.Sprintf("classes-%d", r.Index), func() ([]catalog.ClassDefinition, error) { return a.Save.RosterClasses(r.Index) })
		options := []option{}
		for _, d := range classes {
			options = append(options, option{d.Id, d.Name(a.Language)})
		}
		sort.SliceStable(options, func(i, j int) bool { return options[i].Name < options[j].Name })
		selected := draft(a, key+"/class-choice", func() string {
			if d := catalog.Class(r.ClassHash); d != nil {
				return d.Id
			}
			return ""
		})
		changed := a.combo(c, "Class", key+"/class", selected, options)
		job := catalog.Class(catalog.Hash(*selected))
		variants := []option{}
		if job != nil {
			for _, mask := range job.WeaponVariants() {
				variants = append(variants, option{fmt.Sprint(mask), a.weaponNames(mask)})
			}
		}
		weapons := draft(a, key+"/weapons-choice", func() string { return fmt.Sprint(r.Progress.SelectedWeapons) })
		if changed && len(variants) > 0 {
			*weapons = variants[0].ID
		}
		a.combo(c, "Weapons", key+"/weapons", weapons, variants)
		a.button(c, "ChangeClass", func() {
			mask, _ := strconv.ParseUint(*weapons, 10, 32)
			class := *selected
			a.perform(key, func(s *save.Save) (*save.Save, error) { return s.WithRosterClass(r.Index, class, uint32(mask)) })
		})
	}, func() {
		type values struct{ Level, Experience, SP, Internal float64 }
		d := draft(a, key+"/values", func() values {
			return values{float64(r.Values.Level), float64(r.Values.Experience), float64(r.Values.SkillPoints), float64(r.Progress.InternalLevel)}
		})
		cap := 255
		if job := catalog.Class(r.ClassHash); job != nil {
			cap = job.MaxLevel
		}
		stage := func() {
			a.stage(key+"/values", func(s *save.Save) (*save.Save, error) {
				edited, err := s.WithRosterValues(r.Index, save.RosterValue{Level: int(d.Level), Experience: int(d.Experience), SkillPoints: int(d.SP)})
				if err != nil {
					return nil, err
				}
				rows, err := edited.Roster()
				if err != nil {
					return nil, err
				}
				return edited.WithRosterCondition(r.Index, int(d.Internal), rows[r.Index].Progress.CurrentHP)
			})
		}
		if a.formNumber(c, "Level", key+"/level", &d.Level, 1, float64(cap)) {
			if int(d.Level) == cap {
				d.Experience = 0
			}
			stage()
		}
		if a.formNumber(c, "Experience", key+"/experience", &d.Experience, 0, 99) {
			stage()
		}
		if a.formNumber(c, "SkillPoints", key+"/sp", &d.SP, 0, 9999) {
			stage()
		}
		if a.formNumber(c, "InternalLevel", key+"/internal", &d.Internal, -100, 100) {
			stage()
		}
		a.applyButton(c, key+"/values")
	})
}

var weaponKeys = []string{"", "Sword", "Lance", "Axe", "Bow", "Dagger", "Magic", "Staff", "Arts"}

func (a *App) weaponNames(mask uint32) string {
	names := []string{}
	for i, k := range weaponKeys {
		if i > 0 && mask&(1<<i) != 0 {
			names = append(names, a.T(k))
		}
	}
	return strings.Join(names, " / ")
}
func (a *App) rosterStats(c *ui.Context, key string, r save.RosterCharacter) {
	hpmax := 255
	if a.Save != nil {
		if n, e := a.Save.RosterMaximumHP(r.Index); e == nil {
			hpmax = n
		}
	}
	ui.Row(c).Gap(32).AlignItems(ui.End).Children(func() {
		ui.Column(c).Grow(1).Basis(0).Children(func() {
			a.numberEdit(c, "CurrentHP", key+"/hp", r.Progress.CurrentHP, 0, hpmax, func(s *save.Save, n int) (*save.Save, error) {
				rows, e := s.Roster()
				if e != nil {
					return nil, e
				}
				return s.WithRosterCondition(r.Index, rows[r.Index].Progress.InternalLevel, n)
			})
		})
		if ui.Button(c, a.T("RestoreHP")).Height(42).Grow(1).Basis(0).Clicked() {
			a.perform(key, func(s *save.Save) (*save.Save, error) {
				n, e := s.RosterMaximumHP(r.Index)
				if e != nil {
					return nil, e
				}
				return s.WithRosterCondition(r.Index, r.Progress.InternalLevel, n)
			})
		}
	})
	ui.Row(c).AlignItems(ui.Center).Children(func() {
		ui.Text(c, a.T("PersonalStats")).Bold().Grow(1)
		a.button(c, "MaximizeStats", func() {
			index := r.Index
			a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeRosterStats(&index) })
		})
	})
	ui.Grid(c).Columns(2).Gap(16).Children(func() {
		for _, stat := range r.Stats {
			if stat.Stat == save.Sight {
				continue
			}
			stat := stat
			label := save.StatNames[stat.Stat]
			low, high := -128, 127
			job := catalog.Class(r.ClassHash)
			if job != nil {
				base := 0
				if stat.Stat == save.HP {
					base = 1
				}
				low = max(-128, base-job.BaseStats[stat.Stat])
				if stat.Stat == save.Movement {
					high = 2
				}
			}
			fieldKey := fmt.Sprintf("%s/stats/%d", key, stat.Stat)
			value := a.number(fieldKey, float64(stat.PersonalValue))
			ui.Column(c.Key(label)).Gap(8).Children(func() {
				ui.Row(c).Children(func() {
					ui.Text(c, a.T(label)).Grow(1)
					if job != nil && stat.Maximum != nil {
						preview := int(*value) + job.BaseStats[stat.Stat]
						preview = max(0, min(*stat.Maximum, preview))
						if stat.Stat == save.HP {
							preview = max(1, preview)
						}
						ui.Text(c, replaceFormat(a.T("CurrentClassStat"), fmt.Sprint(preview), fmt.Sprint(*stat.Maximum)))
					}
				})
				if ui.NumberInput(c, value, float64(min(low, stat.PersonalValue)), float64(max(high, stat.PersonalValue)), 1).Height(42).Label(a.T(label)).Changed() {
					n := int(*value)
					a.stage(fieldKey, func(s *save.Save) (*save.Save, error) { return s.WithRosterStat(r.Index, stat.Stat, n, true) })
				}
			})
		}
	})
	a.applyButton(c, key)
}
func (a *App) rosterItems(c *ui.Context, key string, r save.RosterCharacter) {
	ui.Row(c).Gap(32).AlignItems(ui.Start).Children(func() {
		selected := -1
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Gap(16).Children(func() {
			a.button(c, "RestoreAllUses", func() { a.perform(key, func(s *save.Save) (*save.Save, error) { return s.RestoreRosterUses(r.Index) }) })
			ui.Column(c).Height(260).Children(func() {
				rows := []row{}
				for _, v := range r.Items {
					rows = append(rows, row{strconv.Itoa(v.Slot), a.itemLabel(v.Item), ""})
				}
				id := a.list(c, key+"/items", "", rows, nil, nil)
				if id != "" {
					selected, _ = strconv.Atoi(id)
				}
			})
		})
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
			if selected >= 0 && selected < len(r.Items) {
				index := r.Index
				a.itemEditor(c, key, &index, selected, r.Items[selected].Item)
			}
		})
	})
}
func (a *App) rosterSkills(c *ui.Context, key string, r save.RosterCharacter) {
	type skills struct {
		First, Second string
		Class         bool
	}
	d := draft(a, key+"/skills", func() skills {
		v := skills{Class: r.Progress.ClassSkill != nil}
		if len(r.Progress.EquippedSkills) > 0 {
			v.First = fmt.Sprint(r.Progress.EquippedSkills[0].Hash)
		}
		if len(r.Progress.EquippedSkills) > 1 {
			v.Second = fmt.Sprint(r.Progress.EquippedSkills[1].Hash)
		}
		return v
	})
	a.columns(c, func() {
		ui.Text(c, a.T("EquippedSkills")).Bold()
		options := []option{{"", a.T("None")}}
		for _, s := range r.Progress.InheritedSkills {
			options = append(options, option{fmt.Sprint(s.Hash), a.skillName(s.Hash)})
		}
		stage := func() {
			a.stage(key+"/skills", func(s *save.Save) (*save.Save, error) {
				var first, second *uint32
				if d.First != "" {
					n, _ := strconv.ParseUint(d.First, 10, 32)
					v := uint32(n)
					first = &v
				}
				if d.Second != "" {
					n, _ := strconv.ParseUint(d.Second, 10, 32)
					v := uint32(n)
					second = &v
				}
				return s.WithRosterEquippedSkills(r.Index, first, second)
			})
		}
		if a.combo(c, "1", key+"/equipped-1", &d.First, options) {
			stage()
		}
		if a.combo(c, "2", key+"/equipped-2", &d.Second, options) {
			stage()
		}
		a.applyButton(c, key+"/skills")
		ui.Text(c, a.T("ClassSkill")).Bold()
		if job := catalog.Class(r.ClassHash); job != nil && job.LearningSkill != "" {
			ui.Text(c, a.skillName(catalog.Hash(job.LearningSkill)))
		}
		learned := false
		if job := catalog.Class(r.ClassHash); job != nil && job.LearningSkill != "" {
			level := 5
			if job.MaxLevel >= 40 {
				level = 25
			}
			learned = r.Values.Level >= level
		}
		if ui.Checkbox(c, &d.Class, a.T("Unlocked")).Disabled(!learned).Changed() {
			on := d.Class
			a.stage(key+"/class-skill", func(s *save.Save) (*save.Save, error) { return s.WithRosterClassSkill(r.Index, on) })
		}
		a.applyButton(c, key+"/class-skill")
	}, func() {
		ui.Text(c, a.T("InheritedSkills")).Bold()
		var selected string
		ui.Column(c).Height(220).Children(func() {
			rows := []row{}
			for _, s := range r.Progress.InheritedSkills {
				rows = append(rows, row{fmt.Sprint(s.Hash), a.skillName(s.Hash), ""})
			}
			selected = a.list(c, key+"/inherited", "", rows, nil, nil)
		})
		options := []option{}
		for _, s := range catalog.Skills {
			if s.Inheritable {
				options = append(options, option{s.Id, s.Name(a.Language)})
			}
		}
		sort.SliceStable(options, func(i, j int) bool { return options[i].Name < options[j].Name })
		choice := draft(a, key+"/skill-choice", func() string {
			if len(options) > 0 {
				return options[0].ID
			}
			return ""
		})
		a.combo(c, "Skills", key+"/skill-choice", choice, options)
		a.button(c, "UnlockSkill", func() {
			id := *choice
			a.apply(func(s *save.Save) (*save.Save, error) { return s.WithRosterSkill(r.Index, id, true) })
		})
		if ui.Button(c, a.T("RemoveSkill")).Height(42).Disabled(selected == "").Clicked() {
			hash, _ := strconv.ParseUint(selected, 10, 32)
			if def := catalog.Skill(uint32(hash)); def != nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.WithRosterSkill(r.Index, def.Id, false) })
			}
		}
		a.button(c, "UnlockAll", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAllRosterSkills(r.Index) }) })
	})
}
func (a *App) rosterProficiencies(c *ui.Context, key string, r save.RosterCharacter) {
	ui.Column(c).Width(340).Gap(16).Children(func() {
		mask := draft(a, key+"/proficiencies", func() uint32 { return r.Progress.Proficiencies & 510 })
		for i, k := range weaponKeys {
			if i == 0 {
				continue
			}
			bit := uint32(1 << i)
			on := *mask&bit != 0
			if ui.Checkbox(c.Key(k), &on, a.T(k)).Disabled((r.Progress.OriginalProficiencies|r.Progress.SelectedWeapons)&bit != 0).Changed() {
				if on {
					*mask |= bit
				} else {
					*mask &^= bit
				}
				v := *mask
				a.stage(key+"/proficiencies", func(s *save.Save) (*save.Save, error) { return s.WithRosterProficiencies(r.Index, v) })
			}
		}
		a.button(c, "UnlockAll", func() {
			a.apply(func(s *save.Save) (*save.Save, error) { return s.WithRosterProficiencies(r.Index, 510) })
		})
		a.applyButton(c, key+"/proficiencies")
	})
}
func (a *App) rosterEquipment(c *ui.Context, key string, r save.RosterCharacter) {
	current, _ := read(a, key+"/equipment-current", func() (save.EquipmentSelection, error) { return a.Save.RosterEquipment(r.Index) })
	options, _ := read(a, key+"/equipment-options", func() ([]save.EquipmentOption, error) { return a.Save.RosterEquipmentOptions(r.Index) })
	ui.Column(c).Width(340).Gap(16).Children(func() {
		kind := draft(a, key+"/equipment-kind", func() string { return fmt.Sprint(current.Kind) })
		kindChanged := a.combo(c, "Kind", key+"/equipment-kind", kind, []option{{"0", a.T("None")}, {"1", a.T("Emblem")}, {"2", a.T("BondRings")}})
		choices := []option{}
		for _, o := range options {
			if fmt.Sprint(o.Selection.Kind) != *kind {
				continue
			}
			name := a.T("None")
			if o.EmblemId != nil {
				name = a.emblemName(*o.EmblemId)
			}
			if o.RingHash != nil {
				if d := catalog.BondRing(*o.RingHash); d != nil {
					name = d.Name(a.Language) + " · " + d.RankName()
				}
			}
			if o.OwnerIndex != nil {
				roster, _ := a.Save.Roster()
				name += " · " + a.characterName(roster[*o.OwnerIndex])
			}
			choices = append(choices, option{fmt.Sprint(o.Selection.InstanceId), name})
		}
		selected := draft(a, key+"/equipment-choice", func() string { return fmt.Sprint(current.InstanceId) })
		if kindChanged && len(choices) > 0 {
			*selected = choices[0].ID
		}
		if *kind != "0" {
			a.combo(c, "Equipment", key+"/equipment", selected, choices)
		}
		a.button(c, "ApplyChanges", func() {
			k, _ := strconv.Atoi(*kind)
			instance, _ := strconv.ParseUint(*selected, 10, 32)
			a.apply(func(s *save.Save) (*save.Save, error) {
				return s.WithRosterEquipment(r.Index, save.EquipmentSelection{Kind: save.EquipmentKind(k), InstanceId: uint32(instance)})
			})
		})
		a.button(c, "Unequip", func() {
			a.apply(func(s *save.Save) (*save.Save, error) {
				return s.WithRosterEquipment(r.Index, save.EquipmentSelection{})
			})
		})
	})
}
func (a *App) transferCharacter(index int, export bool) {
	source := a.Save
	scope := fmt.Sprintf("roster/%d", index)
	if export {
		var err error
		source, err = a.snapshot(scope)
		if err != nil {
			a.Status = err.Error()
			return
		}
	}
	path := a.Path
	exportTitle, importTitle := a.T("ExportCharacter"), a.T("ImportCharacter")
	go func() {
		if export {
			b, err := source.ExportRosterCharacter(index)
			if err != nil {
				a.post(func() { a.Status = err.Error() })
				return
			}
			output, err := mygo.Dialog.Save(mygo.SaveDialogOptions{Parent: a.win, Title: exportTitle, DefaultPath: filepath.Join(filepath.Dir(path), fmt.Sprintf("character-%d.json", index))})
			if err == nil && output != "" {
				err = save.WriteRosterTransfer(output, b)
			}
			a.post(func() {
				if err != nil {
					a.Status = err.Error()
				} else if output != "" {
					a.Status = a.T("CharacterExported") + " " + output
				}
			})
		} else {
			paths, err := mygo.Dialog.Open(mygo.OpenDialogOptions{Parent: a.win, Title: importTitle})
			if err != nil {
				a.post(func() { a.Status = err.Error() })
				return
			}
			if len(paths) == 0 {
				return
			}
			b, err := os.ReadFile(paths[0])
			a.post(func() {
				if err != nil {
					a.Status = err.Error()
					return
				}
				if a.Save == source {
					if a.apply(func(s *save.Save) (*save.Save, error) { return s.ImportRosterCharacter(index, b) }) {
						a.discard(scope)
						a.pruneDrafts()
					}
				}
			})
		}
	}()
}
