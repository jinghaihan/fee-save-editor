package app

import (
	"fmt"
	"math"
	"strconv"
	"strings"

	"github.com/egoist/mygo/ui"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

type chapterRecordDraft struct {
	Record save.ChapterRecord
	Turns  float64
	Time   string
}

func recordDuration(seconds float32) string {
	value := math.Round(float64(seconds)*1000) / 1000
	hours := strconv.FormatFloat(math.Floor(value/3600), 'f', 0, 64)
	if len(hours) < 2 {
		hours = "0" + hours
	}
	part := strings.TrimRight(strings.TrimRight(strconv.FormatFloat(math.Mod(value, 60), 'f', 3, 64), "0"), ".")
	if math.Mod(value, 60) < 10 {
		part = "0" + part
	}
	return fmt.Sprintf("%s:%02d:%s", hours, int(math.Mod(math.Floor(value/60), 60)), part)
}

func parseRecordDuration(value string) (float32, error) {
	fields := strings.Split(strings.TrimSpace(value), ":")
	invalid := fmt.Errorf("Use HH:MM:SS for clear time.")
	if len(fields) != 3 {
		return 0, invalid
	}
	for i, field := range fields {
		if field == "" || strings.IndexFunc(field, func(r rune) bool { return (r < '0' || r > '9') && !(i == 2 && r == '.') }) >= 0 {
			return 0, invalid
		}
	}
	h, err := strconv.ParseFloat(fields[0], 64)
	if err != nil {
		return 0, invalid
	}
	m, err := strconv.ParseUint(fields[1], 10, 8)
	if err != nil || m > 59 {
		return 0, invalid
	}
	sec, err := strconv.ParseFloat(fields[2], 64)
	if err != nil || sec < 0 || sec >= 60 {
		return 0, invalid
	}
	total := float32(h*3600 + float64(m)*60 + sec)
	if math.IsInf(float64(total), 0) || math.IsNaN(float64(total)) {
		return 0, invalid
	}
	return total, nil
}

func preserveRecordOption(options []option, id, name string) []option {
	for _, o := range options {
		if o.ID == id {
			return options
		}
	}
	return append([]option{{id, name}}, options...)
}

func (a *App) chapterRecordPage(c *ui.Context) {
	records, ok := data(c, a, "chapter-records", func() ([]save.ChapterRecord, error) { return a.Save.ChapterRecords() })
	if !ok {
		return
	}
	rows := make([]row, len(records))
	for i, r := range records {
		rows[i] = row{ID: r.ChapterID, Name: fmt.Sprintf("%02d · %s", i+1, catalog.ChapterName(r.ChapterID, a.Language))}
	}
	a.split(c, "chapter-records", "ChapterRecords", rows, listHeader{followSelection: true}, func() {
		a.chapterRecordOrder(c, records)
	}, func(id string) {
		a.chapterRecordEditor(c, id)
	})
}

func (a *App) chapterRecordOrder(c *ui.Context, records []save.ChapterRecord) {
	table := a.table("chapter-records")
	id := ""
	if table.Selected >= 0 && table.Selected < len(table.Rows) {
		id = table.Rows[table.Selected].ID
	}
	index := -1
	for i, r := range records {
		if r.ChapterID == id {
			index = i
			break
		}
	}
	ui.Row(c).Gap(editorAppearance.ControlGap).Children(func() {
		if ui.Button(c, a.T("MoveUp")).Grow(1).Basis(0).Disabled(index <= 0).Clicked() {
			a.moveChapterRecord(id, index-1)
		}
		if ui.Button(c, a.T("MoveDown")).Grow(1).Basis(0).Disabled(index < 0 || index >= len(records)-1).Clicked() {
			a.moveChapterRecord(id, index+1)
		}
	})
}

func (a *App) chapterRecordEditor(c *ui.Context, id string) {
	current, err := read(a, "chapter-records", func() ([]save.ChapterRecord, error) { return a.Save.ChapterRecords() })
	if err != nil {
		ui.Text(c, err.Error())
		return
	}
	record := save.ChapterRecord{}
	for _, r := range current {
		if r.ChapterID == id {
			record = r
			break
		}
	}
	key := "chapter-records/" + id
	d := draft(a, key, func() chapterRecordDraft {
		return chapterRecordDraft{record, float64(record.ClearTurn), recordDuration(record.ClearTime)}
	})
	stage := func() {
		updated := d.Record
		updated.ClearTurn = int(d.Turns)
		timeText := d.Time
		a.stage(key, func(s *save.Save) (*save.Save, error) {
			// Leave the original float32 bits alone unless the time was edited.
			if timeText != recordDuration(updated.ClearTime) {
				seconds, err := parseRecordDuration(timeText)
				if err != nil {
					return nil, fmt.Errorf("%s", a.T("InvalidClearTime"))
				}
				updated.ClearTime = seconds
			}
			return s.WithChapterRecord(updated)
		})
	}
	a.heading(c, "BattleResult", "")
	editorForm(c, func() {
		a.staticField(c, "Chapter", catalog.ChapterName(record.ChapterID, a.Language))
		people := []option{}
		for _, p := range catalog.Persons {
			people = append(people, option{strings.TrimPrefix(p.Id, "PID_"), p.Name(a.Language)})
		}
		people = preserveRecordOption(people, d.Record.Person, catalog.RecordPersonName(d.Record.Person, a.Language))
		if a.combo(c, "MVP", key+"/person", &d.Record.Person, people) {
			stage()
		}
		classes := []option{}
		for _, p := range catalog.Classes {
			classes = append(classes, option{strings.TrimPrefix(p.Id, "JID_"), p.Name(a.Language)})
		}
		classes = preserveRecordOption(classes, d.Record.Class, catalog.RecordClassName(d.Record.Class, a.Language))
		if a.combo(c, "RecordedClass", key+"/class", &d.Record.Class, classes) {
			stage()
		}
		emblems := []option{{"", a.T("NotRecorded")}}
		for _, p := range catalog.RecordEmblems() {
			emblems = append(emblems, option{p.ID, p.Names.Name(a.Language)})
		}
		emblems = preserveRecordOption(emblems, d.Record.Emblem, catalog.RecordEmblemName(d.Record.Emblem, a.Language))
		if a.combo(c, "RecordedEmblem", key+"/emblem", &d.Record.Emblem, emblems) {
			stage()
		}
		if a.formNumber(c, "ClearTurns", key+"/turns", &d.Turns, 0, math.MaxInt32) {
			stage()
		}
		a.field(c, "ClearTime", func() {
			input := ui.TextInput(c.Key(key+"/time"), &d.Time).Placeholder("HH:MM:SS").Label(a.T("ClearTime"))
			a.trackInput(key+"/time", input)
			if input.Changed() {
				stage()
			}
		})
	})
}

func (a *App) moveChapterRecord(id string, to int) bool {
	return a.perform("chapter-records", func(s *save.Save) (*save.Save, error) { return s.MoveChapterRecord(id, to) })
}
