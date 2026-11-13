package cli

import (
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"io"
	"strconv"
	"strings"
)

func runProgress(group string, args []string, out io.Writer) {
	check(len(args) > 0, "Provide a command.")
	verb := args[0]
	if group == "achievements" && verb == "catalog" {
		lang := language(args[1:])
		rows := []object{}
		for _, d := range catalog.Achievements {
			rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Category": []string{"Unit", "Battle", "Somniel", "Shop", "System"}[d.Category], "Reward": d.Reward, "Chapter": d.Chapter})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	if verb == "list" {
		rows := []object{}
		lang := "en"
		if group == "tickets" || group == "variables" {
			parse(args[2:], "--json")
		} else {
			lang = language(args[2:])
		}
		switch group {
		case "variables":
			outputJSON(out, take(s.Variables()))
			return
		case "animals":
			for _, r := range take(s.Animals()) {
				rows = append(rows, object{"Id": r.Definition.Id, "Name": r.Definition.Names.Name(lang), "Amount": r.Amount, "Maximum": 99})
			}
		case "amiibo":
			groups := []object{}
			rewards := take(s.Amiibo())
			for _, name := range []string{"Outfits", "Music"} {
				unlocked := 0
				for _, r := range rewards {
					if r.Definition.Group == name && r.Unlocked {
						unlocked++
					}
				}
				groups = append(groups, object{"Group": name, "Unlocked": unlocked, "Total": 12})
			}
			for _, r := range rewards {
				rows = append(rows, object{"Key": r.Definition.Key, "Group": r.Definition.Group, "Name": r.Definition.Names.Name(lang), "Unlocked": r.Unlocked, "Value": r.Value})
			}
			outputJSON(out, object{"Groups": groups, "Rewards": rows})
			return
		case "achievements":
			for _, r := range take(s.Achievements()) {
				d := r.Definition
				rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Category": []string{"Unit", "Battle", "Somniel", "Shop", "System"}[d.Category], "Status": []string{"None", "Cleared", "Showed", "Completed"}[r.Status], "Achieved": r.Achieved, "RewardAvailable": r.RewardAvailable, "RewardClaimed": r.RewardClaimed, "Reward": d.Reward, "Chapter": d.Chapter})
			}
		case "tickets":
			for _, r := range take(s.Tickets()) {
				rows = append(rows, object{"Identifier": r.IdentifierHex, "Amount": r.Amount, "Maximum": 999, "LastTime": r.LastTime})
			}
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	rest := args[3:]
	switch group {
	case "variables":
		check(verb == "set", "Use variables list or set.")
		o := parse(rest, "--key", "--value")
		s = take(s.WithVariable(o.required("--key"), o.number("--value")))
	case "animals":
		if verb == "set" {
			o := parse(rest, "--animal", "--amount")
			s = take(s.WithAnimal(o.required("--animal"), o.number("--amount"), false))
		} else {
			check(verb == "max", "Use animals list, set or max.")
			o := parse(rest, "--all", "--animal")
			check(len(o) == 1, "Use --all or --animal.")
			s = take(s.WithAnimal(o["--animal"], 99, o.has("--all")))
		}
	case "amiibo":
		check(verb == "unlock", "Use amiibo list or unlock.")
		keys := []string{}
		if len(rest) == 1 && rest[0] == "--all" {
			for _, d := range catalog.AmiiboRewards {
				keys = append(keys, d.Key)
			}
		} else if len(rest) == 2 && rest[0] == "--group" {
			check(rest[1] == "outfits" || rest[1] == "music", "Choose outfits or music.")
			for _, d := range catalog.AmiiboRewards {
				if strings.EqualFold(d.Group, rest[1]) {
					keys = append(keys, d.Key)
				}
			}
		} else {
			check(len(rest) > 0 && len(rest)%2 == 0, "Provide --all, --group, or --key values.")
			for i := 0; i < len(rest); i += 2 {
				check(rest[i] == "--key", "Only --key can be repeated.")
				keys = append(keys, rest[i+1])
			}
		}
		s = take(s.UnlockAmiibo(keys))
	case "achievements":
		check(verb == "unlock", "Use achievements list, catalog or unlock.")
		o := parse(rest, "--all", "--achievement")
		check(len(o) == 1, "Use --all or --achievement.")
		s = take(s.UnlockAchievements(o["--achievement"]))
	case "tickets":
		check(verb == "set" || verb == "max", "Use tickets list, set or max.")
		allowed := []string{"--identifier"}
		if verb == "set" {
			allowed = append(allowed, "--amount")
		}
		o := parse(rest, allowed...)
		id := take(strconv.ParseUint(o.required("--identifier"), 16, 64))
		amount := 999
		if verb == "set" {
			amount = o.number("--amount")
		}
		s = take(s.WithTickets(id, amount))
	}
	if group == "animals" || group == "amiibo" || group == "tickets" {
		done(s.WriteCopy(args[2]))
	} else {
		write(out, s, args[2])
	}
}
func runDonations(args []string, out io.Writer) {
	verb := args[0]
	if verb == "donation-catalog" {
		lang := language(args[1:])
		rows := []object{}
		for _, d := range catalog.DonationCountries {
			rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Thresholds": d.Thresholds, "MaximumLevel": 5, "MaximumAmount": 9999999})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	if verb == "donations" {
		lang := language(args[2:])
		rows := []object{}
		for _, r := range take(s.Donations()) {
			d := r.Country
			rows = append(rows, object{"Id": d.Id, "Name": d.Names.Name(lang), "Level": r.Level, "Amount": r.Amount, "Thresholds": d.Thresholds, "MaximumLevel": 5})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	check(verb == "donation-set" || verb == "donations-max", "Unknown donation command.")
	allowed := []string{"--country"}
	if verb == "donations-max" {
		allowed = append(allowed, "--all")
	} else {
		allowed = append(allowed, "--level", "--amount")
	}
	o := parse(args[3:], allowed...)
	if o.has("--all") {
		check(len(o) == 1, "Use --all or --country.")
		for _, d := range catalog.DonationCountries {
			s = take(s.WithDonation(d.Id, d.Thresholds[4]))
		}
		write(out, s, args[2])
		return
	}
	id := o.required("--country")
	var country *catalog.DonationCountry
	for _, d := range catalog.DonationCountries {
		if d.Id == id || strings.EqualFold(d.Names.Name("en"), id) {
			copy := d
			country = &copy
		}
	}
	check(country != nil, "Use Firene, Brodia, Elusia, Solm or a country ID.")
	amount := country.Thresholds[4]
	if verb == "donation-set" {
		check(o.has("--amount") || o.has("--level"), "Provide --level and/or --amount.")
		if o.has("--level") {
			level := o.number("--level")
			check(level >= 1 && level <= 5, "Donation level must be 1–5.")
			amount = country.Thresholds[level-1]
			if o.has("--amount") {
				amount = o.number("--amount")
				check(country.Level(amount) == level, "Donation level and amount must agree.")
			}
		} else {
			amount = o.number("--amount")
		}
	}
	write(out, take(s.WithDonation(country.Id, amount)), args[2])
}
func runMinigames(args []string, out io.Writer) {
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	records := take(s.MinigameRecords())
	if args[0] == "minigames" {
		lang := language(args[2:])
		groups := []object{}
		for _, group := range catalog.Minigames {
			rows := []object{}
			for _, d := range group.Records {
				for _, r := range records {
					if r.Definition.Key != d.Key {
						continue
					}
					var ranks []string
					if d.RankKey() != "" {
						ranks = d.Ranks()
					}
					rows = append(rows, object{"Key": d.Key, "Name": d.Names.Name(lang), "Value": r.Value, "Rank": r.Rank, "BestSize": r.BestSize, "MaximumValue": 2147483647, "Ranks": ranks})
				}
			}
			groups = append(groups, object{"Id": group.Id, "Name": group.Names.Name(lang), "ReadOnly": false, "Records": rows})
		}
		outputJSON(out, groups)
		return
	}
	check(args[0] == "minigame-set" && len(args) >= 3, "Use main minigames or minigame-set.")
	o := parse(args[3:], "--record", "--value", "--rank", "--best-size")
	key := o.required("--record")
	check(o.has("--value") || o.has("--rank") || o.has("--best-size"), "Provide --value, --rank and/or --best-size.")
	var record *save.MinigameRecord
	for _, r := range records {
		if r.Definition.Key == key {
			copy := r
			record = &copy
		}
	}
	check(record != nil, "Use a record key from main minigames.")
	values := save.MinigameValues{Value: o.integerOr("--value", record.Value)}
	if text, ok := o["--rank"]; ok {
		check(record.Definition.RankKey() != "", "Strength-training scores have no stored rank.")
		rank := -1
		for i, name := range record.Definition.Ranks() {
			if strings.EqualFold(name, text) {
				rank = i
			}
		}
		if rank < 0 {
			rank = integer(text)
		}
		values.Rank = &rank
	}
	if o.has("--best-size") {
		size := o.number("--best-size")
		values.BestSize = &size
	}
	write(out, take(s.WithMinigame(key, values)), args[2])
}
