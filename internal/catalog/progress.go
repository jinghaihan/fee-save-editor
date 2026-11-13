package catalog

import "strings"

type Names map[string]string

func (n Names) Name(language string) string {
	if s := n[language]; s != "" {
		return s
	}
	return n["en"]
}

type QuantityDefinition struct {
	Id       string
	Names    Names
	Category string
	Maximum  int
}

func (q QuantityDefinition) Key() string { return "G_所持_" + q.Id }

var QuantityItems = func() []QuantityDefinition {
	var d struct{ Items []QuantityDefinition }
	if e := Decode("quantity-items", &d); e != nil {
		panic(e)
	}
	return d.Items
}()

type AnimalDefinition struct {
	Id    string
	Names Names
}

func (a AnimalDefinition) Key() string { return "G_動物_" + a.Id }

var Animals = func() []AnimalDefinition {
	var d struct{ Animals []AnimalDefinition }
	if e := Decode("animals", &d); e != nil {
		panic(e)
	}
	return d.Animals
}()

type AmiiboDefinition struct {
	Key, Group string
	Names      Names
}

var AmiiboRewards = func() []AmiiboDefinition {
	var d struct{ Rewards []AmiiboDefinition }
	if e := Decode("amiibo", &d); e != nil {
		panic(e)
	}
	return d.Rewards
}()

type DonationCountry struct {
	Id, Key    string
	Names      Names
	Thresholds []int
}

func (d DonationCountry) Level(amount int) int {
	n := 0
	for _, t := range d.Thresholds {
		if t <= amount {
			n++
		}
	}
	return n
}

var DonationCountries = func() []DonationCountry {
	var d struct{ Countries []DonationCountry }
	if e := Decode("donations", &d); e != nil {
		panic(e)
	}
	return d.Countries
}()

type AchievementDefinition struct {
	Id       string
	Category int
	Names    Names
	Reward   int
	Chapter  string
}

func (a AchievementDefinition) Key() string { return "G_実績_" + a.Id[4:] }

var Achievements = func() []AchievementDefinition {
	var d struct{ Achievements []AchievementDefinition }
	if e := Decode("achievements", &d); e != nil {
		panic(e)
	}
	return d.Achievements
}()

type MinigameDefinition struct {
	Key   string
	Names Names
}

func (m MinigameDefinition) Fishing() bool { return strings.HasPrefix(m.Key, "G_Fishing_") }
func (m MinigameDefinition) RankKey() string {
	if m.Fishing() {
		return m.Key[:len(m.Key)-6] + "_BestRank"
	}
	if strings.HasPrefix(m.Key, "G_DragonRide") {
		return m.Key[:len(m.Key)-5] + "RankNum"
	}
	return ""
}
func (m MinigameDefinition) SizeKey() string {
	if m.Fishing() {
		return m.Key[:len(m.Key)-6] + "_BestSize"
	}
	return ""
}
func (m MinigameDefinition) Ranks() []string {
	if m.Fishing() {
		return []string{"Tiny", "Small", "Middle", "Large", "Big", "Giant"}
	}
	if m.RankKey() != "" {
		return []string{"None", "SSS", "SS", "S", "A", "B", "C", "D", "E", "F"}
	}
	return []string{}
}

type MinigameGroup struct {
	Id      string
	Names   Names
	Records []MinigameDefinition
}

var Minigames = func() []MinigameGroup {
	var d struct{ Groups []MinigameGroup }
	if e := Decode("minigames", &d); e != nil {
		panic(e)
	}
	return d.Groups
}()
