package app

import (
	"embed"
	"encoding/json"
	"fmt"
	"github.com/egoist/mygo"
	"github.com/egoist/mygo/i18n"
	_ "github.com/egoist/mygo/locales/ja"
	_ "github.com/egoist/mygo/locales/zh-Hans"
	_ "github.com/egoist/mygo/locales/zh-Hant"
	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/assets"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"os"
	"path/filepath"
	"strings"
)

//go:embed locales/*.json
var localeFiles embed.FS
var Version = "0.4.0"
var pages = []string{"Main", "Items", "Roster", "Emblems", "Support", "Minigames", "Achievements", "Animals", "Amiibo", "Inspector"}

type edit func(*save.Save) (*save.Save, error)
type tableState struct {
	List     ui.ListState
	Query    string
	Selected int
	Rows     []row
}
type row struct{ ID, Name, Value string }
type App struct {
	drafts                 map[string]any
	Save                   *save.Save
	Path, Language, Status string
	Page                   int
	win                    *mygo.Window
	locale                 map[string]map[string]string
	tabs                   map[string]*int
	tables                 map[string]*tableState
	numbers                map[string]*float64
	texts                  map[string]*string
	choices                map[string]*string
	flags                  map[string]*bool
	pending                map[string]edit
	pendingOrder           []string
	cache                  map[string]any
	revision               int
	confirm                bool
	confirmLabel           string
	confirmAction          func()
	preferencesPath        string
	dark                   bool
	languageChosen         bool
}

func New() *App {
	a := &App{drafts: map[string]any{}, Language: "en", locale: map[string]map[string]string{}, tabs: map[string]*int{}, tables: map[string]*tableState{}, numbers: map[string]*float64{}, texts: map[string]*string{}, choices: map[string]*string{}, flags: map[string]*bool{}, pending: map[string]edit{}, cache: map[string]any{}}
	for _, l := range catalog.Languages {
		b, e := localeFiles.ReadFile("locales/" + l.Code + ".json")
		if e != nil {
			panic(e)
		}
		var text map[string]string
		if e = json.Unmarshal(b, &text); e != nil {
			panic(e)
		}
		a.locale[l.Code] = text
	}
	return a
}
func (a *App) T(key string) string {
	if value := a.locale[a.Language][key]; value != "" {
		return value
	}
	if value := a.locale["en"][key]; value != "" {
		return value
	}
	return key
}
func (a *App) SetLanguage(code string) error { return a.setLanguage(code, true) }
func (a *App) setLanguage(code string, remember bool) error {
	if a.locale[code] == nil {
		return fmt.Errorf("unsupported language: %s", code)
	}
	a.Language = code
	if remember {
		a.languageChosen = true
	}
	if a.win != nil {
		mygo.App.SetLocale(code)
		a.menu()
		a.win.Invalidate()
	}
	if remember && a.preferencesPath != "" && os.Getenv("FEE_EDITOR_LANGUAGE") == "" {
		if e := os.MkdirAll(filepath.Dir(a.preferencesPath), 0700); e != nil {
			return e
		}
		return os.WriteFile(a.preferencesPath, []byte(code), 0600)
	}
	return nil
}
func (a *App) Load(path string) error {
	s, e := save.Load(path)
	if e != nil {
		return e
	}
	if s.Kind == save.Game {
		if _, e = s.Roster(); e == nil {
			if repaired, err := s.RepairAllRosterClasses(); err == nil {
				s = repaired
			} else {
				return err
			}
		}
	}
	return a.install(path, s)
}
func (a *App) install(path string, s *save.Save) error {
	a.Save = s
	a.Path = path
	a.Status = ""
	a.drafts = map[string]any{}
	a.pending = map[string]edit{}
	a.pendingOrder = nil
	a.numbers = map[string]*float64{}
	a.texts = map[string]*string{}
	a.choices = map[string]*string{}
	a.flags = map[string]*bool{}
	a.tables = map[string]*tableState{}
	a.cache = map[string]any{}
	a.revision++
	if a.win != nil {
		a.menu()
	}
	return nil
}
func (a *App) stage(key string, fn edit) {
	if a.pending[key] == nil {
		a.pendingOrder = append(a.pendingOrder, key)
	}
	a.pending[key] = fn
}
func (a *App) discard(prefix string) {
	order := a.pendingOrder[:0]
	for _, key := range a.pendingOrder {
		if key == prefix || strings.HasPrefix(key, prefix+"/") {
			delete(a.pending, key)
		} else if a.pending[key] != nil {
			order = append(order, key)
		}
	}
	a.pendingOrder = order
}
func (a *App) dirty(key string) bool {
	for group := range a.pending {
		if key == group || strings.HasPrefix(key, group+"/") {
			return true
		}
	}
	return false
}
func (a *App) ApplyPending(prefix string) error {
	if a.Save == nil {
		return fmt.Errorf("%s", a.T("NoSave"))
	}
	edited := a.Save
	applied := []string{}
	for _, key := range a.pendingOrder {
		if prefix != "" && key != prefix && !strings.HasPrefix(key, prefix+"/") {
			continue
		}
		fn := a.pending[key]
		if fn == nil {
			continue
		}
		var e error
		edited, e = fn(edited)
		if e != nil {
			a.Status = e.Error()
			return e
		}
		applied = append(applied, key)
	}
	a.Save = edited
	for _, key := range applied {
		delete(a.pending, key)
	}
	order := a.pendingOrder[:0]
	for _, key := range a.pendingOrder {
		if a.pending[key] != nil {
			order = append(order, key)
		}
	}
	a.pendingOrder = order
	a.cache = map[string]any{}
	a.pruneDrafts()
	a.Status = ""
	return nil
}

