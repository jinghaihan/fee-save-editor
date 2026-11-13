package save

import (
	"bytes"
	"hash/crc32"
	"math"
	"sort"
	"strings"
	"unicode"
)

type MainValues struct {
	Money, BondFragments, IronIngots, SteelIngots, SilverIngots int
	Difficulty, GameMode, SommieName                            string
}

var Difficulties = []string{"Normal", "Hard", "Maddening"}
var GameModes = []string{"Casual", "Classic"}

type variable struct {
	Key         *string
	Type        byte
	ValueOffset int
}
type variableLayout struct {
	Section                        Section
	Start, End, CountOffset, Count int
	Entries                        []variable
}

func (s *Save) variables() variableLayout {
	s.RequireGame()
	sec := s.Section("USER")
	r := reader{s.data, sec.PayloadOffset(), sec.PayloadOffset() + sec.Length}
	need(r.u32() == 20, "Unsupported USER version for game-variable editing.")
	r.skip(45)
	start := r.pos
	length := int(r.u32())
	need(length >= 40 && length <= r.end-start, "Invalid game-variable block length.")
	r.end = start + length
	need(r.u32() == 0, "Unsupported game-variable block version.")
	r.skip(28)
	countOffset := r.pos
	count := int(r.u32())
	need(count <= (r.end-r.pos)/9, "Invalid game-variable count.")
	v := variableLayout{sec, start, r.end, countOffset, count, []variable{}}
	for i := 0; i < count; i++ {
		key, typ := r.str(), r.byte()
		v.Entries = append(v.Entries, variable{key, typ, r.pos})
		switch typ {
		case 0:
			r.skip(4)
		case 1:
			r.str()
		default:
			panic(dataError("Unsupported game-variable type."))
		}
	}
	need(r.pos == r.end, "Unrecognized game-variable trailing data.")
	return v
}
func (v variableLayout) integerOffset(key string) int {
	offset := -1
	for _, e := range v.Entries {
		if e.Key != nil && *e.Key == key {
			need(e.Type == 0 && offset == -1, "Duplicated or noninteger game variable: "+key)
			offset = e.ValueOffset
		}
	}
	return offset
}

type GameVariable struct {
	Key   string
	Value int
}

