package catalog

import "strings"

type EmblemDefinition struct {
	Id               string
	Names            Names
	LevelCapVariable *string
	Dlc              bool
}

func (d EmblemDefinition) Name(lang string) string { return d.Names.Name(lang) }

type BondRingSkill struct {
	Id                  string
	Names, Descriptions Names
}

func (d BondRingSkill) Name(lang string) string { return d.Names.Name(lang) }

type BondRingDefinition struct {
	Id          string
	Names       Names
	EmblemId    string
	Rank        int
	SingleRank  bool
	StatBonuses []int
	Skills      []BondRingSkill
}

func (d BondRingDefinition) Hash() uint32            { return Hash(d.Id) }
func (d BondRingDefinition) Name(lang string) string { return d.Names.Name(lang) }
func (d BondRingDefinition) Group() string           { return d.Id[5 : len(d.Id)-2] }
func (d BondRingDefinition) AcquisitionKey() string  { return "G_指輪_" + d.Group() }
func (d BondRingDefinition) RankName() string        { return []string{"C", "B", "A", "S"}[d.Rank] }

var Emblems []EmblemDefinition
var BondRings []BondRingDefinition
var BondExperience []int
var emblemsByID = map[string]*EmblemDefinition{}
var bondRingsByHash = map[uint32]*BondRingDefinition{}

func init() {
	var d struct {
		Emblems        []EmblemDefinition
		Rings          []BondRingDefinition
		BondExperience []int
	}
	if e := Decode("emblems", &d); e != nil {
		panic(e)
	}
	Emblems = d.Emblems
	BondRings = d.Rings
	BondExperience = d.BondExperience
	for i := range Emblems {
		v := &Emblems[i]
		emblemsByID[v.Id] = v
	}
	for i := range BondRings {
		v := &BondRings[i]
		bondRingsByHash[v.Hash()] = v
	}
}
func Emblem(id string) *EmblemDefinition       { return emblemsByID[id] }
func BondRing(hash uint32) *BondRingDefinition { return bondRingsByHash[hash] }

type SupportDefinition struct {
	FirstPersonId, SecondPersonId string
	Thresholds                    []int
}

func (d SupportDefinition) Key() string { return d.FirstPersonId + d.SecondPersonId }
func (d SupportDefinition) IncludesAlear() bool {
	return d.FirstPersonId == "PID_リュール" || d.SecondPersonId == "PID_リュール"
}
func (d SupportDefinition) OtherPersonId() string {
	if d.FirstPersonId == "PID_リュール" {
		return d.SecondPersonId
	}
	return d.FirstPersonId
}
func (d SupportDefinition) PointsForRank(rank int) int {
	if rank == 0 {
		return 0
	}
	if rank == 4 {
		return 99
	}
	return d.Thresholds[rank-1]
}
func (d SupportDefinition) MaximumPoints(rank int) int {
	if rank >= 3 {
		return 99
	}
	return d.Thresholds[rank] - 1
}

var SupportPairs []SupportDefinition
var supportByKey = map[string]*SupportDefinition{}

func init() {
	var d struct{ Pairs []SupportDefinition }
	if e := Decode("supports", &d); e != nil {
		panic(e)
	}
	SupportPairs = d.Pairs
	for i := range SupportPairs {
		v := &SupportPairs[i]
		supportByKey[v.Key()] = v
		supportByKey[v.SecondPersonId+v.FirstPersonId] = v
	}
}
func SupportPair(key string) *SupportDefinition { return supportByKey[key] }

type MeldingRule struct{ SourceRank, RequiredRings, BondFragments int }

var MeldingRules []MeldingRule

func init() {
	var d struct{ Rules []MeldingRule }
	if e := Decode("bond-ring-melding", &d); e != nil {
		panic(e)
	}
	MeldingRules = d.Rules
}
func MeldResult(source BondRingDefinition) *BondRingDefinition {
	suffix := []string{"B", "A", "S"}
	if source.SingleRank || source.Rank >= 3 {
		return nil
	}
	id := strings.TrimSuffix(source.Id, "_"+source.RankName()) + "_" + suffix[source.Rank]
	return BondRing(Hash(id))
}
