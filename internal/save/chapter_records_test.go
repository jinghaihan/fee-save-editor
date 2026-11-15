package save

import (
	"bytes"
	"math"
	"os"
	"path/filepath"
	"reflect"
	"testing"
)

func checkChapterIsolation(t *testing.T, before, after *Save) {
	t.Helper()
	if !bytes.Equal(before.data[:128], after.data[:128]) {
		t.Fatal("header/play-time summary changed")
	}
	for _, sec := range before.Sections {
		if sec.Name == "USER" {
			continue
		}
		other := after.Section(sec.Name)
		if !bytes.Equal(before.data[sec.PayloadOffset():sec.PayloadOffset()+sec.Length], after.data[other.PayloadOffset():other.PayloadOffset()+other.Length]) {
			t.Fatalf("unrelated %s changed", sec.Name)
		}
	}
	b, a := before.chapterRecords(), after.chapterRecords()
	bs, as := before.Section("USER"), after.Section("USER")
	if !bytes.Equal(before.data[bs.PayloadOffset():b.start], after.data[as.PayloadOffset():a.start]) || !bytes.Equal(before.data[b.end:bs.PayloadOffset()+bs.Length], after.data[a.end:as.PayloadOffset()+as.Length]) {
		t.Fatal("unrelated USER fields changed")
	}
	if _, err := Parse(after.Bytes()); err != nil {
		t.Fatal("invalid checksum or section index:", err)
	}
}

func TestChapterRecordsRoundTrip(t *testing.T) {
	s := synthetic(t, "chapter-records")
	rows, err := s.ChapterRecords()
	if err != nil {
		t.Fatal(err)
	}
	if len(rows) != 4 || rows[0].Person != "M000_リュール" || rows[3].Emblem != "" || rows[1].ClearTurn != 34 || rows[1].ClearTime != 5341.7890625 {
		t.Fatalf("wrong records: %+v", rows)
	}
	noOp, err := s.WithChapterRecords(rows)
	if err != nil || !bytes.Equal(noOp.Bytes(), s.Bytes()) {
		t.Fatal("no-op changed raw strings/nulls or float precision", err)
	}
	original := rows[1]
	edit := original
	edit.Person = "リュール"
	edit.Class = "神竜ノ子"
	edit.Emblem = "ディミトリ"
	edit.ClearTurn = math.MaxInt32
	edit.ClearTime = 123.0625
	changed, err := s.WithChapterRecord(edit)
	if err != nil {
		t.Fatal(err)
	}
	actual, err := changed.ChapterRecords()
	if err != nil || actual[1] != edit {
		t.Fatal("edited fields did not roundtrip", actual, err)
	}
	for _, i := range []int{0, 2, 3} {
		if rows[i] != actual[i] {
			t.Fatal("another result changed")
		}
	}
	checkChapterIsolation(t, s, changed)
	restored, err := changed.WithChapterRecord(original)
	if err != nil || !bytes.Equal(restored.Bytes(), s.Bytes()) {
		t.Fatal("restoring fields changed unrelated bytes", err)
	}
	// Empty is distinct from a null encoding; moving it must retain its raw bytes.
	changed, err = s.WithChapterRecord(ChapterRecord{ChapterID: rows[3].ChapterID, Person: rows[3].Person, Class: rows[3].Class, Emblem: "アイク", ClearTime: 0, ClearTurn: 0})
	if err != nil {
		t.Fatal(err)
	}
	restored, err = changed.WithChapterRecord(rows[3])
	if err != nil {
		t.Fatal(err)
	}
	got, _ := restored.ChapterRecords()
	if got[3] != rows[3] {
		t.Fatal("empty emblem did not roundtrip")
	}
	if !reflect.DeepEqual(rows, mustChapterRecords(t, s)) {
		t.Fatal("source was mutated")
	}
}
func mustChapterRecords(t *testing.T, s *Save) []ChapterRecord {
	t.Helper()
	v, e := s.ChapterRecords()
	if e != nil {
		t.Fatal(e)
	}
	return v
}

func TestChapterRecordsMove(t *testing.T) {
	s := synthetic(t, "chapter-records")
	rows := mustChapterRecords(t, s)
	for _, move := range [][2]int{{0, 3}, {3, 0}, {1, 2}, {2, 1}, {1, 1}} {
		changed, err := s.MoveChapterRecord(rows[move[0]].ChapterID, move[1])
		if err != nil {
			t.Fatal(err)
		}
		got := mustChapterRecords(t, changed)
		if got[move[1]] != rows[move[0]] {
			t.Fatal("moved only part of a result")
		}
		checkChapterIsolation(t, s, changed)
		back, err := changed.MoveChapterRecord(rows[move[0]].ChapterID, move[0])
		if err != nil || !bytes.Equal(back.Bytes(), s.Bytes()) {
			t.Fatal("move roundtrip lost bytes", err)
		}
	}
	for _, pos := range []int{-1, 4} {
		if _, err := s.MoveChapterRecord(rows[0].ChapterID, pos); err == nil {
			t.Fatal("invalid destination accepted")
		}
	}
	if _, err := s.MoveChapterRecord("CID_unknown", 0); err == nil {
		t.Fatal("missing chapter accepted")
	}
}