func (a *App) snapshot(prefix string) (*save.Save, error) {
	candidate := a.Save
	for _, key := range a.pendingOrder {
		if key != prefix && !strings.HasPrefix(key, prefix+"/") {
			continue
		}
		if fn := a.pending[key]; fn != nil {
			var err error
			candidate, err = fn(candidate)
			if err != nil {
				return nil, err
			}
		}
	}
	return candidate, nil
}

// perform commits the editor and its action together, so an action error
// retains the original save and all pending edits.
func (a *App) perform(prefix string, fn edit) bool {
	if a.Save == nil {
		return false
	}
	candidate, err := a.snapshot(prefix)
	if err != nil {
		a.Status = err.Error()
		return false
	}
	next, err := fn(candidate)
	if err != nil {
		a.Status = err.Error()
		return false
	}
	a.discard(prefix)
	a.Save = next
	a.cache = map[string]any{}
	a.pruneDrafts()
	a.Status = ""
	return true
}
func (a *App) apply(fn edit) bool {
	if a.Save == nil {
		return false
	}
	s, e := fn(a.Save)
	if e != nil {
		a.Status = e.Error()
		return false
	}
	a.Save = s
	a.cache = map[string]any{}
	a.pruneDrafts()
	a.Status = ""
	return true
}
func (a *App) SaveCopy(path string) error {
	if e := a.ApplyPending(""); e != nil {
		return e
	}
	if e := a.Save.WriteCopy(path); e != nil {
		return e
	}
	a.Status = a.T("CopySaved") + " " + path
	return nil
}

type cachedResult[T any] struct {
	Value T
	Err   error
}

