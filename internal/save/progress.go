package save

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

func (s *Save) integer(v variableLayout, key string) int {
	p := v.integerOffset(key)
	if p < 0 {
		return 0
	}
	return signed32(s.data, p)
}

type QuantityItem struct {
	Definition catalog.QuantityDefinition
	Amount     int
}

func (s *Save) Quantities() (out []QuantityItem, err error) {
	defer recoverError(&err)
	v := s.variables()
	out = []QuantityItem{}
	for _, d := range catalog.QuantityItems {
		n := s.integer(v, d.Key())
		need(n >= 0, "Negative quantity for "+d.Id)
		out = append(out, QuantityItem{d, n})
	}
	return out, nil
}
func (s *Save) WithQuantity(id string, n int) (out *Save, err error) {
	defer recoverError(&err)
	for _, d := range catalog.QuantityItems {
		if d.Id == id {
			need(n >= 0 && n <= d.Maximum, "Amount is outside the item's native limit.")
			return s.setIntegerValues([]GameVariable{{d.Key(), n}}), nil
		}
	}
	return nil, fmt.Errorf("unknown quantity item: %s", id)
}
func (s *Save) FillQuantities(category string) (out *Save, err error) {
	defer recoverError(&err)
	need(category == "ReclassItems" || category == "Materials" || category == "Ingredients" || category == "Gifts", "Choose reclass items, materials, ingredients or gifts.")
	pairs := []GameVariable{}
	for _, d := range catalog.QuantityItems {
		if d.Category == category {
			pairs = append(pairs, GameVariable{d.Key(), d.Maximum})
		}
	}
	return s.setIntegerValues(pairs), nil
}

type Animal struct {
	Definition catalog.AnimalDefinition
	Amount     int
}

func (s *Save) Animals() (out []Animal, err error) {
	defer recoverError(&err)
	v := s.variables()
	out = []Animal{}
	for _, d := range catalog.Animals {
		n := s.integer(v, d.Key())
		need(n >= 0, "Negative animal count for "+d.Id)
		out = append(out, Animal{d, n})
	}
	return out, nil
}
func (s *Save) WithAnimal(id string, n int, all bool) (out *Save, err error) {
	defer recoverError(&err)
	need(n >= 0 && n <= 99, "Animal count must be 0–99.")
	pairs := []GameVariable{}
	for _, d := range catalog.Animals {
		if all || d.Id == id {
			pairs = append(pairs, GameVariable{d.Key(), n})
		}
	}
	need(len(pairs) > 0, "Unknown animal.")
	return s.setIntegerValues(pairs), nil
}

type AmiiboReward struct {
	Definition catalog.AmiiboDefinition
	Value      int
	Unlocked   bool
}

func (s *Save) Amiibo() (out []AmiiboReward, err error) {
	defer recoverError(&err)
	v := s.variables()
	out = []AmiiboReward{}
	for _, d := range catalog.AmiiboRewards {
		n := s.integer(v, d.Key)
		out = append(out, AmiiboReward{d, n, n == 1})
	}
	return out, nil
}
func (s *Save) UnlockAmiibo(keys []string) (out *Save, err error) {
	defer recoverError(&err)
	if _, e := s.Amiibo(); e != nil {
		return nil, e
	}
	pairs := []GameVariable{}
	seen := map[string]bool{}
	for _, key := range keys {
		if seen[key] {
			continue
		}
		found := false
		for _, d := range catalog.AmiiboRewards {
			if d.Key == key {
				found = true
			}
		}
		need(found, "Unknown amiibo reward.")
		seen[key] = true
		pairs = append(pairs, GameVariable{key, 1})
	}
	return s.setIntegerValues(pairs), nil
}

type Donation struct {
	Country       catalog.DonationCountry
	Amount, Level int
}

func (s *Save) Donations() (out []Donation, err error) {
	defer recoverError(&err)
	v := s.variables()
	out = []Donation{}
	for _, d := range catalog.DonationCountries {
		n := s.integer(v, d.Key)
		need(n >= 0 && n <= 9999999, "Donated gold must be between 0 and 9,999,999.")
		out = append(out, Donation{d, n, d.Level(n)})
	}
	return out, nil
}
func (s *Save) WithDonation(id string, amount int) (out *Save, err error) {
	defer recoverError(&err)
	if _, e := s.Donations(); e != nil {
		return nil, e
	}
	need(amount >= 0 && amount <= 9999999, "Donated gold must be between 0 and 9,999,999.")
	for _, d := range catalog.DonationCountries {
		if d.Id == id {
			return s.setIntegerValues([]GameVariable{{d.Key, amount}}), nil
		}
	}
	return nil, fmt.Errorf("select one of the four donation countries")
}

type Achievement struct {
	Definition                               catalog.AchievementDefinition
	Status                                   int
	Achieved, RewardClaimed, RewardAvailable bool
}

func (s *Save) Achievements() (out []Achievement, err error) {
	defer recoverError(&err)
	v := s.variables()
	out = []Achievement{}
	for _, d := range catalog.Achievements {
		n := s.integer(v, d.Key())
		need(n >= 0 && n <= 3, "Invalid achievement status: "+d.Id)
		out = append(out, Achievement{d, n, n != 0, n == 3, n == 1 || n == 2})
	}
	return out, nil
}
func (s *Save) UnlockAchievements(id string) (out *Save, err error) {
	defer recoverError(&err)
	rows, e := s.Achievements()
	if e != nil {
		return nil, e
	}
	pairs := []GameVariable{}
	found := id == ""
	for _, r := range rows {
		if id == "" || r.Definition.Id == id {
			found = true
			if !r.Achieved {
				pairs = append(pairs, GameVariable{r.Definition.Key(), 1})
			}
		}
	}
	need(found, "Select an ID from the achievement catalog.")
	return s.setIntegerValues(pairs), nil
}

