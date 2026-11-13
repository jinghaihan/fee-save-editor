package app

import (
	"fmt"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"strings"
)

func (a *App) tab(key string) *int {
	if v := a.tabs[key]; v != nil {
		return v
	}
	v := 0
	a.tabs[key] = &v
	return &v
}
func (a *App) table(key string) *tableState {
	if v := a.tables[key]; v != nil {
		return v
	}
	v := &tableState{}
	a.tables[key] = v
	return v
}
func (a *App) number(key string, current float64) *float64 {
	v := a.numbers[key]
	if v == nil {
		v = new(float64)
		a.numbers[key] = v
	}
	if !a.dirty(key) {
		*v = current
	}
	return v
}
func (a *App) text(key, current string) *string {
	v := a.texts[key]
	if v == nil {
		v = new(string)
		a.texts[key] = v
	}
	if !a.dirty(key) {
		*v = current
	}
	return v
}
func (a *App) choice(key, current string) *string {
	v := a.choices[key]
	if v == nil {
		v = new(string)
		a.choices[key] = v
	}
	if !a.dirty(key) {
		*v = current
	}
	return v
}
func (a *App) flag(key string, current bool) *bool {
	v := a.flags[key]
	if v == nil {
		v = new(bool)
		a.flags[key] = v
	}
	if !a.dirty(key) {
		*v = current
	}
	return v
}
func (a *App) card(c *ui.Context, title string) ui.Element {
	return ui.Column(c).Padding(20).Gap(16).Radius(editorAppearance.CardRadius).Background(cardColor(c)).Border(1, c.Theme().Border).MinWidth(0).Role(ui.RoleGroup).Label(a.T(title))
}
func (a *App) section(c *ui.Context, title string, fn func()) {
	a.card(c, title).Children(func() { ui.Text(c, a.T(title)).Bold(); fn() })
}
func (a *App) columns(c *ui.Context, fn ...func()) {
	ui.Row(c).Gap(24).AlignItems(ui.Start).Children(func() {
		for _, f := range fn {
			ui.Column(c).Grow(1).Basis(0).MinWidth(0).Gap(24).Children(f)
		}
	})
}
func (a *App) field(c *ui.Context, label string, fn func()) {
	ui.Field(c.Key(label), a.T(label), fn).Gap(8).MinWidth(0)
}
func (a *App) numberField(c *ui.Context, label, key string, value float64, lo, hi float64, update func(int)) *float64 {
	v := a.number(key, value)
	a.field(c, label, func() {
		if ui.NumberInput(c.Key(key), v, lo, hi, 1).Height(42).Label(a.T(label)).Changed() {
			update(int(*v))
		}
	})
	return v
}
func (a *App) textField(c *ui.Context, label, key, current string, update func(string)) *string {
	v := a.text(key, current)
	a.field(c, label, func() {
		if ui.TextInput(c.Key(key), v).Height(42).Label(a.T(label)).Changed() {
			update(*v)
		}
	})
	return v
}
func (a *App) selectField(c *ui.Context, label, key, current string, options []string, update func(string)) *string {
	v := a.choice(key, current)
	a.field(c, label, func() {
		if ui.Select(c.Key(key), v, options).Height(42).Label(a.T(label)).Changed() {
			update(*v)
		}
	})
	return v
}
func (a *App) button(c *ui.Context, key string, fn func()) {
	if ui.Button(c.Key(key), a.T(key)).Height(42).Clicked() {
		fn()
	}
}
func (a *App) applyButton(c *ui.Context, prefix string) {
	if ui.Button(c.Key(prefix+"/apply"), a.T("ApplyChanges")).Height(42).Clicked() {
		a.ApplyPending(prefix)
	}
}
func (a *App) numberEdit(c *ui.Context, label, key string, value, lo, hi int, fn func(*save.Save, int) (*save.Save, error)) {
	a.numberField(c, label, key, float64(value), float64(lo), float64(hi), func(n int) { a.stage(key, func(s *save.Save) (*save.Save, error) { return fn(s, n) }) })
}
func (a *App) list(c *ui.Context, key, title string, rows []row, header, footer func()) string {
	s := a.table(key)
	selectedID := ""
	if s.Selected >= 0 && s.Selected < len(s.Rows) {
		selectedID = s.Rows[s.Selected].ID
	}
	ui.Column(c.Key(key)).Fill().Gap(16).Children(func() {
		if title != "" {
			ui.Text(c, a.T(title)).Bold()
		}
		if header != nil {
			header()
		}
		ui.SearchField(c, &s.Query).Placeholder(a.T("Search")).Label(a.T("Search"))
		filtered := []row{}
		for _, r := range rows {
			if strings.Contains(strings.ToLower(r.Name+" "+r.Value+" "+r.ID), strings.ToLower(s.Query)) {
				filtered = append(filtered, r)
			}
		}
		if selectedID != "" {
			s.Selected = 0
			for i, r := range filtered {
				if r.ID == selectedID {
					s.Selected = i
					break
				}
			}
		}
		s.Rows = filtered
		s.Selected = clampIndex(s.Selected, len(filtered))
		s.List.Selected = &s.Selected
		s.List.Key = func(i int) any { return filtered[i].ID }
		old := s.Selected
		if themedList(c, &s.List, len(filtered), func(i int) {
			r := filtered[i]
			text := r.Name
			if r.Value != "" {
				text += " · " + r.Value
			}
			ui.Text(c, text).Padding(7, 12).SingleLine()
		}).Changed() {
			if e := a.ApplyPending(strings.Split(key, "/")[0]); e != nil {
				s.Selected = old
			}
		}
		if footer != nil {
			footer()
		}
	})
	if s.Selected < 0 || s.Selected >= len(s.Rows) {
		return ""
	}
	return s.Rows[s.Selected].ID
}
func clampIndex(index, length int) int {
	if length == 0 {
		return -1
	}
	return max(0, min(index, length-1))
}
func (a *App) split(c *ui.Context, key, title string, rows []row, header, footer func(), detail func(string)) {
	ui.Row(c).Grow(1).Basis(0).MinHeight(0).Gap(24).AlignItems(ui.Stretch).Children(func() {
		id := ""
		width := float32(320)
		windowWidth, _ := c.Size()
		if windowWidth < 1000 {
			width = 280
		}
		a.card(c, title).Width(width).MinHeight(0).Children(func() { id = a.list(c, key, title, rows, header, footer) })
		a.card(c, key+"Editor").Grow(1).MinHeight(0).Children(func() {
			ui.Scroll(c).Grow(1).MinHeight(0).Children(func() {
				ui.Column(c).Gap(16).MinWidth(0).Children(func() {
					if id != "" || key == "convoy" || a.Save == nil || a.Save.Kind == save.Global {
						detail(id)
					}
				})
			})
		})
	})
}
func (a *App) staticField(c *ui.Context, label string, value any) {
	a.field(c, label, func() { ui.Text(c, fmt.Sprint(value)).MinHeight(24) })
}
func (a *App) tabsView(c *ui.Context, key string, labels ...string) int {
	tab := a.tab(key)
	names := make([]string, len(labels))
	for i, l := range labels {
		names[i] = a.T(l)
	}
	ui.ScrollHorizontal(c).Children(func() {
		old := *tab
		if ui.Tabs(c, tab, names...).Changed() {
			if a.Save != nil && a.ApplyPending("") != nil {
				*tab = old
			}
		}
	})
	return *tab
}
func (a *App) pick(c *ui.Context, label, key string, options []string) string {
	v := a.choices[key]
	if v == nil {
		v = new(string)
		if len(options) > 0 {
			*v = options[0]
		}
		a.choices[key] = v
	}
	a.field(c, label, func() { ui.Select(c.Key(key), v, options).Height(42).Label(a.T(label)) })
	return *v
}
func draft[T any](a *App, key string, init func() T) *T {
	if v, ok := a.drafts[key]; ok {
		return v.(*T)
	}
	value := init()
	a.drafts[key] = &value
	return &value
}
func (a *App) pruneDrafts() {
	for key := range a.drafts {
		if !a.dirty(key) {
			delete(a.drafts, key)
		}
	}
}