func TestChapterRecordsInputLimits(t *testing.T) {
	s := synthetic(t, "chapter-records")
	rows := mustChapterRecords(t, s)
	for _, change := range []func(*ChapterRecord){
		func(r *ChapterRecord) { r.ClearTime = float32(math.NaN()) }, func(r *ChapterRecord) { r.ClearTime = float32(math.Inf(1)) }, func(r *ChapterRecord) { r.ClearTime = -1 },
		func(r *ChapterRecord) { r.ClearTurn = -1 }, func(r *ChapterRecord) { r.ClearTurn = math.MaxInt32 + 1 },
		func(r *ChapterRecord) { r.Person = "bad\x00id" }, func(r *ChapterRecord) { r.Class = string(bytes.Repeat([]byte("a"), 2049)) }, func(r *ChapterRecord) { r.Emblem = "\xff" },
	} {
		bad := rows[0]
		change(&bad)
		if _, err := s.WithChapterRecord(bad); err == nil {
			t.Fatalf("invalid result accepted: %+v", bad)
		}
	}
	for _, bad := range [][]ChapterRecord{rows[:3], {rows[0], rows[0], rows[2], rows[3]}, append(append([]ChapterRecord{}, rows...), rows[0])} {
		if _, err := s.WithChapterRecords(bad); err == nil {
			t.Fatal("record count or duplicate chapter accepted")
		}
	}
	bad := append([]ChapterRecord{}, rows...)
	bad[0].ChapterID = "CID_M999"
	if _, err := s.WithChapterRecords(bad); err == nil {
		t.Fatal("changing chapter identity accepted")
	}
}

func TestChapterRecordsMalformed(t *testing.T) {
	s := synthetic(t, "chapter-records")
	block := s.chapterRecords()
	for _, mutate := range []func([]byte){
		func(b []byte) { b[block.start+36]++ }, func(b []byte) { put32(b, block.start, uint32(block.end-block.start-1)) },
		func(b []byte) { put32(b, block.start+4, 3) }, func(b []byte) { put32(b, block.start+37, 4098) },
		func(b []byte) { put16(b, block.rows[0].fields[1]+4, 0xd800) },
		func(b []byte) { put32(b, block.rows[0].fields[4], math.Float32bits(float32(math.NaN()))) },
		func(b []byte) {
			copy(b[block.rows[1].fields[0]:block.rows[1].fields[1]], b[block.rows[0].fields[0]:block.rows[0].fields[1]])
		},
	} {
		b := s.Bytes()
		mutate(b)
		bad := verified(b)
		if _, err := bad.ChapterRecords(); err == nil {
			t.Fatal("malformed block accepted")
		}
	}
	// Two structurally valid active blocks are ambiguous, even with valid CRC.
	sec := s.Section("USER")
	payload := append(bytes.Clone(s.data[sec.PayloadOffset():sec.PayloadOffset()+sec.Length]), s.data[block.start:block.end]...)
	ambiguous := s.replaceSection(sec, payload)
	if _, err := ambiguous.ChapterRecords(); err == nil {
		t.Fatal("ambiguous USER block accepted")
	}
}

func TestProvidedChapterRecords(t *testing.T) {
	dir := os.Getenv("FEE_TEST_SAVE_DIR")
	if dir == "" {
		t.Skip("set FEE_TEST_SAVE_DIR for private save validation")
	}
	for _, name := range []string{"Manual0", "Auto", "Manual1"} {
		t.Run(name, func(t *testing.T) {
			path := filepath.Join(dir, name)
			before, err := os.ReadFile(path)
			if err != nil {
				t.Fatal(err)
			}
			s, err := Parse(before)
			if err != nil {
				t.Fatal(err)
			}
			rows := mustChapterRecords(t, s)
			if len(rows) != 53 {
				t.Fatalf("record count %d", len(rows))
			}
			noOp, err := s.WithChapterRecords(rows)
			if err != nil || !bytes.Equal(before, noOp.Bytes()) {
				t.Fatal("real no-op changed data", err)
			}
			edited := rows[12]
			edited.Person = "リュール"
			edited.Class = "神竜ノ子"
			edited.Emblem = "ディミトリ"
			edited.ClearTime = 98.125
			edited.ClearTurn = 2
			changed, err := s.WithChapterRecord(edited)
			if err != nil {
				t.Fatal(err)
			}
			checkChapterIsolation(t, s, changed)
			restored, err := changed.WithChapterRecord(rows[12])
			if err != nil || !bytes.Equal(restored.Bytes(), before) {
				t.Fatal("real field restore changed bytes", err)
			}
			moved, err := changed.MoveChapterRecord(rows[12].ChapterID, 0)
			if err != nil {
				t.Fatal(err)
			}
			out := filepath.Join(t.TempDir(), "edited")
			if err := moved.WriteCopy(out); err != nil {
				t.Fatal(err)
			}
			reloaded, err := Load(out)
			if err != nil {
				t.Fatal(err)
			}
			if mustChapterRecords(t, reloaded)[0] != edited {
				t.Fatal("saved order or edited fields lost")
			}
			back, err := reloaded.MoveChapterRecord(rows[12].ChapterID, 12)
			if err != nil {
				t.Fatal(err)
			}
			back, err = back.WithChapterRecord(rows[12])
			if err != nil || !bytes.Equal(back.Bytes(), before) {
				t.Fatal("real move+edit restore changed bytes", err)
			}
			after, err := os.ReadFile(path)
			if err != nil || !bytes.Equal(before, after) {
				t.Fatal("source file changed")
			}
		})
	}
}
