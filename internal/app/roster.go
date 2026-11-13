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
	a.split(c, "roster", "CharacterList", rows, listHeader{}, func() {
		ui.Column(c.Key("roster-actions")).Gap(editorAppearance.ControlGap).Children(func() {
			a.button(c, "MaximizeAllStats", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeRosterStats(nil) }) })
			id := a.selectedID("roster")
			index, _ := strconv.Atoi(id)
			ui.Row(c).Gap(editorAppearance.ControlGap).Children(func() {
				for _, export := range []bool{true, false} {
					key := "Import"
					if export {
						key = "Export"
					}
					if ui.Button(c, a.T(key)).Grow(1).Basis(0).Disabled(id == "").Clicked() {
						a.transferCharacter(index, export)
					}
				}
			})
			ui.Row(c).Gap(editorAppearance.ControlGap).Children(func() {
				a.addCharacterButton(c)
				if ui.Button(c, a.T("Delete")).Grow(1).Basis(0).Disabled(id == "" || a.Save == nil || !a.Save.CanDeleteRosterCharacter(index)).Clicked() {
					a.ask("ConfirmDeleteCharacter", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.WithoutRosterCharacter(index) }) })
				}
			})
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
		ui.Column(c.Key(id)).Gap(editorAppearance.SectionGap).Children(func() {
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
	anchor := ui.Button(c, a.T("Add")).Grow(1).Basis(0).Disabled(len(missing) == 0)
	open := ui.Local(anchor, "open", func() bool { return false })
	if anchor.Clicked() {
		*open = !*open
	}
	ui.Popover(c, anchor, open, func() {
		ui.Column(c).Width(280).Gap(editorAppearance.ControlGap).Children(func() {
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
			a.comboControl(c, "CharacterList", "add-character", selected, options)
			a.button(c, "Add", func() {
				id := *selected
				if a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAddedRosterCharacter(id) }) {
					*open = false
				}
			})
		})
	})
}

type rosterGeneralDraft struct {
	Class, Weapons, Force           string
	Level, Experience, SP, Internal float64
	ClassChanged                    bool
}

