package save

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"hash/crc32"
	"os"
	"path/filepath"
	"strings"
	"unicode/utf16"
)

type Kind string

const (
	Game   Kind = "Game"
	Global Kind = "Global"
)

type Section struct {
	Name           string
	Offset, Length int
}

func (s Section) PayloadOffset() int { return s.Offset + 8 }

type Save struct {
	data                       []byte
	Kind                       Kind
	FormatVersion, GameVersion *uint32
	Sections                   []Section
	Checksum                   uint32
}
type dataError string

func (e dataError) Error() string { return string(e) }
func need(ok bool, text string) {
	if !ok {
		panic(dataError(text))
	}
}
func recoverError(err *error) {
	if p := recover(); p != nil {
		if e, ok := p.(dataError); ok {
			*err = e
		} else {
			panic(p)
		}
	}
}
func Parse(b []byte) (s *Save, err error) {
	defer recoverError(&err)
	need(len(b) >= 140, "The save is truncated.")
	index := 0
	kind := Global
	if string(b[:4]) != "EDNI" {
		need(len(b) >= 268 && string(b[128:132]) == "EDNI", "The Engage section index was not found.")
		index = 128
		kind = Game
	}
	end := len(b) - 4
	need(string(b[end-4:end]) == "LVRC", "The checksum footer is missing.")
	sum := get32(b, end)
	need(sum == crc32.ChecksumIEEE(b[:end]), "The save checksum does not match its contents.")
	offsets := []int{}
	ended := false
	for slot := 0; slot < 32; slot++ {
		off := int(get32(b, index+4+slot*4))
		if off == 0 {
			ended = true
			continue
		}
		need(!ended && off <= end-4, "The section index contains an invalid offset.")
		offsets = append(offsets, off)
	}
	need(len(offsets) >= 2 && offsets[0] == index+132 && offsets[len(offsets)-1] == end-4, "The section index does not cover the save body.")
	sections := []Section{}
	for i := 0; i < len(offsets)-1; i++ {
		start, stop := offsets[i], offsets[i+1]
		need(stop-start >= 8 && int(get32(b, start+4)) == stop-start-4, "A section has an invalid size or overlaps another section.")
		tag := []byte{b[start+3], b[start+2], b[start+1], b[start]}
		for _, v := range tag {
			need(v >= 32 && v <= 126, "A section tag is not a printable FourCC.")
		}
		sections = append(sections, Section{strings.TrimRight(string(tag), " "), start, stop - start - 8})
	}
	s = &Save{data: bytes.Clone(b), Kind: kind, Sections: sections, Checksum: sum}
	if kind == Game {
		v, g := get32(b, 0), get32(b, 4)
		s.FormatVersion = &v
		s.GameVersion = &g
	}
	return s, nil
}
func Load(path string) (*Save, error) {
	b, e := os.ReadFile(path)
	if e != nil {
		return nil, e
	}
	return Parse(b)
}
func (s *Save) Bytes() []byte { return bytes.Clone(s.data) }
func (s *Save) Length() int   { return len(s.data) }
func (s *Save) Section(name string) Section {
	found := []Section{}
	for _, sec := range s.Sections {
		if sec.Name == name {
			found = append(found, sec)
		}
	}
	need(len(found) == 1, "The save must contain exactly one "+name+" section.")
	return found[0]
}
func (s *Save) RequireGame() {
	need(s.Kind == Game && s.FormatVersion != nil && *s.FormatVersion == 9, "Editing requires an Engage format-version 9 game save.")
}
func (s *Save) replaceSection(sec Section, payload []byte) *Save {
	delta := len(payload) - sec.Length
	p := sec.PayloadOffset()
	b := make([]byte, 0, len(s.data)+delta)
	b = append(b, s.data[:p]...)
	b = append(b, payload...)
	b = append(b, s.data[p+sec.Length:]...)
	put32(b, sec.Offset+4, uint32(len(payload)+4))
	index := 4
	if s.Kind == Game {
		index = 132
	}
	for slot := 0; slot < 32; slot++ {
		pos := index + slot*4
		off := int(get32(b, pos))
		if off > sec.Offset {
			put32(b, pos, uint32(off+delta))
		}
	}
	return verified(b)
}
func verified(b []byte) *Save {
	put32(b, len(b)-4, crc32.ChecksumIEEE(b[:len(b)-4]))
	s, e := Parse(b)
	if e != nil {
		panic(dataError(e.Error()))
	}
	return s
}
func (s *Save) WriteCopy(path string) error {
	output, e := filepath.Abs(path)
	if e != nil {
		return e
	}
	if _, e = os.Stat(output); e == nil {
		return fmt.Errorf("the output file already exists; choose a new path")
	}
	f, e := os.CreateTemp(filepath.Dir(output), ".fee-*.tmp")
	if e != nil {
		return e
	}
	defer os.Remove(f.Name())
	if _, e = f.Write(s.data); e == nil {
		e = f.Sync()
	}
	ce := f.Close()
	if e != nil {
		return e
	}
	if ce != nil {
		return ce
	}
	persisted, e := os.ReadFile(f.Name())
	if e != nil {
		return e
	}
	if _, e = Parse(persisted); e != nil {
		return e
	}
	if !bytes.Equal(s.data, persisted) {
		return fmt.Errorf("the written save differs from the in-memory save")
	}
	return os.Link(f.Name(), output)
}
func get16(b []byte, p int) uint16    { return binary.LittleEndian.Uint16(b[p:]) }
func get32(b []byte, p int) uint32    { return binary.LittleEndian.Uint32(b[p:]) }
func signed32(b []byte, p int) int    { return int(int32(get32(b, p))) }
func put16(b []byte, p int, v uint16) { binary.LittleEndian.PutUint16(b[p:], v) }
func put32(b []byte, p int, v uint32) { binary.LittleEndian.PutUint32(b[p:], v) }
func wide(text string) []byte {
	us := utf16.Encode([]rune(text))
	b := make([]byte, 4+len(us)*2)
	put32(b, 0, uint32(len(us)*2))
	for i, u := range us {
		put16(b, 4+i*2, u)
	}
	return b
}

