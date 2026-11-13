package app

import (
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"strconv"
)

func (a *App) mainEdit(key string, patch func(*save.MainValues)) {
	a.stage(key, func(s *save.Save) (*save.Save, error) {
		v, e := s.Main()
		if e != nil {
			return nil, e
		}
		patch(&v)
		return s.WithMain(v)
	})
}
func (a *App) mainPage(c *ui.Context) {
	if a.Save != nil && a.Save.Kind == save.Global {
		a.ticketsPage(c)
		return
	}
	v, ok := data(c, a, "main", a.Save.Main)
	if !ok {
		return
	}
	ui.Scroll(c).Grow(1).MinHeight(0).Children(func() {
		a.sectionColumns(c, func() {
			a.section(c, "GameSettings", func() {
				name, e := read(a, "protagonist-name", a.Save.ProtagonistName)
				if a.Save == nil {
					v := ""
					name = &v
				}
				if e == nil && name != nil {
					a.textField(c, "PlayerName", "main/name", *name, func(n string) {
						a.stage("main/name", func(s *save.Save) (*save.Save, error) { return s.WithProtagonistName(n) })
					})
				}
				gender, e := read(a, "protagonist-gender", a.Save.ProtagonistGender)
				if a.Save == nil {
					v := 1
					gender = &v
				}
				if e == nil && gender != nil {
					labels := []string{a.T("Male"), a.T("Female")}
					a.selectField(c, "Gender", "main/gender", labels[*gender-1], labels, func(text string) {
						n := 1
						if text == labels[1] {
							n = 2
						}
						a.stage("main/gender", func(s *save.Save) (*save.Save, error) { return s.WithProtagonistGender(n) })
					})
				}
				for _, field := range []struct {
					key, current string
					values       []string
				}{{"Difficulty", v.Difficulty, save.Difficulties}, {"GameMode", v.GameMode, save.GameModes}} {
					labels := make([]string, len(field.values))
					for i, n := range field.values {
						labels[i] = a.T(n)
					}
					a.selectField(c, field.key, "main/"+field.key, a.T(field.current), labels, func(text string) {
						selected := ""
						for i, n := range labels {
							if n == text {
								selected = field.values[i]
							}
						}
						a.mainEdit("main/"+field.key, func(v *save.MainValues) {
							if field.key == "Difficulty" {
								v.Difficulty = selected
							} else {
								v.GameMode = selected
							}
						})
					})
				}
				a.textField(c, "SommieName", "main/sommie", v.SommieName, func(n string) { a.mainEdit("main/sommie", func(v *save.MainValues) { v.SommieName = n }) })
				if seconds, e := read(a, "playtime", a.Save.PlayTime); e == nil {
					a.field(c, "PlayTime", func() {
						ui.Row(c).Gap(editorAppearance.ControlGap).AlignItems(ui.Center).Children(func() {
							for i, key := range []string{"Hours", "Minutes", "Seconds"} {
								value, maximum := int(seconds)/3600, 999
								switch i {
								case 1:
									value = int(seconds) / 60 % 60
									maximum = 59
								case 2:
									value = int(seconds) % 60
									maximum = 59
								}
								n := a.number("main/time/"+key, float64(value))
								if i > 0 {
									ui.Text(c, ":")
								}
								format := "%02d"
								if i == 0 {
									format = "%d"
								}
								text := a.text("main/time/"+key, fmt.Sprintf(format, int(*n)))
								input := ui.DecimalInput(c.Key(key), text, "0", strconv.Itoa(maximum), "1").Grow(1).Basis(0).MinWidth(0).Label(a.T(key))
								a.trackInput("main/time/"+key, input)
								if input.Changed() {
									parsed, err := strconv.Atoi(*text)
									if err != nil {
										continue
									}
									*n = float64(parsed)
									a.stage("main/time", func(s *save.Save) (*save.Save, error) {
										old, e := s.PlayTime()
										if e != nil {
											return nil, e
										}
										whole := int(*a.numbers["main/time/Hours"])*3600 + int(*a.numbers["main/time/Minutes"])*60 + int(*a.numbers["main/time/Seconds"])
										if whole == int(old) {
											return s, nil
										}
										return s.WithPlayTime(float32(whole))
									})
								}
							}
						})
					})
				}
			})
			a.section(c, "Resources", func() {
				a.columns(c, func() {
					a.resourceField(c, "Money", v.Money, 9999999)
					a.resourceField(c, "IronIngots", v.IronIngots, 9999)
					a.resourceField(c, "SilverIngots", v.SilverIngots, 9999)
				}, func() {
					a.resourceField(c, "BondFragments", v.BondFragments, 9999999)
					a.resourceField(c, "SteelIngots", v.SteelIngots, 9999)
				})
			})
			a.section(c, "SomnielActivities", func() {
				activities, e := read(a, "activities", a.Save.Activities)
				if e != nil {
					ui.Text(c, e.Error())
					return
				}
				a.columns(c, func() {
					a.numberEdit(c, "TrainingRemaining", "main/activities/training", activities.TrainingRemaining, 0, 1, func(s *save.Save, n int) (*save.Save, error) {
						v, e := s.Activities()
						if e != nil {
							return nil, e
						}
						v.TrainingRemaining = n
						return s.WithActivities(v)
					})
				}, func() {
					a.numberEdit(c, "ArenaRemaining", "main/activities/arena", activities.ArenaRemaining, 0, 3, func(s *save.Save, n int) (*save.Save, error) {
						v, e := s.Activities()
						if e != nil {
							return nil, e
						}
						v.ArenaRemaining = n
						return s.WithActivities(v)
					})
				})
				a.button(c, "RestoreActivityUses", func() {
					if a.ApplyPending("main/activities") == nil {
						a.apply(func(s *save.Save) (*save.Save, error) {
							return s.WithActivities(save.ActivityValues{TrainingRemaining: 1, ArenaRemaining: 3})
						})
					}
				})
			})
		}, func() { a.donationsCard(c) })
	})
}
func (a *App) resourceField(c *ui.Context, key string, value, maximum int) {
	a.numberField(c, key, "main/"+key, float64(value), 0, float64(maximum), func(n int) {
		a.mainEdit("main/"+key, func(v *save.MainValues) {
			switch key {
			case "Money":
				v.Money = n
			case "BondFragments":
				v.BondFragments = n
			case "IronIngots":
				v.IronIngots = n
			case "SteelIngots":
				v.SteelIngots = n
			case "SilverIngots":
				v.SilverIngots = n
			}
		})
	})
}
func (a *App) donationsCard(c *ui.Context) {
	a.section(c, "Donations", func() {
		rows, e := read(a, "donations", a.Save.Donations)
		if e != nil {
			ui.Text(c, e.Error())
			return
		}
		amounts := map[string]int{}
		for _, r := range rows {
			amounts[r.Country.Id] = r.Amount
		}
		for _, d := range catalog.DonationCountries {
			savedAmount := amounts[d.Id]
			ui.Column(c.Key(d.Id)).Gap(editorAppearance.ControlGap).Children(func() {
				a.columns(c, func() {
					amount := int(*a.number("main/donations/"+d.Id, float64(savedAmount)))
					levels := []string{"1", "2", "3", "4", "5"}
					a.selectField(c, replaceFormat(a.T("CountryLevel"), d.Names.Name(a.Language)), "donation-level/"+d.Id, fmt.Sprint(d.Level(amount)), levels, func(text string) {
						n := 1
						fmt.Sscan(text, &n)
						amount := d.Thresholds[n-1]
						a.stage("main/donations/"+d.Id, func(s *save.Save) (*save.Save, error) { return s.WithDonation(d.Id, amount) })
						*a.numbers["main/donations/"+d.Id] = float64(amount)
					})
				}, func() {
					a.numberEdit(c, "DonatedGold", "main/donations/"+d.Id, savedAmount, 0, 9999999, func(s *save.Save, n int) (*save.Save, error) { return s.WithDonation(d.Id, n) })
				})
			})
		}
		a.button(c, "MaxAllLevels", func() {
			if a.ApplyPending("main/donations") != nil {
				return
			}
			a.apply(func(s *save.Save) (*save.Save, error) {
				edited := s
				for _, d := range catalog.DonationCountries {
					var e error
					edited, e = edited.WithDonation(d.Id, d.Thresholds[4])
					if e != nil {
						return nil, e
					}
				}
				return edited, nil
			})
		})
	})
}
func (a *App) ticketsPage(c *ui.Context) {
	rows, ok := data(c, a, "tickets", a.Save.Tickets)
	if !ok {
		return
	}
	ui.Column(c).Width(420).Children(func() {
		a.section(c, "RelayTickets", func() {
			names := []string{}
			for _, r := range rows {
				names = append(names, r.IdentifierHex)
			}
			if len(names) == 0 {
				return
			}
			selected := a.choices["ticket-campaign"]
			if selected == nil {
				v := names[0]
				selected = &v
				a.choices["ticket-campaign"] = selected
			}
			a.field(c, "Campaign", func() {
				if ui.Select(c, selected, names).Changed() {
					a.ApplyPending("tickets")
				}
			})
			for _, r := range rows {
				if r.IdentifierHex != *selected {
					continue
				}
				a.numberEdit(c, "Amount", "tickets/"+r.IdentifierHex, r.Amount, 0, 999, func(s *save.Save, n int) (*save.Save, error) { return s.WithTickets(r.Identifier, n) })
				a.button(c, "MaxAmount", func() {
					if a.ApplyPending("tickets") == nil {
						a.apply(func(s *save.Save) (*save.Save, error) { return s.WithTickets(r.Identifier, 999) })
					}
				})
			}
		})
	})
}