func (a *App) rosterGeneral(c *ui.Context, key string, r save.RosterCharacter) {
	d := draft(a, key+"/values", func() rosterGeneralDraft {
		class := ""
		if job := catalog.Class(r.ClassHash); job != nil {
			class = job.Id
		}
		return rosterGeneralDraft{
			Class: class, Weapons: fmt.Sprint(r.Progress.SelectedWeapons), Force: fmt.Sprint(r.Force),
			Level: float64(r.Values.Level), Experience: float64(r.Values.Experience),
			SP: float64(r.Values.SkillPoints), Internal: float64(r.Progress.InternalLevel),
		}
	})
	stage := func() {
		v := *d
		a.stage(key+"/values", func(s *save.Save) (*save.Save, error) {
			mask, _ := strconv.ParseUint(v.Weapons, 10, 32)
			edited := s
			var err error
			if v.ClassChanged {
				edited, err = edited.WithRosterClass(r.Index, v.Class, uint32(mask))
				if err != nil {
					return nil, err
				}
			}
			edited, err = edited.WithRosterValues(r.Index, save.RosterValue{Level: int(v.Level), Experience: int(v.Experience), SkillPoints: int(v.SP)})
			if err != nil {
				return nil, err
			}
			rows, err := edited.Roster()
			if err != nil {
				return nil, err
			}
			hp, force := rows[r.Index].Progress.CurrentHP, rows[r.Index].Force
			edited, err = edited.WithRosterCondition(r.Index, int(v.Internal), hp)
			if err != nil {
				return nil, err
			}
			target, _ := strconv.Atoi(v.Force)
			if save.UnitForce(target) != force {
				return edited.WithRosterForce(r.Index, save.UnitForce(target))
			}
			return edited, nil
		})
	}
	job := catalog.Class(catalog.Hash(d.Class))
	a.columns(c, func() {
		a.field(c, "Status", func() {
			ui.Row(c).Gap(editorAppearance.ControlGap).AlignItems(ui.Center).Children(func() {
				ui.Text(c, a.T("Roster"+r.Availability())).Grow(1)
				if ui.Button(c, a.T("RestoreCharacter")).Disabled(a.Save == nil || !a.Save.CanRestoreRosterCharacter(r.Index)).Clicked() {
					a.perform(key, func(s *save.Save) (*save.Save, error) { return s.RestoreRosterCharacter(r.Index) })
				}
			})
		})
		forces := []option{{"3", a.T("ForceAbsent")}, {"4", a.T("ForceDead")}, {"5", a.T("ForceLost")}}
		ui.Column(c).Disabled(a.Save == nil || !a.Save.CanMoveRosterCharacter(r.Index)).Children(func() {
			if a.combo(c, "Force", key+"/force", &d.Force, forces) {
				stage()
			}
		})
		classes, _ := read(a, fmt.Sprintf("classes-%d", r.Index), func() ([]catalog.ClassDefinition, error) { return a.Save.RosterClasses(r.Index) })
		options := []option{}
		for _, v := range classes {
			options = append(options, option{v.Id, v.Name(a.Language)})
		}
		sort.SliceStable(options, func(i, j int) bool { return options[i].Name < options[j].Name })
		changed := a.combo(c, "Class", key+"/class", &d.Class, options)
		job = catalog.Class(catalog.Hash(d.Class))
		variants := []option{}
		if job != nil {
			for _, mask := range job.WeaponVariants() {
				variants = append(variants, option{fmt.Sprint(mask), a.weaponNames(mask)})
			}
		}
		valid := false
		for _, v := range variants {
			valid = valid || v.ID == d.Weapons
		}
		if (changed || !valid) && len(variants) > 0 {
			d.Weapons = variants[0].ID
		}
		weaponsChanged := a.combo(c, "Weapons", key+"/weapons", &d.Weapons, variants)
		if changed || weaponsChanged {
			d.ClassChanged = true
			preview, err := a.snapshot(key)
			if err == nil {
				mask, _ := strconv.ParseUint(d.Weapons, 10, 32)
				preview, err = preview.WithRosterClass(r.Index, d.Class, uint32(mask))
			}
			if err == nil {
				rows, e := preview.Roster()
				err = e
				if err == nil {
					v := rows[r.Index]
					d.Level, d.Experience, d.SP, d.Internal = float64(v.Values.Level), float64(v.Values.Experience), float64(v.Values.SkillPoints), float64(v.Progress.InternalLevel)
				}
			}
			if err != nil {
				a.Status = err.Error()
			}
			stage()
		}
	}, func() {
		cap := 255
		if job != nil {
			cap = job.MaxLevel
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
	ui.Row(c).Gap(editorAppearance.SectionGap).AlignItems(ui.End).Children(func() {
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
			a.numberEdit(c, "CurrentHP", key+"/hp", r.Progress.CurrentHP, 0, hpmax, func(s *save.Save, n int) (*save.Save, error) {
				rows, e := s.Roster()
				if e != nil {
					return nil, e
				}
				return s.WithRosterCondition(r.Index, rows[r.Index].Progress.InternalLevel, n)
			})
		})
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
			if ui.Button(c, a.T("RestoreHP")).Clicked() {
				a.perform(key, func(s *save.Save) (*save.Save, error) {
					n, e := s.RosterMaximumHP(r.Index)
					if e != nil {
						return nil, e
					}
					return s.WithRosterCondition(r.Index, r.Progress.InternalLevel, n)
				})
			}
		})
	})
	ui.Row(c).AlignItems(ui.Center).Children(func() {
		ui.Text(c, a.T("PersonalStats")).Bold().Grow(1)
		a.button(c, "MaximizeStats", func() {
			index := r.Index
			a.apply(func(s *save.Save) (*save.Save, error) { return s.MaximizeRosterStats(&index) })
		})
	})
	if !a.hasGameSave() {
		for i := range save.StatNames {
			r.Stats = append(r.Stats, save.CharacterStat{Stat: save.RosterStat(i)})
		}
	}
	ui.Grid(c).Columns(2).GapX(editorAppearance.SectionGap).GapY(editorAppearance.ControlGap).Children(func() {
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
			ui.Column(c.Key(label)).Gap(editorAppearance.ControlGap).Children(func() {
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
				input := ui.NumberInput(c, value, float64(min(low, stat.PersonalValue)), float64(max(high, stat.PersonalValue)), 1).Label(a.T(label))
				a.trackInput(fieldKey, input)
				if input.Changed() {
					n := int(*value)
					a.stage(fieldKey, func(s *save.Save) (*save.Save, error) { return s.WithRosterStat(r.Index, stat.Stat, n, true) })
				}
			})
		}
	})
}
func (a *App) rosterItems(c *ui.Context, key string, r save.RosterCharacter) {
	ui.Row(c).Gap(editorAppearance.SectionGap).AlignItems(ui.Start).Children(func() {
		selected := -1
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Gap(editorAppearance.ControlGap).Children(func() {
			a.button(c, "RestoreAllUses", func() { a.perform(key, func(s *save.Save) (*save.Save, error) { return s.RestoreRosterUses(r.Index) }) })
			ui.Column(c).Height(260).Children(func() {
				rows := []row{}
				for _, v := range r.Items {
					rows = append(rows, row{strconv.Itoa(v.Slot), a.itemLabel(v.Item), ""})
				}
				id := a.list(c, key+"/items", "", rows, listHeader{}, nil)
				if id != "" {
					selected, _ = strconv.Atoi(id)
				}
			})
		})
		ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
			if selected >= 0 && selected < len(r.Items) {
				index := r.Index
				a.itemEditor(c, key, &index, selected, r.Items[selected].Item)
			} else if !a.hasGameSave() {
				index := r.Index
				a.itemEditor(c, key, &index, -1, nil)
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
	}, func() {
		ui.Text(c, a.T("InheritedSkills")).Bold()
		var selected string
		ui.Column(c).Height(220).Children(func() {
			rows := []row{}
			for _, s := range r.Progress.InheritedSkills {
				rows = append(rows, row{fmt.Sprint(s.Hash), a.skillName(s.Hash), ""})
			}
			selected = a.list(c, key+"/inherited", "", rows, listHeader{}, nil)
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
		if ui.Button(c, a.T("RemoveSkill")).Disabled(selected == "").Clicked() {
			hash, _ := strconv.ParseUint(selected, 10, 32)
			if def := catalog.Skill(uint32(hash)); def != nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.WithRosterSkill(r.Index, def.Id, false) })
			}
		}
		a.button(c, "UnlockAll", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAllRosterSkills(r.Index) }) })
	})
}
func (a *App) rosterProficiencies(c *ui.Context, key string, r save.RosterCharacter) {
	ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
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
	})
}
func (a *App) rosterEquipment(c *ui.Context, key string, r save.RosterCharacter) {
	current, _ := read(a, key+"/equipment-current", func() (save.EquipmentSelection, error) { return a.Save.RosterEquipment(r.Index) })
	options, _ := read(a, key+"/equipment-options", func() ([]save.EquipmentOption, error) { return a.Save.RosterEquipmentOptions(r.Index) })
	ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
		kind := draft(a, key+"/equipment/kind", func() string { return fmt.Sprint(current.Kind) })
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
		selected := draft(a, key+"/equipment/choice", func() string {
			if !a.hasGameSave() || current.Kind == save.EquipmentNone {
				return ""
			}
			return fmt.Sprint(current.InstanceId)
		})
		if kindChanged {
			*selected = ""
			if len(choices) > 0 {
				*selected = choices[0].ID
			}
		}
		equipmentChanged := false
		ui.Column(c).Disabled(*kind == "0").Children(func() { equipmentChanged = a.combo(c, "Equipment", key+"/equipment", selected, choices) })
		if kindChanged || equipmentChanged {
			k, _ := strconv.Atoi(*kind)
			instance, _ := strconv.ParseUint(*selected, 10, 32)
			if k == 0 {
				instance = 0
			}
			selection := save.EquipmentSelection{Kind: save.EquipmentKind(k), InstanceId: uint32(instance)}
			a.stage(key+"/equipment", func(s *save.Save) (*save.Save, error) { return s.WithRosterEquipment(r.Index, selection) })
		}
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
