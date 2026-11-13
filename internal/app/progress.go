package app

import (
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"path/filepath"
	"strconv"
)

func (a *App) animalsPage(c *ui.Context) {
	animals, ok := data(c, a, "animals", a.Save.Animals)
	if !ok {
		return
	}
	rows := []row{}
	for _, r := range animals {
		rows = append(rows, row{r.Definition.Id, r.Definition.Names.Name(a.Language), fmt.Sprint(r.Amount)})
	}
	a.split(c, "animals", "Animals", rows, listHeader{}, func() {
		a.button(c, "MaxAllAnimals", func() {
			if a.ApplyPending("animals") == nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAnimal("", 99, true) })
			}
		})
	}, func(id string) {
		var r save.Animal
		for _, row := range animals {
			if row.Definition.Id == id {
				r = row
				break
			}
		}
		ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
			if r.Definition.Id != "" {
				ui.Text(c, r.Definition.Names.Name(a.Language)).Bold()
			}
			a.numberEdit(c, "Amount", "animals/"+id, r.Amount, 0, 99, func(s *save.Save, n int) (*save.Save, error) { return s.WithAnimal(id, n, false) })
			a.button(c, "MaxAmount", func() {
				if a.ApplyPending("animals") == nil {
					a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAnimal(id, 99, false) })
				}
			})
		})
	})
}
func (a *App) amiiboPage(c *ui.Context) {
	rewards, ok := data(c, a, "amiibo", a.Save.Amiibo)
	if !ok {
		return
	}
	if !a.hasGameSave() {
		for _, d := range catalog.AmiiboRewards {
			rewards = append(rewards, save.AmiiboReward{Definition: d})
		}
	}
	ui.Scroll(c).Grow(1).Children(func() {
		a.card(c, "amiibo").Gap(editorAppearance.CardPadding).Children(func() {
			a.amiiboGroup(c, "Outfits", rewards)
			a.amiiboGroup(c, "Music", rewards)
		})
	})
}
func (a *App) amiiboGroup(c *ui.Context, group string, rewards []save.AmiiboReward) {
	unlocked := 0
	for _, r := range rewards {
		if r.Definition.Group == group && r.Unlocked {
			unlocked++
		}
	}
	ui.Column(c).Gap(editorAppearance.ControlGap).Role(ui.RoleGroup).Label(a.T("Amiibo" + group)).Children(func() {
		ui.Row(c).Gap(editorAppearance.ControlGap).AlignItems(ui.Center).Children(func() {
			ui.Text(c, a.T("Amiibo"+group)).Bold().Grow(1)
			ui.Textf(c, "%d/12", unlocked)
			a.button(c, "UnlockAll", func() {
				keys := []string{}
				for _, r := range rewards {
					if r.Definition.Group == group {
						keys = append(keys, r.Definition.Key)
					}
				}
				a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAmiibo(keys) })
			})
		})
		for _, r := range rewards {
			if r.Definition.Group != group {
				continue
			}
			unlocked := r.Unlocked
			if ui.Checkbox(c.Key(r.Definition.Key), &unlocked, r.Definition.Names.Name(a.Language)).Disabled(r.Unlocked).Changed() {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAmiibo([]string{r.Definition.Key}) })
			}
		}
	})
}
func (a *App) achievementsPage(c *ui.Context) {
	achievements, ok := data(c, a, "achievements", a.Save.Achievements)
	if !ok {
		return
	}
	category := a.tab("achievement-category")
	statusFilter := a.tab("achievement-status")
	cats := []string{a.T("AllCategories")}
	for _, key := range []string{"Unit", "Battle", "Somniel", "Shop", "System"} {
		cats = append(cats, a.T("AchievementCategory"+key))
	}
	statuses := []string{a.T("AllStatuses"), a.T("NotAchieved"), a.T("RewardAvailable"), a.T("RewardClaimed")}
	rows := []row{}
	for _, r := range achievements {
		state := 1
		if r.RewardAvailable {
			state = 2
		}
		if r.RewardClaimed {
			state = 3
		}
		if *category != 0 && *category != r.Definition.Category+1 {
			continue
		}
		if *statusFilter != 0 && *statusFilter != state {
			continue
		}
		rows = append(rows, row{r.Definition.Id, r.Definition.Names.Name(a.Language), ""})
	}
	a.split(c, "achievements", "Achievements", rows, listHeader{controls: func() {
		ui.Row(c).Gap(editorAppearance.ControlGap).Children(func() {
			categoryLabel, statusLabel := cats[*category], statuses[*statusFilter]
			if ui.Select(c.Key("category"), &categoryLabel, cats).Grow(1).Basis(0).Label(a.T("Category")).Changed() {
				for i, label := range cats {
					if label == categoryLabel {
						*category = i
					}
				}
			}
			if ui.Select(c.Key("status"), &statusLabel, statuses).Grow(1).Basis(0).Label(a.T("Status")).Changed() {
				for i, label := range statuses {
					if label == statusLabel {
						*statusFilter = i
					}
				}
			}
		})
	}}, func() {
		a.button(c, "UnlockAll", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAchievements("") }) })
	}, func(id string) {
		var r save.Achievement
		for _, row := range achievements {
			if row.Definition.Id == id {
				r = row
				break
			}
		}
		ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
			if r.Definition.Id != "" {
				ui.Text(c, r.Definition.Names.Name(a.Language)).Bold()
			}
			a.staticField(c, "Category", a.T("AchievementCategory"+[]string{"Unit", "Battle", "Somniel", "Shop", "System"}[r.Definition.Category]))
			status := "NotAchieved"
			if r.RewardAvailable {
				status = "RewardAvailable"
			}
			if r.RewardClaimed {
				status = "RewardClaimed"
			}
			a.staticField(c, "Status", a.T(status))
			a.staticField(c, "BondFragments", r.Definition.Reward)
			if ui.Button(c, a.T("UnlockAchievement")).Disabled(r.Achieved).Clicked() {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAchievements(id) })
			}
		})
	})
}
func (a *App) minigamesPage(c *ui.Context) {
	records, ok := data(c, a, "minigames", a.Save.MinigameRecords)
	if !ok {
		return
	}
	if !a.hasGameSave() {
		for _, group := range catalog.Minigames {
			for _, d := range group.Records {
				r := save.MinigameRecord{Definition: d}
				if d.RankKey() != "" {
					r.Rank = new(int)
				}
				if d.SizeKey() != "" {
					r.BestSize = new(int)
				}
				records = append(records, r)
			}
		}
	}
	byKey := map[string]save.MinigameRecord{}
	for _, r := range records {
		byKey[r.Definition.Key] = r
	}
	ui.Scroll(c).Grow(1).MinHeight(0).Children(func() {
		ui.Column(c).Gap(editorAppearance.SectionGap).Children(func() {
			ui.Grid(c).Columns(2).Gap(editorAppearance.SectionGap).AlignItems(ui.Stretch).Children(func() {
				for _, id := range []string{"PushUps", "SitUps", "Squats", "WyvernRide"} {
					for _, group := range catalog.Minigames {
						if group.Id == id {
							a.minigameCard(c, group, byKey)
						}
					}
				}
			})
			for _, group := range catalog.Minigames {
				if group.Id == "Fishing" {
					a.minigameCard(c, group, byKey)
				}
			}
		})
	})
}
func (a *App) minigameCard(c *ui.Context, group catalog.MinigameGroup, records map[string]save.MinigameRecord) {
	a.section(c, group.Names.Name(a.Language), func() {
		valueTitle := "BestScore"
		nameTitle := "Difficulty"
		if group.Id == "Fishing" {
			valueTitle, nameTitle = "Caught", "Name"
		}
		headers := []string{nameTitle, valueTitle}
		first := records[group.Records[0].Key]
		if first.BestSize != nil {
			headers = append(headers, "BestSize")
		}
		if first.Rank != nil {
			headers = append(headers, "BestRank")
		}
		ui.Grid(c).Columns(len(headers)).GapX(editorAppearance.SectionGap).GapY(editorAppearance.ControlGap).Children(func() {
			for _, header := range headers {
				ui.Text(c, a.T(header)).Bold()
			}
			for _, d := range group.Records {
				r := records[d.Key]
				label := func(field string) string {
					return group.Names.Name(a.Language) + " — " + d.Names.Name(a.Language) + " — " + a.T(field)
				}
				ui.Text(c, d.Names.Name(a.Language)).AlignSelf(ui.Center)
				ui.Column(c.Key(d.Key + "/value")).MinWidth(0).Children(func() {
					key := "minigames/" + d.Key + "/value"
					a.numberControl(c, label(valueTitle), key, float64(r.Value), 0, 2147483647, func(n int) {
						a.stage(key, func(s *save.Save) (*save.Save, error) { return s.WithMinigame(d.Key, save.MinigameValues{Value: n}) })
					})
				})
				if r.BestSize != nil {
					ui.Column(c.Key(d.Key + "/size")).MinWidth(0).Children(func() {
						key := "minigames/" + d.Key + "/size"
						a.numberControl(c, label("BestSize"), key, float64(*r.BestSize), 0, 2147483647, func(n int) {
							a.stage(key, func(s *save.Save) (*save.Save, error) {
								rows, e := s.MinigameRecords()
								if e != nil {
									return nil, e
								}
								for _, old := range rows {
									if old.Definition.Key == d.Key {
										return s.WithMinigame(d.Key, save.MinigameValues{Value: old.Value, BestSize: &n})
									}
								}
								return nil, fmt.Errorf("unknown minigame record")
							})
						})
					})
				}
				if r.Rank != nil {
					ui.Column(c.Key(d.Key + "/rank")).MinWidth(0).Children(func() {
						labels := make([]string, len(d.Ranks()))
						for i, rank := range d.Ranks() {
							labels[i] = a.T(rank)
						}
						key := "minigames/" + d.Key + "/rank"
						a.selectControl(c, label("BestRank"), key, labels[*r.Rank], labels, func(text string) {
							rank := 0
							for i, label := range labels {
								if label == text {
									rank = i
								}
							}
							a.stage(key, func(s *save.Save) (*save.Save, error) {
								rows, e := s.MinigameRecords()
								if e != nil {
									return nil, e
								}
								for _, old := range rows {
									if old.Definition.Key == d.Key {
										return s.WithMinigame(d.Key, save.MinigameValues{Value: old.Value, Rank: &rank})
									}
								}
								return nil, fmt.Errorf("unknown minigame record")
							})
						})
					})
				}
			}
		})
	})
}
func (a *App) inspectorPage(c *ui.Context) {
	tab := a.tabsView(c, "inspector", "Overview", "Variables")
	if tab == 1 {
		variables, ok := data(c, a, "variables", a.Save.Variables)
		if !ok {
			return
		}
		rows := []row{}
		for _, v := range variables {
			rows = append(rows, row{v.Key, v.Key, fmt.Sprint(v.Value)})
		}
		a.split(c, "variables", "Variables", rows, listHeader{}, nil, func(key string) {
			var v save.GameVariable
			for _, row := range variables {
				if row.Key == key {
					v = row
					break
				}
			}
			ui.Column(c).Width(340).Gap(editorAppearance.ControlGap).Children(func() {
				ui.Text(c, key)
				a.numberEdit(c, "Value", "variables/"+key, v.Value, -2147483648, 2147483647, func(s *save.Save, n int) (*save.Save, error) { return s.WithVariable(key, n) })
			})
		})
		return
	}
	sections := []row{}
	var sectionsData []save.Section
	if a.Save != nil {
		sectionsData = a.Save.Sections
	}
	for i, sec := range sectionsData {
		sections = append(sections, row{strconv.Itoa(i), sec.Name, ""})
	}
	a.split(c, "sections", "Sections", sections, listHeader{}, nil, func(id string) {
		i, _ := strconv.Atoi(id)
		ui.Column(c).Gap(editorAppearance.SectionGap).Children(func() {
			ui.Column(c).Gap(editorAppearance.ControlGap).Children(func() {
				filename := ""
				if a.Path != "" {
					filename = filepath.Base(a.Path)
				}
				a.staticField(c, "FileName", filename)
				kind := "GameSave"
				if a.Save != nil && a.Save.Kind == save.Global {
					kind = "GlobalSave"
				}
				a.staticField(c, "Kind", a.T(kind))
				a.staticField(c, "FileSize", func() int {
					if a.Save == nil {
						return 0
					}
					return a.Save.Length()
				}())
				if a.Save == nil || a.Save.FormatVersion != nil {
					version, gameVersion := 0, ""
					if a.Save != nil {
						version = int(*a.Save.FormatVersion)
						gameVersion = fmt.Sprintf("0x%X", *a.Save.GameVersion)
					}
					a.staticField(c, "FormatVersion", version)
					a.staticField(c, "GameVersion", gameVersion)
				}
				a.staticField(c, "Checksum", func() string {
					if a.Save == nil {
						return ""
					}
					return fmt.Sprintf("%08X", a.Save.Checksum)
				}())
				a.staticField(c, "SectionCount", len(sectionsData))
			})
			ui.Column(c).Gap(editorAppearance.ControlGap).Children(func() {
				ui.Text(c, a.T("SelectedSection")).Bold()
				sec := save.Section{}
				if i >= 0 && i < len(sectionsData) {
					sec = sectionsData[i]
				}
				a.staticField(c, "SectionName", sec.Name)
				a.staticField(c, "Offset", sec.Offset)
				a.staticField(c, "PayloadSize", sec.Length)
			})
		})
	})
}
