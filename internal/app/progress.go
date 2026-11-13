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
	a.split(c, "animals", "Animals", rows, nil, func() {
		a.button(c, "MaxAllAnimals", func() {
			if a.ApplyPending("animals") == nil {
				a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAnimal("", 99, true) })
			}
		})
	}, func(id string) {
		for _, r := range animals {
			if r.Definition.Id != id {
				continue
			}
			ui.Column(c).Width(340).Gap(16).Children(func() {
				ui.Text(c, r.Definition.Names.Name(a.Language)).Bold()
				a.numberEdit(c, "Amount", "animals/"+id, r.Amount, 0, 99, func(s *save.Save, n int) (*save.Save, error) { return s.WithAnimal(id, n, false) })
				a.applyButton(c, "animals")
				a.button(c, "MaxAmount", func() {
					if a.ApplyPending("animals") == nil {
						a.apply(func(s *save.Save) (*save.Save, error) { return s.WithAnimal(id, 99, false) })
					}
				})
			})
		}
	})
}
func (a *App) amiiboPage(c *ui.Context) {
	rewards, ok := data(c, a, "amiibo", a.Save.Amiibo)
	if !ok {
		return
	}
	ui.Scroll(c).Grow(1).Children(func() {
		a.columns(c, func() { a.amiiboGroup(c, "Outfits", rewards) }, func() { a.amiiboGroup(c, "Music", rewards) })
	})
}
func (a *App) amiiboGroup(c *ui.Context, group string, rewards []save.AmiiboReward) {
	a.section(c, "Amiibo"+group, func() {
		unlocked := 0
		for _, r := range rewards {
			if r.Definition.Group == group && r.Unlocked {
				unlocked++
			}
		}
		ui.Text(c, fmt.Sprintf("%d/12", unlocked))
		for _, r := range rewards {
			if r.Definition.Group != group {
				continue
			}
			key := "amiibo-selection/" + r.Definition.Key
			selected := a.flags[key]
			if selected == nil {
				selected = new(bool)
				a.flags[key] = selected
			}
			if r.Unlocked {
				*selected = true
			}
			ui.Checkbox(c.Key(key), selected, r.Definition.Names.Name(a.Language)).Disabled(r.Unlocked)
		}
		a.columns(c, func() {
			a.button(c, "UnlockSelected", func() {
				keys := []string{}
				for _, r := range rewards {
					if r.Definition.Group == group {
						if flag := a.flags["amiibo-selection/"+r.Definition.Key]; flag != nil && *flag {
							keys = append(keys, r.Definition.Key)
						}
					}
				}
				a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAmiibo(keys) })
			})
		}, func() {
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
	a.split(c, "achievements", "Achievements", rows, func() {
		ui.Row(c).Gap(12).Children(func() {
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
	}, func() {
		a.button(c, "UnlockAll", func() { a.apply(func(s *save.Save) (*save.Save, error) { return s.UnlockAchievements("") }) })
	}, func(id string) {
		for _, r := range achievements {
			if r.Definition.Id != id {
				continue
			}
			ui.Column(c).Width(340).Gap(24).Children(func() {
				ui.Text(c, r.Definition.Names.Name(a.Language)).Bold()
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
		}
	})
}
func (a *App) minigamesPage(c *ui.Context) {
	records, ok := data(c, a, "minigames", a.Save.MinigameRecords)
	if !ok {
		return
	}
	byKey := map[string]save.MinigameRecord{}
	for _, r := range records {
		byKey[r.Definition.Key] = r
	}
	ui.Scroll(c).Grow(1).MinHeight(0).Children(func() {
		ui.Column(c).Gap(24).Children(func() {
			a.columns(c, func() {
				for _, group := range catalog.Minigames {
					if group.Id == "PushUps" || group.Id == "Squats" {
						a.minigameCard(c, group, byKey)
					}
				}
			}, func() {
				for _, group := range catalog.Minigames {
					if group.Id == "SitUps" || group.Id == "WyvernRide" {
						a.minigameCard(c, group, byKey)
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
		for _, d := range group.Records {
			r := records[d.Key]
			ui.Row(c.Key(d.Key)).Gap(24).AlignItems(ui.Center).Children(func() {
				ui.Text(c, d.Names.Name(a.Language)).Grow(1).Basis(0)
				ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
					label := "BestScore"
					if d.Fishing() {
						label = "Caught"
					}
					a.numberEdit(c, label, "minigames/"+d.Key+"/value", r.Value, 0, 2147483647, func(s *save.Save, n int) (*save.Save, error) {
						return s.WithMinigame(d.Key, save.MinigameValues{Value: n})
					})
				})
				if r.BestSize != nil {
					ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
						a.numberEdit(c, "BestSize", "minigames/"+d.Key+"/size", *r.BestSize, 0, 2147483647, func(s *save.Save, n int) (*save.Save, error) {
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
				}
				if r.Rank != nil {
					ui.Column(c).Grow(1).Basis(0).MinWidth(0).Children(func() {
						labels := make([]string, len(d.Ranks()))
						for i, rank := range d.Ranks() {
							labels[i] = a.T(rank)
						}
						a.selectField(c, "BestRank", "minigames/"+d.Key+"/rank", labels[*r.Rank], labels, func(text string) {
							rank := 0
							for i, label := range labels {
								if label == text {
									rank = i
								}
							}
							a.stage("minigames/"+d.Key+"/rank", func(s *save.Save) (*save.Save, error) {
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
			})
		}
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
		a.split(c, "variables", "Variables", rows, nil, nil, func(key string) {
			for _, v := range variables {
				if v.Key == key {
					ui.Column(c).Width(340).Gap(16).Children(func() {
						ui.Text(c, key)
						a.numberEdit(c, "Value", "variables/"+key, v.Value, -2147483648, 2147483647, func(s *save.Save, n int) (*save.Save, error) { return s.WithVariable(key, n) })
						a.applyButton(c, "variables")
					})
				}
			}
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
	a.split(c, "sections", "Sections", sections, nil, nil, func(id string) {
		i, _ := strconv.Atoi(id)
		ui.Column(c).Gap(24).Children(func() {
			a.section(c, "Overview", func() {
				a.staticField(c, "FileName", filepath.Base(a.Path))
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
				if a.Save != nil && a.Save.FormatVersion != nil {
					a.staticField(c, "FormatVersion", *a.Save.FormatVersion)
					a.staticField(c, "GameVersion", fmt.Sprintf("0x%X", *a.Save.GameVersion))
				}
				a.staticField(c, "Checksum", func() string {
					if a.Save == nil {
						return ""
					}
					return fmt.Sprintf("%08X", a.Save.Checksum)
				}())
				a.staticField(c, "SectionCount", len(sectionsData))
			})
			a.section(c, "SelectedSection", func() {
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