type reader struct {
	b        []byte
	pos, end int
}

func (r *reader) skip(n int) {
	need(n >= 0 && n <= r.end-r.pos, "A saved field extends beyond its record.")
	r.pos += n
}
func (r *reader) byte() byte  { p := r.pos; r.skip(1); return r.b[p] }
func (r *reader) u16() uint16 { p := r.pos; r.skip(2); return get16(r.b, p) }
func (r *reader) u32() uint32 { p := r.pos; r.skip(4); return get32(r.b, p) }
func (r *reader) ref() *uint32 {
	switch r.u16() {
	case 0xccdb:
		return nil
	case 0xefcd:
		v := r.u32()
		return &v
	}
	panic(dataError("Unsupported data-reference encoding."))
}
func (r *reader) str() *string {
	n := r.u32()
	if n == 0xffffffff {
		return nil
	}
	need(n <= 4096 && n%2 == 0, "Invalid saved string length.")
	p := r.pos
	r.skip(int(n))
	us := make([]uint16, n/2)
	for i := range us {
		us[i] = get16(r.b, p+i*2)
	}
	for i := 0; i < len(us); i++ {
		u := us[i]
		if u >= 0xd800 && u <= 0xdbff {
			need(i+1 < len(us) && us[i+1] >= 0xdc00 && us[i+1] <= 0xdfff, "Invalid saved UTF-16 string.")
			i++
		} else {
			need(!(u >= 0xdc00 && u <= 0xdfff), "Invalid saved UTF-16 string.")
		}
	}
	s := string(utf16.Decode(us))
	return &s
}
func text(p *string) string {
	if p == nil {
		return ""
	}
	return *p
}