type ActivityValues struct{ TrainingRemaining, ArenaRemaining int }

func (s *Save) Activities() (out ActivityValues, err error) {
	defer recoverError(&err)
	m := s.mainLayout()
	tr, ar := signed32(s.data, m.MoneyOffset+8), signed32(s.data, m.MoneyOffset+12)
	need(tr >= 0 && tr <= 1 && ar >= 0 && ar <= 3, "The saved activity counters are outside the native game's limits.")
	return ActivityValues{1 - tr, 3 - ar}, nil
}
func (s *Save) WithActivities(v ActivityValues) (out *Save, err error) {
	defer recoverError(&err)
	need(v.TrainingRemaining >= 0 && v.TrainingRemaining <= 1 && v.ArenaRemaining >= 0 && v.ArenaRemaining <= 3, "Remaining strength-training uses must be 0–1 and standard Arena uses 0–3.")
	if _, e := s.Activities(); e != nil {
		return nil, e
	}
	m := s.mainLayout()
	sec := m.Section
	p := bytes.Clone(s.data[sec.PayloadOffset() : sec.PayloadOffset()+sec.Length])
	at := m.MoneyOffset - sec.PayloadOffset()
	put32(p, at+8, uint32(1-v.TrainingRemaining))
	put32(p, at+12, uint32(3-v.ArenaRemaining))
	return s.replaceSection(sec, p), nil
}

type MinigameValues struct {
	Value          int
	Rank, BestSize *int
}
type MinigameRecord struct {
	Definition     catalog.MinigameDefinition
	Value          int
	Rank, BestSize *int
}

func (s *Save) MinigameRecords() (out []MinigameRecord, err error) {
	defer recoverError(&err)
	v := s.variables()
	read := func(key string) *int {
		if key == "" {
			return nil
		}
		n := s.integer(v, key)
		need(n >= 0, "Negative minigame record: "+key)
		return &n
	}
	out = []MinigameRecord{}
	for _, group := range catalog.Minigames {
		for _, d := range group.Records {
			n := read(d.Key)
			rank, size := read(d.RankKey()), read(d.SizeKey())
			if rank != nil {
				need(*rank < len(d.Ranks()), "Invalid minigame rank: "+d.Key)
			}
			out = append(out, MinigameRecord{d, *n, rank, size})
		}
	}
	return out, nil
}
func (s *Save) WithMinigame(key string, values MinigameValues) (out *Save, err error) {
	defer recoverError(&err)
	rows, e := s.MinigameRecords()
	if e != nil {
		return nil, e
	}
	need(values.Value >= 0, "Minigame values must be nonnegative.")
	for _, r := range rows {
		if r.Definition.Key != key {
			continue
		}
		pairs := []GameVariable{{key, values.Value}}
		if values.Rank != nil {
			need(r.Definition.RankKey() != "" && *values.Rank >= 0 && *values.Rank < len(r.Definition.Ranks()), "This record has no valid stored rank.")
			pairs = append(pairs, GameVariable{r.Definition.RankKey(), *values.Rank})
		}
		if values.BestSize != nil {
			need(r.Definition.SizeKey() != "" && *values.BestSize >= 0, "Only fishing records store a nonnegative best size.")
			pairs = append(pairs, GameVariable{r.Definition.SizeKey(), *values.BestSize})
		}
		return s.setIntegerValues(pairs), nil
	}
	return nil, fmt.Errorf("unknown minigame record: %s", key)
}

type RelayTickets struct {
	Identifier    uint64
	Amount        int
	LastTime      int64
	IdentifierHex string
}

func (s *Save) Tickets() (out []RelayTickets, err error) {
	defer recoverError(&err)
	need(s.Kind == Global, "Relay Tickets belong to the Global save; open Global, not Manual or Auto.")
	sec := s.Section("RLGL")
	r := reader{s.data, sec.PayloadOffset(), sec.PayloadOffset() + sec.Length}
	need(r.u32() == 1, "Unsupported RLGL version.")
	r.skip(32)
	n := int(r.u32())
	need(n <= (r.end-r.pos)/20 && r.end-r.pos == n*20, "Invalid Relay Ticket record count.")
	seen := map[uint64]bool{}
	out = []RelayTickets{}
	for i := 0; i < n; i++ {
		p := r.pos
		r.skip(20)
		id := binary.LittleEndian.Uint64(s.data[p:])
		amount := get32(s.data, p+8)
		need(!seen[id] && amount <= 0x7fffffff, "Invalid or duplicated Relay Ticket record.")
		seen[id] = true
		out = append(out, RelayTickets{id, int(amount), int64(binary.LittleEndian.Uint64(s.data[p+12:])), fmt.Sprintf("%016X", id)})
	}
	return out, nil
}
func (s *Save) WithTickets(id uint64, amount int) (out *Save, err error) {
	defer recoverError(&err)
	need(amount >= 0 && amount <= 999, "Relay Ticket count must be 0–999.")
	rows, e := s.Tickets()
	if e != nil {
		return nil, e
	}
	for i, r := range rows {
		if r.Identifier != id {
			continue
		}
		if r.Amount == amount {
			return s, nil
		}
		sec := s.Section("RLGL")
		p := bytes.Clone(s.data[sec.PayloadOffset() : sec.PayloadOffset()+sec.Length])
		put32(p, 48+i*20, uint32(amount))
		return s.replaceSection(sec, p), nil
	}
	return nil, fmt.Errorf("this campaign identifier is not registered in Global")
}
