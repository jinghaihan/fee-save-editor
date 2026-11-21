package save

import (
	"slices"

	"github.com/jinghaihan/fee-save-editor/internal/catalog"
)

func (s *Save) rosterOrder() (rosterLayout, []int) {
	l := s.roster()
	s.validateOutOfBattle(l)
	order := []int{}
	seen := map[uint32]bool{}
	for _, c := range l.characters {
		if c.character.Force != Absent || catalog.Person(c.character.PersonHash) == nil {
			continue
		}
		need(!seen[c.character.PersonHash], "Duplicate characters cannot be reordered.")
		seen[c.character.PersonHash] = true
		order = append(order, c.character.Index)
	}
	return l, order
}

// RosterOrder lists the movable character indices in their saved order.
// Dead, lost, unknown and temporary units keep their original records and positions.
func (s *Save) RosterOrder() (order []int, err error) {
	defer recoverError(&err)
	_, order = s.rosterOrder()
	return
}

// WithRosterOrder permutes complete character records within the bench force.
// Indices refer to the input save; no character data or other force is rewritten.
func (s *Save) WithRosterOrder(order []int) (out *Save, err error) {
	defer recoverError(&err)
	l, current := s.rosterOrder()
	need(len(order) == len(current), "The roster order must include every available character exactly once.")
	positions := map[int]int{}
	for i, index := range current {
		positions[index] = i
	}
	seen := map[int]bool{}
	for _, index := range order {
		_, valid := positions[index]
		need(valid && !seen[index], "The roster order contains an invalid or duplicate character index.")
		seen[index] = true
	}
	if slices.Equal(current, order) {
		return s, nil
	}
	sec := l.section
	p, cursor := sec.PayloadOffset(), sec.PayloadOffset()
	payload := make([]byte, 0, sec.Length)
	for _, c := range l.characters {
		payload = append(payload, s.data[cursor:c.start]...)
		record := c
		if position, movable := positions[c.character.Index]; movable {
			record = l.get(order[position])
		}
		payload = append(payload, s.data[record.start:record.end]...)
		cursor = c.end
	}
	payload = append(payload, s.data[cursor:p+sec.Length]...)
	return s.replaceSection(sec, payload), nil
}

// MoveRosterCharacter uses source and destination indices from the input Roster.
func (s *Save) MoveRosterCharacter(index, to int) (out *Save, err error) {
	defer recoverError(&err)
	_, order := s.rosterOrder()
	from, destination := slices.Index(order, index), slices.Index(order, to)
	need(from >= 0 && destination >= 0, "Choose available characters in an out-of-battle save.")
	order = slices.Delete(order, from, from+1)
	order = slices.Insert(order, destination, index)
	return s.WithRosterOrder(order)
}

func (s *Save) SortRosterByRecruitment() (out *Save, err error) {
	defer recoverError(&err)
	l, order := s.rosterOrder()
	ranks := map[uint32]int{}
	for i, id := range catalog.RecruitmentOrder {
		ranks[catalog.Hash(id)] = i
	}
	slices.SortStableFunc(order, func(a, b int) int {
		return ranks[l.get(a).character.PersonHash] - ranks[l.get(b).character.PersonHash]
	})
	return s.WithRosterOrder(order)
}