type option struct{ ID, Name string }
type optionDisplay struct{ ID, Language, Text string }

func (a *App) combo(c *ui.Context, label, key string, selected *string, options []option) bool {
	changed := false
	a.field(c, label, func() {
		labels := make([]string, len(options))
		counts := map[string]int{}
		for _, o := range options {
			counts[o.Name]++
		}
		current := *selected
		for i, o := range options {
			labels[i] = o.Name
			if counts[o.Name] > 1 {
				labels[i] += " [" + o.ID + "]"
			}
			if o.ID == *selected {
				current = labels[i]
			}
		}
		box := ui.Column(c.Key(key)).MinWidth(0)
		state := ui.Local(box, "option", func() optionDisplay { return optionDisplay{*selected, a.Language, current} })
		if state.ID != *selected || state.Language != a.Language {
			state.ID = *selected
			state.Language = a.Language
			state.Text = current
		}
		box.Children(func() {
			if ui.Combobox(c, &state.Text, labels).Height(42).Label(a.T(label)).Changed() {
				for i, text := range labels {
					if state.Text == text {
						*selected = options[i].ID
						state.ID = *selected
						changed = true
						break
					}
				}
			}
		})
	})
	return changed
}
func (a *App) formNumber(c *ui.Context, label, key string, value *float64, lo, hi float64) bool {
	changed := false
	a.field(c, label, func() { changed = ui.NumberInput(c.Key(key), value, lo, hi, 1).Height(42).Label(a.T(label)).Changed() })
	return changed
}
func (a *App) query(c *ui.Context, key string) *string {
	v := a.texts[key]
	if v == nil {
		v = new(string)
		a.texts[key] = v
	}
	ui.SearchField(c.Key(key), v).Placeholder(a.T("Search")).Label(a.T("Search"))
	return v
}

func replaceFormat(text string, values ...string) string {
	for i, v := range values {
		text = strings.ReplaceAll(text, fmt.Sprintf("{%d}", i), v)
		text = strings.ReplaceAll(text, fmt.Sprintf("{%d:N0}", i), v)
	}
	return text
}
