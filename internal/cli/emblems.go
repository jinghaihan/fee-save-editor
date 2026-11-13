package cli

import (
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"io"
	"slices"
)

func runEmblems(args []string, out io.Writer) {
	check(len(args) > 0, "Provide an Emblems command.")
	verb := args[0]
	if verb == "catalog" {
		lang := language(args[1:])
		emblems, rings := []object{}, []object{}
		for _, d := range catalog.Emblems {
			emblems = append(emblems, object{"Id": d.Id, "Name": d.Name(lang), "Dlc": d.Dlc, "CanAdd": catalog.EmblemWeapons[d.Id] != nil})
		}
		for _, d := range catalog.BondRings {
			bonuses, skills := []object{}, []object{}
			for i, value := range d.StatBonuses {
				if value != 0 {
					bonuses = append(bonuses, object{"Stat": save.StatNames[i], "Value": value})
				}
			}
			for _, skill := range d.Skills {
				skills = append(skills, object{"Id": skill.Id, "Name": skill.Name(lang), "Description": skill.Descriptions.Name(lang)})
			}
			var melding any
			if result := catalog.MeldResult(d); result != nil {
				for _, rule := range catalog.MeldingRules {
					if rule.SourceRank == d.Rank {
						melding = object{"ResultId": result.Id, "ResultRank": result.RankName(), "RequiredRings": rule.RequiredRings, "BondFragments": rule.BondFragments}
					}
				}
			}
			rings = append(rings, object{"Id": d.Id, "Name": d.Name(lang), "Rank": d.RankName(), "MaxStock": 99, "StatBonuses": bonuses, "Skills": skills, "Melding": melding})
		}
		outputJSON(out, object{"Emblems": emblems, "Rings": rings})
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	if slices.Contains([]string{"list", "rings", "missing", "conditions"}, verb) {
		lang := language(args[2:])
		rows := []object{}
		switch verb {
		case "list":
			for _, v := range take(s.Emblems()) {
				name := v.EmblemId
				def := catalog.Emblem(v.EmblemId)
				if def != nil {
					name = def.Name(lang)
				}
				bonds := []object{}
				for _, bond := range v.Bonds {
					name := bond.PersonId
					if person := catalog.Person(catalog.Hash(bond.PersonId)); person != nil {
						name = person.Name(lang)
					}
					var maximum *int
					if def != nil {
						n := save.MaximumBondLevel(v, bond.PersonId)
						maximum = &n
					}
					bonds = append(bonds, object{"PersonId": bond.PersonId, "Name": name, "Level": bond.Level, "Experience": bond.Experience, "InheritedSkills": bond.InheritedSkills, "TalkFlags": int(bond.TalkFlags), "MaximumLevel": maximum})
				}
				rows = append(rows, object{"InstanceId": v.InstanceId, "Id": v.EmblemId, "Name": name, "PactPartner": v.PactPartner, "Bonds": bonds})
			}
		case "conditions":
			for _, v := range take(s.EmblemConditions()) {
				rows = append(rows, object{"InstanceId": v.InstanceId, "BondHolderId": v.BondHolderId, "EmblemId": v.EmblemId, "Name": catalog.Emblem(v.EmblemId).Name(lang), "Dirtiness": v.Dirtiness, "MaximumDirtiness": 255})
			}
		case "missing":
			for _, v := range take(s.MissingEmblems()) {
				rows = append(rows, object{"Id": v.Id, "Name": v.Name(lang), "Dlc": v.Dlc})
			}
		case "rings":
			for _, v := range take(s.BondRings()) {
				var id, rank *string
				var maximum *int
				name := fmt.Sprintf("Unknown ring (0x%08X)", v.RingHash)
				if def := catalog.BondRing(v.RingHash); def != nil {
					i, r := def.Id, def.RankName()
					id, rank = &i, &r
					name = def.Name(lang)
					n := take(s.MaximumBondRingStock(v.InstanceId))
					maximum = &n
				}
				rows = append(rows, object{"InstanceId": v.InstanceId, "RingHash": v.RingHash, "Id": id, "Name": name, "Rank": rank, "StockCount": v.StockCount, "OwnerIndex": v.OwnerIndex, "MaximumStock": maximum})
			}
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	allowed := []string{}
	switch verb {
	case "add":
		allowed = []string{"--emblem"}
	case "bond-set":
		allowed = []string{"--instance", "--person", "--level", "--experience"}
	case "bond-max":
		allowed = []string{"--instance", "--person"}
	case "bonds-max", "ring-meld", "clean", "remove":
		allowed = []string{"--instance"}
	case "clean-all", "rings-fill-s":
	case "dirt-set":
		allowed = []string{"--instance", "--value"}
	case "ring-set":
		allowed = []string{"--instance", "--amount"}
	default:
		fail("Unknown Emblems command.")
	}
	o := parse(args[3:], allowed...)
	var instance uint32
	if verb != "add" && verb != "rings-fill-s" && verb != "clean-all" {
		n := o.number("--instance")
		check(n >= 0, "Instance must be nonnegative.")
		instance = uint32(n)
	}
	switch verb {
	case "remove":
		s = take(s.WithoutEmblem(instance))
	case "clean-all":
		s = take(s.WithEmblemDirtiness(nil, 0))
	case "clean":
		s = take(s.WithEmblemDirtiness(&instance, 0))
	case "dirt-set":
		s = take(s.WithEmblemDirtiness(&instance, o.number("--value")))
	case "add":
		s = take(s.WithAddedEmblem(o.required("--emblem")))
	case "rings-fill-s":
		s = take(s.WithMissingSBondRings())
	case "ring-meld":
		s = take(s.WithMeldedBondRing(instance))
	case "ring-set":
		s = take(s.WithBondRingStock(instance, o.number("--amount")))
	case "bond-max":
		s = take(s.MaximizeEmblemBonds(instance, o.required("--person")))
	case "bonds-max":
		s = take(s.MaximizeEmblemBonds(instance, ""))
	case "bond-set":
		person := o.required("--person")
		check(o.has("--level") || o.has("--experience"), "Provide --level and/or --experience.")
		var exp *int
		if o.has("--experience") {
			v := o.number("--experience")
			exp = &v
		}
		level := 0
		if o.has("--level") {
			level = o.number("--level")
		} else {
			check(*exp >= 0 && *exp <= 208, "Bond EXP must be 0–208.")
			for _, threshold := range catalog.BondExperience {
				if threshold <= *exp {
					level++
				}
			}
		}
		s = take(s.WithEmblemBond(instance, person, level, exp))
	}
	write(out, s, args[2])
}
