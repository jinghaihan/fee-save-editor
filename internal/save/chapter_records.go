package save

import (
	"bytes"
	"fmt"
	"math"
	"strings"
	"unicode"
	"unicode/utf16"
	"unicode/utf8"
)

// ChapterRecord is a historical battle result, independent of the current roster.
// Person, Class and Emblem use the prefixless identifiers stored by the game.
type ChapterRecord struct {
	ChapterID             string
	Person, Class, Emblem string
	ClearTime             float32 // seconds; retain the game's fractional precision
	ClearTurn             int
}

type chapterRecordSpan struct {
	value  ChapterRecord
	fields [5]int // four strings followed by the metrics
}
type chapterRecordBlock struct {
	start, end int
	rows       []chapterRecordSpan
}

func parseChapterRecordBlock(b []byte, start, end int) (block chapterRecordBlock, err error) {
	defer recoverError(&err)
	need(end-start >= 37 && int(get32(b, start)) == end-start && get32(b, start+4) == 2, "Unsupported chapter record block.")
	r := reader{b: b, pos: start + 36, end: end}
	count := int(r.byte())
	block = chapterRecordBlock{start: start, end: end}
	seen := map[string]bool{}
	for range count {
		row := chapterRecordSpan{}
		values := [4]string{}
		for i := range values {
			row.fields[i] = r.pos
			values[i] = text(r.str())
		}
		row.fields[4] = r.pos
		row.value = ChapterRecord{values[0], values[1], values[2], values[3], math.Float32frombits(r.u32()), int(int32(r.u32()))}
		need(validChapterRecord(row.value) && !seen[row.value.ChapterID], "Invalid or duplicate chapter record.")
		seen[row.value.ChapterID] = true
		block.rows = append(block.rows, row)
	}
	need(r.pos == end, "Chapter records do not match their block boundary.")
	return
}

func validChapterRecord(r ChapterRecord) bool {
	if !strings.HasPrefix(r.ChapterID, "CID_") || len(r.ChapterID) <= 4 || r.ClearTurn < 0 || int64(r.ClearTurn) > math.MaxInt32 || math.IsNaN(float64(r.ClearTime)) || math.IsInf(float64(r.ClearTime), 0) || r.ClearTime < 0 {
		return false
	}
	for _, s := range []string{r.ChapterID, r.Person, r.Class, r.Emblem} {
		if !utf8.ValidString(s) || len(utf16.Encode([]rune(s))) > 2048 || strings.ContainsFunc(s, unicode.IsControl) {
			return false
		}
	}
	return true
}

func (s *Save) chapterRecords() chapterRecordBlock {
	s.RequireGame()
	sec := s.Section("USER")
	first, last := sec.PayloadOffset(), sec.PayloadOffset()+sec.Length
	need(sec.Length >= 4 && get32(s.data, first) == 20, "Unsupported USER version for chapter records.")
	var found []chapterRecordBlock
	// USER contains nested, variable-length streams. Locate a version-2 stream by
	// its exact boundary, record count, unique chapter IDs and complete contents.
	// Never search rewind snapshots in REST or assume a fixed absolute address.
	for p := first + 4; p+37 <= last; p++ {
		length := int(get32(s.data, p))
		if length < 37 || length > last-p || get32(s.data, p+4) != 2 {
			continue
		}
		count := s.data[p+36]
		if count > 0 {
			if length < 49 || !bytes.Equal(s.data[p+41:p+49], []byte{'C', 0, 'I', 0, 'D', 0, '_', 0}) {
				continue
			}
		} else if length != 37 || get32(s.data, p+8) != 0xcdcdcdcd {
			continue
		}
		if block, err := parseChapterRecordBlock(s.data, p, p+length); err == nil {
			found = append(found, block)
		}
	}
	need(len(found) == 1, fmt.Sprintf("Expected one active chapter record block; found %d.", len(found)))
	return found[0]
}

func (s *Save) ChapterRecords() (records []ChapterRecord, err error) {
	defer recoverError(&err)
	block := s.chapterRecords()
	records = make([]ChapterRecord, len(block.rows))
	for i, row := range block.rows {
		records[i] = row.value
	}
	return
}

// WithChapterRecords edits and reorders existing records. It does not add or
// remove chapters, advance story progress, or change total play time.
func (s *Save) WithChapterRecords(records []ChapterRecord) (out *Save, err error) {
	defer recoverError(&err)
	block := s.chapterRecords()
	need(len(records) == len(block.rows), "Chapter record count cannot change.")
	original := map[string]chapterRecordSpan{}
	for _, row := range block.rows {
		original[row.value.ChapterID] = row
	}
	payload := bytes.Clone(s.data[block.start : block.start+37])
	for _, record := range records {
		old, ok := original[record.ChapterID]
		need(ok && validChapterRecord(record), "Invalid, duplicate or unknown chapter record.")
		delete(original, record.ChapterID)
		values := []string{record.ChapterID, record.Person, record.Class, record.Emblem}
		previous := []string{old.value.ChapterID, old.value.Person, old.value.Class, old.value.Emblem}
		for i, value := range values {
			if value == previous[i] {
				payload = append(payload, s.data[old.fields[i]:old.fields[i+1]]...)
			} else {
				payload = append(payload, wide(value)...)
			}
		}
		metrics := make([]byte, 8)
		put32(metrics, 0, math.Float32bits(record.ClearTime))
		put32(metrics, 4, uint32(record.ClearTurn))
		payload = append(payload, metrics...)
	}
	put32(payload, 0, uint32(len(payload)))
	sec := s.Section("USER")
	user := bytes.Clone(s.data[sec.PayloadOffset():block.start])
	user = append(user, payload...)
	user = append(user, s.data[block.end:sec.PayloadOffset()+sec.Length]...)
	return s.replaceSection(sec, user), nil
}

func (s *Save) WithChapterRecord(record ChapterRecord) (*Save, error) {
	rows, err := s.ChapterRecords()
	if err != nil {
		return nil, err
	}
	for i, row := range rows {
		if row.ChapterID == record.ChapterID {
			rows[i] = record
			return s.WithChapterRecords(rows)
		}
	}
	return nil, fmt.Errorf("Unknown chapter record: %s", record.ChapterID)
}

// MoveChapterRecord uses a zero-based destination in the complete saved list.
func (s *Save) MoveChapterRecord(chapterID string, to int) (*Save, error) {
	rows, err := s.ChapterRecords()
	if err != nil {
		return nil, err
	}
	if to < 0 || to >= len(rows) {
		return nil, fmt.Errorf("Chapter record position is out of range.")
	}
	from := -1
	for i, row := range rows {
		if row.ChapterID == chapterID {
			from = i
			break
		}
	}
	if from < 0 {
		return nil, fmt.Errorf("Unknown chapter record: %s", chapterID)
	}
	row := rows[from]
	if from < to {
		copy(rows[from:to], rows[from+1:to+1])
	} else {
		copy(rows[to+1:from+1], rows[to:from])
	}
	rows[to] = row
	return s.WithChapterRecords(rows)
}
