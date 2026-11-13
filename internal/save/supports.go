package save

import (
	"bytes"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

type CharacterSupport struct {
	Key                 string
	Rank, Points, Score int
}
type supportLocation struct {
	support CharacterSupport
	offset  int
}
type supportLayout struct {
	section Section
	entries []supportLocation
}

func (s *Save) supports() supportLayout {
	sec, r := s.pool("UREL", 1)
	count := int(r.u32())
	need(count <= 10000 && count <= (r.end-r.pos)/11, "Invalid character support count.")
	l := supportLayout{section: sec, entries: []supportLocation{}}
	keys := map[string]bool{}
	for range count {
		key := r.str()
		need(key != nil, "A character support has no key.")
		canonical := *key
		if d := catalog.SupportPair(*key); d != nil {
			canonical = d.Key()
		}
		need(!keys[canonical] && r.u32() == 1, "Duplicate character support or unsupported record version.")
		keys[canonical] = true
		offset := r.pos
		rank := int(r.byte())
		need(rank <= 4, "Unsupported character support rank.")
		l.entries = append(l.entries, supportLocation{CharacterSupport{*key, rank, int(int8(r.byte())), int(int8(r.byte()))}, offset})
	}
	need(r.pos == r.end, "Unrecognized character support trailing data.")
	return l
}
func (s *Save) Supports() (out []CharacterSupport, err error) {
	defer recoverError(&err)
	out = []CharacterSupport{}
	for _, v := range s.supports().entries {
		out = append(out, v.support)
	}
	return out, nil
}
func (s *Save) withAlearSupport(person string, level int) *Save {
	if person == "PID_リュール" {
		return s
	}
	rank, ok := map[int]int{1: 0, 5: 1, 10: 2, 20: 3, 21: 4}[level]
	need(ok, "Alear's bond must correspond to a support rank.")
	l := s.supports()
	var location *supportLocation
	for _, v := range l.entries {
		if v.support.Key == "PID_リュール"+person || v.support.Key == person+"PID_リュール" {
			copy := v
			location = &copy
		}
	}
	need(location != nil, "The character has no saved support with Alear.")
	need(location.support.Rank != 4 || rank == 4, "A Pact support cannot be downgraded through an ordinary bond edit.")
	if location.support.Rank == rank {
		return s
	}
	pair := catalog.SupportPair(location.support.Key)
	need(pair != nil, "Unverified Alear support pair.")
	b := bytes.Clone(s.data)
	b[location.offset] = byte(rank)
	b[location.offset+1] = byte(pair.PointsForRank(rank))
	return verified(b)
}
func (s *Save) maximumSupportRank(key string) int {
	pair := catalog.SupportPair(key)
	need(pair != nil, "The support pair is not in the verified catalog.")
	if !pair.IncludesAlear() {
		return 3
	}
	has := false
	for _, sec := range s.Sections {
		has = has || sec.Name == "GDBD"
	}
	if !has {
		return 3
	}
	for _, v := range s.emblems().emblems {
		if v.EmblemId == "GID_リュール" && v.PactPartner != nil && *v.PactPartner == pair.OtherPersonId() {
			return 4
		}
	}
	return 3
}
func (s *Save) MaximumSupportRank(key string) (value int, err error) {
	defer recoverError(&err)
	return s.maximumSupportRank(key), nil
}
func (s *Save) WithSupport(key string, rank int, points *int) (out *Save, err error) {
	defer recoverError(&err)
	l := s.supports()
	pair := catalog.SupportPair(key)
	need(pair != nil, "Select a verified support pair.")
	need(rank >= 0 && rank <= s.maximumSupportRank(key), "The rank exceeds this pair's legal maximum.")
	var loc *supportLocation
	for _, v := range l.entries {
		p := catalog.SupportPair(v.support.Key)
		if p != nil && p.Key() == pair.Key() {
			copy := v
			loc = &copy
		}
	}
	need(loc != nil, "The pair has no saved support; missing relationships are not added.")
	value := pair.PointsForRank(rank)
	if points != nil {
		value = *points
	}
	need(value >= 0 && value <= pair.MaximumPoints(rank), "Support points are outside this rank's native limits.")
	need(loc.support.Rank != 4 || rank == 4, "A Pact support cannot be downgraded or reassigned.")
	changed := loc.support.Rank != rank
	edited := s
	if changed || loc.support.Points != value {
		b := bytes.Clone(s.data)
		b[loc.offset] = byte(rank)
		b[loc.offset+1] = byte(value)
		edited = verified(b)
	}
	hasBonds := false
	for _, sec := range s.Sections {
		hasBonds = hasBonds || sec.Name == "GDBD"
	}
	if changed && pair.IncludesAlear() && hasBonds {
		for _, emblem := range edited.emblems().emblems {
			if emblem.EmblemId != "GID_リュール" {
				continue
			}
			for _, bond := range emblem.Bonds {
				if bond.PersonId == pair.OtherPersonId() {
					return edited.WithEmblemBond(emblem.InstanceId, bond.PersonId, []int{1, 5, 10, 20, 21}[rank], nil)
				}
			}
		}
	}
	return edited, nil
}
func (s *Save) MaximizeSupports(key string) (out *Save, err error) {
	defer recoverError(&err)
	entries, e := s.Supports()
	if e != nil {
		return nil, e
	}
	if key != "" {
		pair := catalog.SupportPair(key)
		need(pair != nil, "Select a verified support pair.")
		for _, v := range entries {
			p := catalog.SupportPair(v.Key)
			if p != nil && p.Key() == pair.Key() {
				maximum := s.maximumSupportRank(key)
				if v.Rank == maximum {
					return s, nil
				}
				return s.WithSupport(v.Key, maximum, nil)
			}
		}
		panic(dataError("Select an existing support pair."))
	}
	edited := s
	for _, v := range entries {
		if catalog.SupportPair(v.Key) == nil {
			continue
		}
		maximum := edited.maximumSupportRank(v.Key)
		if v.Rank < maximum {
			edited, e = edited.WithSupport(v.Key, maximum, nil)
			if e != nil {
				return nil, e
			}
		}
	}
	return edited, nil
}
