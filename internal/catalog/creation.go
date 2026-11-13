package catalog

type RosterCreationDefaults struct {
	Id                                   string
	InternalLevel, SkillPoints           int
	OriginalProficiencies, Proficiencies uint32
	PersonalStats                        [][]int
}

var RosterDefaults map[string]RosterCreationDefaults
var EmblemWeapons map[string][]string

func init() {
	var people struct{ Persons []RosterCreationDefaults }
	if e := Decode("roster-creation", &people); e != nil {
		panic(e)
	}
	RosterDefaults = map[string]RosterCreationDefaults{}
	for _, v := range people.Persons {
		RosterDefaults[v.Id] = v
	}
	var gods struct {
		Emblems []struct {
			Id      string
			Weapons []string
		}
	}
	if e := Decode("emblem-creation", &gods); e != nil {
		panic(e)
	}
	EmblemWeapons = map[string][]string{}
	for _, v := range gods.Emblems {
		EmblemWeapons[v.Id] = v.Weapons
	}
}
