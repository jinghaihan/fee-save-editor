package catalog

type PersonDefinition struct {
	Id             string
	Names          Names
	LimitModifiers []int
	Gender         int
	BirthClass     string
}

func (p PersonDefinition) Hash() uint32            { return Hash(p.Id) }
func (p PersonDefinition) Name(lang string) string { return p.Names.Name(lang) }

type ClassDefinition struct {
	Id                       string
	Names                    Names
	MaxLevel                 int
	BaseStats, Limits        []int
	Flags                    int
	Advanced                 bool
	Promotion, LearningSkill string
	Weapons                  []int
}

func (c ClassDefinition) Hash() uint32            { return Hash(c.Id) }
func (c ClassDefinition) Name(lang string) string { return c.Names.Name(lang) }
func (c ClassDefinition) WeaponVariants() []uint32 {
	var mandatory uint32
	options := []uint32{}
	count := 0
	for i := 1; i < len(c.Weapons); i++ {
		switch c.Weapons[i] {
		case 1:
			mandatory |= 1 << i
		case 2, 3:
			options = append(options, 1<<i)
			count = c.Weapons[i] - 1
		}
	}
	if count == 0 {
		return []uint32{mandatory}
	}
	out := []uint32{}
	for i, m := range options {
		if count == 1 {
			out = append(out, mandatory|m)
		} else {
			for _, n := range options[i+1:] {
				out = append(out, mandatory|m|n)
			}
		}
	}
	return out
}

type ItemNameDefinition struct {
	Id         string
	Names      Names
	EngageOnly bool
}

func (i ItemNameDefinition) Name(lang string) string { return i.Names.Name(lang) }

type SkillDefinition struct {
	Id          string
	Names       Names
	Inheritable bool
	Family      string
	Tier        int
}

func (s SkillDefinition) Hash() uint32            { return Hash(s.Id) }
func (s SkillDefinition) Name(lang string) string { return s.Names.Name(lang) }

var rosterData struct {
	Persons   []PersonDefinition
	Classes   []ClassDefinition
	ItemNames []ItemNameDefinition
	Skills    []SkillDefinition
}
var Persons []PersonDefinition
var Classes []ClassDefinition
var Skills []SkillDefinition
var personsByHash = map[uint32]*PersonDefinition{}
var classesByHash = map[uint32]*ClassDefinition{}
var itemNamesByHash = map[uint32]*ItemNameDefinition{}
var skillsByHash = map[uint32]*SkillDefinition{}

func init() {
	if err := Decode("roster", &rosterData); err != nil {
		panic(err)
	}
	Persons = rosterData.Persons
	Classes = rosterData.Classes
	Skills = rosterData.Skills
	for i := range Persons {
		personsByHash[Persons[i].Hash()] = &Persons[i]
	}
	for i := range Classes {
		classesByHash[Classes[i].Hash()] = &Classes[i]
	}
	for i := range Skills {
		skillsByHash[Skills[i].Hash()] = &Skills[i]
	}
	for i := range rosterData.ItemNames {
		v := &rosterData.ItemNames[i]
		itemNamesByHash[Hash(v.Id)] = v
	}
}
func Person(hash uint32) *PersonDefinition       { return personsByHash[hash] }
func Class(hash uint32) *ClassDefinition         { return classesByHash[hash] }
func RosterItem(hash uint32) *ItemNameDefinition { return itemNamesByHash[hash] }
func Skill(hash uint32) *SkillDefinition         { return skillsByHash[hash] }
func ClassesFor(person uint32, gender int) []ClassDefinition {
	p := Person(person)
	if p == nil {
		return nil
	}
	birth := Class(Hash(p.BirthClass))
	promotion := ""
	if birth != nil {
		promotion = birth.Promotion
	}
	out := []ClassDefinition{}
	for _, c := range Classes {
		if c.Flags&1 != 0 && (c.Flags&2 != 0 || c.Id == p.BirthClass || c.Id == promotion) && (c.Flags&4 == 0 || gender == 2) {
			out = append(out, c)
		}
	}
	return out
}

type EngravingDefinition struct {
	Id                                          string
	Aliases                                     []string
	Power, Weight, Hit, Critical, Avoid, Secure int
}

func (e EngravingDefinition) Hash() uint32 { return Hash(e.Id) }

var Engravings []EngravingDefinition
var engravingByHash = map[uint32]*EngravingDefinition{}
var engravable = map[uint32]bool{}

func init() {
	var data struct {
		Engravings []EngravingDefinition
		Weapons    []string
	}
	if err := Decode("engravings", &data); err != nil {
		panic(err)
	}
	Engravings = data.Engravings
	for i := range Engravings {
		e := &Engravings[i]
		engravingByHash[e.Hash()] = e
		for _, id := range e.Aliases {
			engravingByHash[Hash(id)] = e
		}
	}
	for _, id := range data.Weapons {
		engravable[Hash(id)] = true
	}
}
func Engraving(hash uint32) *EngravingDefinition { return engravingByHash[hash] }
func CanEngrave(hash uint32) bool                { return engravable[hash] }
