package cli

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
)

func TestRosterOrderCommands(t *testing.T) {
	s, err := save.Load("../save/testdata/creation.bin")
	if err != nil {
		t.Fatal(err)
	}
	s, err = s.WithAddedRosterCharacter("PID_フラン")
	if err != nil {
		t.Fatal(err)
	}
	root := t.TempDir()
	source := filepath.Join(root, "source")
	if err := s.WriteCopy(source); err != nil {
		t.Fatal(err)
	}
	original := s.Bytes()
	run := func(args ...string) (int, string) {
		var out, errors bytes.Buffer
		code := Run(append([]string{"roster"}, args...), &out, &errors)
		return code, out.String() + errors.String()
	}
	for _, test := range []struct {
		name string
		args []string
		want []string
	}{
		{"move-down", []string{"reorder", "--character", "0", "--to", "2"}, []string{"PID_ユナカ", "PID_フラン", "PID_リュール"}},
		{"move-up", []string{"reorder", "--character", "2", "--to", "0"}, []string{"PID_フラン", "PID_リュール", "PID_ユナカ"}},
		{"sort", []string{"sort", "--by", "recruitment"}, []string{"PID_リュール", "PID_フラン", "PID_ユナカ"}},
	} {
		t.Run(test.name, func(t *testing.T) {
			output := filepath.Join(root, test.name)
			args := append([]string{test.args[0], source, output}, test.args[1:]...)
			if code, text := run(args...); code != 0 {
				t.Fatal(code, text)
			}
			edited, err := save.Load(output)
			if err != nil {
				t.Fatal(err)
			}
			rows, err := edited.Roster()
			if err != nil || len(rows) != len(test.want) {
				t.Fatal(err, len(rows))
			}
			for i, id := range test.want {
				if rows[i].PersonHash != catalog.Hash(id) {
					t.Fatal("CLI saved the wrong order")
				}
			}
			if code, _ := run(args...); code == 0 {
				t.Fatal("CLI overwrote an existing output")
			}
		})
	}
	for i, args := range [][]string{
		{"reorder", "--character", "-1", "--to", "0"},
		{"reorder", "--character", "0", "--to", "3"},
		{"reorder", "--character", "0"},
		{"reorder", "--character", "0", "--to", "1", "--to", "2"},
		{"sort", "--by", "level"},
		{"sort"},
	} {
		output := filepath.Join(t.TempDir(), "invalid")
		command := append([]string{args[0], source, output}, args[1:]...)
		if code, _ := run(command...); code == 0 {
			t.Fatalf("invalid case %d accepted", i)
		}
		if _, err := os.Stat(output); !os.IsNotExist(err) {
			t.Fatal("invalid command created a file")
		}
	}
	if code, _ := run("sort", source, source, "--by", "recruitment"); code == 0 {
		t.Fatal("CLI accepted overwriting the input")
	}
	after, err := os.ReadFile(source)
	if err != nil || !bytes.Equal(after, original) {
		t.Fatal("CLI changed source")
	}
}

func TestRecruitmentOrderCommand(t *testing.T) {
	var out, errors bytes.Buffer
	if code := Run([]string{"roster", "recruitment-order", "--json", "--language", "zh-Hans"}, &out, &errors); code != 0 {
		t.Fatal(code, errors.String())
	}
	var rows []struct {
		Position       int
		PersonId, Name string
	}
	if err := json.Unmarshal(out.Bytes(), &rows); err != nil {
		t.Fatal(err)
	}
	if len(rows) != 41 || rows[0].Name != "琉尔" || rows[8].Name != "克萝艾" || rows[9].Name != "路易" || rows[40].Name != "玛德琳" {
		t.Fatal("wrong recruitment catalog")
	}
	for i, row := range rows {
		if row.Position != i+1 || row.PersonId != catalog.RecruitmentOrder[i] {
			t.Fatal("catalog order changed")
		}
	}
}