func read[T any](a *App, key string, fn func() (T, error)) (T, error) {
	if a.Save == nil || a.Save.Kind == save.Global && key != "tickets" {
		var zero T
		return zero, nil
	}
	if v, ok := a.cache[key]; ok {
		r := v.(cachedResult[T])
		return r.Value, r.Err
	}
	value, err := fn()
	a.cache[key] = cachedResult[T]{value, err}
	return value, err
}
func data[T any](c *ui.Context, a *App, key string, fn func() (T, error)) (T, bool) {
	value, err := read(a, key, fn)
	if err != nil {
		ui.Text(c, err.Error())
		return value, false
	}
	return value, true
}
func (a *App) post(fn func()) {
	if a.win != nil {
		a.win.Update(fn)
	} else {
		fn()
	}
}
func (a *App) open() {
	title := a.T("OpenSave")
	go func() {
		paths, err := mygo.Dialog.Open(mygo.OpenDialogOptions{Parent: a.win, Title: title})
		if err != nil {
			a.post(func() { a.Status = err.Error() })
			return
		}
		if len(paths) == 0 {
			return
		}
		// Read and parse cloud-backed files without blocking the UI thread.
		reader := New()
		err = reader.Load(paths[0])
		a.post(func() {
			if err != nil {
				a.Status = err.Error()
			} else {
				a.install(paths[0], reader.Save)
			}
		})
	}()
}
func (a *App) saveCopy() {
	if a.Save == nil {
		return
	}
	title := a.T("SaveCopy")
	defaultPath := filepath.Join(filepath.Dir(a.Path), filepath.Base(a.Path)+".edited")
	go func() {
		path, err := mygo.Dialog.Save(mygo.SaveDialogOptions{Parent: a.win, Title: title, DefaultPath: defaultPath})
		a.post(func() {
			if err != nil {
				a.Status = err.Error()
				return
			}
			if path == "" {
				return
			}
			if a.ApplyPending("") != nil {
				return
			}
			snapshot := a.Save
			go func() {
				err := snapshot.WriteCopy(path)
				a.post(func() {
					if err != nil {
						a.Status = err.Error()
					} else {
						a.Status = a.T("CopySaved") + " " + path
					}
				})
			}()
		})
	}()
}
func (a *App) menu() {
	languages := []*mygo.MenuItem{}
	for _, l := range catalog.Languages {
		code := l.Code
		languages = append(languages, &mygo.MenuItem{Label: l.NativeName, Click: func(*mygo.MenuItem, *mygo.Window) {
			a.post(func() {
				if e := a.SetLanguage(code); e != nil {
					a.Status = e.Error()
				}
			})
		}})
	}
	mygo.App.SetMenu(mygo.NewMenu([]*mygo.MenuItem{{Role: mygo.RoleAppMenu}, {Label: a.T("File"), Submenu: []*mygo.MenuItem{{Label: a.T("OpenSave"), Accelerator: "CmdOrCtrl+O", Click: func(*mygo.MenuItem, *mygo.Window) { a.open() }}, {Label: a.T("SaveCopy"), Disabled: a.Save == nil, Accelerator: "CmdOrCtrl+Shift+S", Click: func(*mygo.MenuItem, *mygo.Window) { a.saveCopy() }}, {Label: a.T("Exit"), Accelerator: "CmdOrCtrl+Q", Click: func(*mygo.MenuItem, *mygo.Window) { mygo.App.Quit() }}}}, {Label: a.T("Language"), Submenu: languages}, {Role: mygo.RoleEditMenu}, {Label: a.T("Help"), Submenu: []*mygo.MenuItem{{Label: a.T("About"), Click: func(*mygo.MenuItem, *mygo.Window) {
		go mygo.Dialog.Message(mygo.MessageOptions{Parent: a.win, Message: a.T("AppTitle") + " " + Version, Detail: "Game data: Xzonn, delvier. Save references: laqieer, LordMewtwo73. UI: MyGo.\nhttps://github.com/jinghaihan/fee-save-editor", Buttons: []string{"OK"}})
	}}}}}))
}
func (a *App) ask(key string, fn func()) {
	a.confirm = true
	a.confirmLabel = a.T(key)
	a.confirmAction = fn
}
func Run(path string) error {
	a := New()
	if dir, e := os.UserConfigDir(); e == nil {
		a.preferencesPath = filepath.Join(dir, "FeeEditor", "language.txt")
		if b, e := os.ReadFile(a.preferencesPath); e == nil {
			a.setLanguage(strings.TrimSpace(string(b)), true)
		}
	}
	if lang := os.Getenv("FEE_EDITOR_LANGUAGE"); lang != "" {
		a.SetLanguage(lang)
	}
	if path != "" {
		if e := a.Load(path); e != nil {
			a.Status = e.Error()
		}
	}
	mygo.App.WhenReady(func() {
		if !a.languageChosen {
			tags := make([]string, len(catalog.Languages))
			for i, l := range catalog.Languages {
				tags[i] = l.Code
			}
			a.setLanguage(i18n.Match(mygo.App.PreferredLanguages(), tags), false)
		}
		mygo.Theme.SetSource(mygo.ThemeSystem)
		a.dark = mygo.Theme.IsDark()
		a.win = mygo.NewWindow(mygo.WindowOptions{Title: a.T("AppTitle"), Width: 1120, Height: 780, MinWidth: 860, MinHeight: 600, StateKey: "main", Content: ui.View(a.View)})
		if err := a.win.SetIcon(assets.WindowIcon); err != nil {
			a.Status = err.Error()
		}
		a.menu()
		mygo.Theme.OnUpdated(func() { a.post(func() { a.dark = mygo.Theme.IsDark() }) })
	})
	return mygo.App.Run()
}