func (s *Save) Variables() (out []GameVariable, err error) {
	defer recoverError(&err)
	seen := map[string]bool{}
	out = []GameVariable{}
	for _, v := range s.variables().Entries {
		if v.Key == nil {
			continue
		}
		need(!seen[*v.Key], "The game-variable block contains duplicate keys.")
		seen[*v.Key] = true
		if v.Type == 0 {
			out = append(out, GameVariable{*v.Key, signed32(s.data, v.ValueOffset)})
		}
	}
	return out, nil
}
func (s *Save) setIntegers(values map[string]int) *Save {
	keys := make([]string, 0, len(values))
	for key := range values {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	pairs := make([]GameVariable, 0, len(keys))
	for _, key := range keys {
		pairs = append(pairs, GameVariable{key, values[key]})
	}
	return s.setIntegerValues(pairs)
}
func (s *Save) setIntegerValues(values []GameVariable) *Save {
	v := s.variables()
	payload := bytes.Clone(s.data[v.Section.PayloadOffset() : v.Section.PayloadOffset()+v.Section.Length])
	add := []byte{}
	added := 0
	changed := false
	for _, pair := range values {
		key, value := pair.Key, pair.Value
		if p := v.integerOffset(key); p >= 0 {
			if signed32(s.data, p) == value {
				continue
			}
			put32(payload, p-v.Section.PayloadOffset(), uint32(value))
			changed = true
		} else if value != 0 {
			add = append(add, wide(key)...)
			add = append(add, 0)
			buf := make([]byte, 4)
			put32(buf, 0, uint32(value))
			add = append(add, buf...)
			added++
		}
	}
	if !changed && added == 0 {
		return s
	}
	if added > 0 {
		put32(payload, v.Start-v.Section.PayloadOffset(), uint32(v.End-v.Start+len(add)))
		put32(payload, v.CountOffset-v.Section.PayloadOffset(), uint32(v.Count+added))
		at := v.End - v.Section.PayloadOffset()
		payload = append(append(append([]byte{}, payload[:at]...), add...), payload[at:]...)
	}
	return s.replaceSection(v.Section, payload)
}
func (s *Save) WithVariable(key string, value int) (out *Save, err error) {
	defer recoverError(&err)
	values, e := s.Variables()
	if e != nil {
		return nil, e
	}
	found := false
	for _, v := range values {
		if v.Key == key {
			found = true
		}
	}
	need(strings.TrimSpace(key) != "" && found, "Select an existing numeric game variable.")
	return s.setIntegers(map[string]int{key: value}), nil
}

var materialKeys = []string{"G_所持_IID_てつの晶石", "G_所持_IID_はがねの晶石", "G_所持_IID_ぎんの晶石"}

type mainLayout struct {
	Section                                                      Section
	Values                                                       MainValues
	MoneyOffset, BondOffset, NameOffset, NameEnd, HeaderChecksum int
	MaterialOffsets                                              [3]int
}

func (s *Save) mainLayout() mainLayout {
	vars := s.variables()
	sec := vars.Section
	m := mainLayout{Section: sec}
	mode, diff := int(s.data[sec.Offset+45]), int(s.data[sec.Offset+46])
	need(mode < len(GameModes) && diff < len(Difficulties), "The save contains an unknown difficulty or game mode.")
	for i, key := range materialKeys {
		p := vars.integerOffset(key)
		need(p >= 0, "The game-variable block is incomplete.")
		m.MaterialOffsets[i] = p
	}
	r := reader{s.data, vars.End, sec.PayloadOffset() + sec.Length}
	m.MoneyOffset = r.pos
	money := int(int32(r.u32()))
	r.skip(16)
	r.str()
	m.BondOffset = r.pos
	bond := int(int32(r.u32()))
	r.skip(4)
	m.NameOffset = r.pos
	name := text(r.str())
	m.NameEnd = r.pos
	need(s.data[12] == 2 && s.data[17] == 2, "Unsupported chapter encoding in the save summary.")
	checks := []int{}
	for p := 26; p <= 124; p++ {
		if get32(s.data, p) == crc32.ChecksumIEEE(s.data[:p]) {
			checks = append(checks, p)
		}
	}
	need(len(checks) == 1, "The save summary checksum is invalid or ambiguous.")
	m.HeaderChecksum = checks[0]
	m.Values = MainValues{money, bond, signed32(s.data, m.MaterialOffsets[0]), signed32(s.data, m.MaterialOffsets[1]), signed32(s.data, m.MaterialOffsets[2]), Difficulties[diff], GameModes[mode], name}
	need(money >= 0 && bond >= 0 && m.Values.IronIngots >= 0 && m.Values.SteelIngots >= 0 && m.Values.SilverIngots >= 0, "A resource amount is outside the supported nonnegative range.")
	return m
}
func (s *Save) Main() (v MainValues, err error) {
	defer recoverError(&err)
	return s.mainLayout().Values, nil
}
func (s *Save) WithMain(v MainValues) (out *Save, err error) {
	defer recoverError(&err)
	for _, x := range []struct{ value, max int }{{v.Money, 9999999}, {v.BondFragments, 9999999}, {v.IronIngots, 9999}, {v.SteelIngots, 9999}, {v.SilverIngots, 9999}} {
		need(x.value >= 0 && x.value <= x.max, "A resource amount is outside its native limit.")
	}
	m := s.mainLayout()
	if m.Values == v {
		return s, nil
	}
	diff, mode := -1, -1
	for i, x := range Difficulties {
		if x == v.Difficulty {
			diff = i
		}
	}
	for i, x := range GameModes {
		if x == v.GameMode {
			mode = i
		}
	}
	need(diff >= 0 && mode >= 0, "Select a valid difficulty and game mode.")
	need(strings.TrimSpace(v.SommieName) != "" && !strings.ContainsFunc(v.SommieName, unicode.IsControl), "Sommie needs a nonempty name without control characters.")
	encoded := wide(v.SommieName)
	need(len(encoded) <= 4100, "The name exceeds the supported serialized string size.")
	b := s.Bytes()
	put32(b, m.MoneyOffset, uint32(v.Money))
	put32(b, m.BondOffset, uint32(v.BondFragments))
	for i, value := range []int{v.IronIngots, v.SteelIngots, v.SilverIngots} {
		put32(b, m.MaterialOffsets[i], uint32(value))
	}
	b[m.Section.Offset+45] = byte(mode)
	b[m.Section.Offset+46] = byte(diff)
	b[23] = byte(diff)
	b[25] = byte(mode)
	put32(b, m.HeaderChecksum, crc32.ChecksumIEEE(b[:m.HeaderChecksum]))
	sec := m.Section
	payload := append(append(append([]byte{}, b[sec.PayloadOffset():m.NameOffset]...), encoded...), b[m.NameEnd:sec.PayloadOffset()+sec.Length]...)
	base := *s
	base.data = b
	out = base.replaceSection(sec, payload)
	need(out.mainLayout().Values == v, "The edited Main values did not survive serialization.")
	return out, nil
}
func (s *Save) playTimeSection() Section {
	s.RequireGame()
	sec := s.Section("TIME")
	need(sec.Length == 40 && get32(s.data, sec.PayloadOffset()) == 0, "The save does not contain a supported TIME section.")
	return sec
}
func (s *Save) PlayTime() (v float32, err error) {
	defer recoverError(&err)
	sec := s.playTimeSection()
	v = math.Float32frombits(get32(s.data, sec.PayloadOffset()+32))
	need(!math.IsNaN(float64(v)) && !math.IsInf(float64(v), 0) && v >= 0 && v <= 3599999.5, "The saved play time is outside the game's supported range.")
	return v, nil
}
func (s *Save) WithPlayTime(seconds float32) (out *Save, err error) {
	defer recoverError(&err)
	need(!math.IsNaN(float64(seconds)) && !math.IsInf(float64(seconds), 0) && seconds >= 0 && seconds <= 3599999.5, "Play time must be between 0 and 3,599,999.5 seconds.")
	sec := s.playTimeSection()
	m := s.mainLayout()
	need(m.HeaderChecksum >= 44, "The save summary does not contain a supported play-time field.")
	b := s.Bytes()
	put32(b, sec.PayloadOffset()+32, math.Float32bits(seconds))
	put32(b, 40, math.Float32bits(seconds))
	put32(b, m.HeaderChecksum, crc32.ChecksumIEEE(b[:m.HeaderChecksum]))
	return verified(b), nil
}
