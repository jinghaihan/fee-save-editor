package save

import (
	"bytes"
	"os"
	"path/filepath"
	"testing"
)

func synthetic(t *testing.T, name string) *Save {
	t.Helper()
	b, e := os.ReadFile("testdata/" + name + ".bin")
	if e != nil {
		t.Fatal(e)
	}
	s, e := Parse(b)
	if e != nil {
		t.Fatal(e)
	}
	return s
}
func TestContainerLossless(t *testing.T) {
	for _, name := range []string{"game", "global", "main"} {
		s := synthetic(t, name)
		b := s.Bytes()
		d, e := Parse(b)
		if e != nil || !bytes.Equal(d.Bytes(), b) {
			t.Fatal("roundtrip changed bytes")
		}
		bad := bytes.Clone(b)
		bad[len(bad)-1] ^= 1
		if _, e = Parse(bad); e == nil {
			t.Fatal("corrupt checksum accepted")
		}
		p := filepath.Join(t.TempDir(), "copy")
		if e = s.WriteCopy(p); e != nil {
			t.Fatal(e)
		}
		if e = s.WriteCopy(p); e == nil {
			t.Fatal("copy overwrote existing output")
		}
	}
}
func TestMainRoundTripAndStringResize(t *testing.T) {
	s := synthetic(t, "main")
	before := s.Bytes()
	v, e := s.Main()
	if e != nil {
		t.Fatal(e)
	}
	if v.Money != 5000 || v.BondFragments != 1200 || v.SommieName != "Sommie" {
		t.Fatalf("main fields: %+v", v)
	}
	original := v
	v.Money = 123
	v.BondFragments = 456
	v.IronIngots = 7
	v.SteelIngots = 9
	v.SilverIngots = 11
	v.Difficulty = "Normal"
	v.GameMode = "Casual"
	v.SommieName = "索拉NewName"
	d, e := s.WithMain(v)
	if e != nil {
		t.Fatal(e)
	}
	actual, e := d.Main()
	if e != nil || actual != v {
		t.Fatal("edited values did not roundtrip")
	}
	if !bytes.Equal(before, s.Bytes()) {
		t.Fatal("source changed")
	}
	back, e := d.WithMain(original)
	if e != nil || !bytes.Equal(back.Bytes(), before) {
		t.Fatal("restoring original values changed other data")
	}
	v.Money = 10000000
	if _, e = s.WithMain(v); e == nil {
		t.Fatal("invalid money accepted")
	}
}
func TestVariableChangePreservesOtherRecords(t *testing.T) {
	s := synthetic(t, "main")
	d, e := s.WithVariable(materialKeys[0], 85)
	if e != nil {
		t.Fatal(e)
	}
	v, e := d.Main()
	if e != nil || v.IronIngots != 85 || v.Money != 5000 {
		t.Fatal("variable change")
	}
	if _, e = s.WithVariable("new-key", 1); e == nil {
		t.Fatal("arbitrary new inspector variable accepted")
	}
}
func TestNumericVariablesRejectOverflow(t *testing.T) {
	s := loadFixture(t, "main")
	if _, e := s.WithVariable("G_所持_IID_てつの晶石", 2147483648); e == nil {
		t.Fatal("signed 32-bit overflow accepted")
	}
	if _, e := s.WithVariable("G_所持_IID_てつの晶石", -2147483649); e == nil {
		t.Fatal("signed 32-bit underflow accepted")
	}
}
