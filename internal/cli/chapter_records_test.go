package cli

import (
	"bytes"
	"encoding/json"
	"os"
	"testing"
)

func TestChapterRecordsReadOnly(t *testing.T) {
	path := "../save/testdata/chapter-records.bin"
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var out, errors bytes.Buffer
	if code := Run([]string{"chapter-records", path, "--json", "--language", "en"}, &out, &errors); code != 0 {
		t.Fatal(code, errors.String())
	}
	var rows []struct {
		Position                      int
		ChapterID, MVP, Class, Emblem string
		ClearTurn                     int
		ClearTimeSeconds              float32
	}
	if err := json.Unmarshal(out.Bytes(), &rows); err != nil {
		t.Fatal(err)
	}
	if len(rows) != 4 || rows[1].Position != 2 || rows[1].ChapterID != "CID_M014" || rows[1].MVP != "Panette" || rows[1].ClearTurn != 34 || rows[1].ClearTimeSeconds != 5341.7890625 || rows[2].Emblem != "Claude" {
		t.Fatalf("incorrect read-only export %s", out.String())
	}
	out.Reset()
	errors.Reset()
	if code := Run([]string{"chapter-records", path, "--language", "invalid"}, &out, &errors); code == 0 {
		t.Fatal("invalid language accepted")
	}
	after, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(before, after) {
		t.Fatal("read-only command changed source")
	}
}
