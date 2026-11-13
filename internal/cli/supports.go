package cli

import (
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"io"
	"strings"
)

var supportRanks = []string{"None", "C", "B", "A", "A+"}

func personName(id *string, lang string) *string {
	if id == nil {
		return nil
	}
	name := *id
	if p := catalog.Person(catalog.Hash(*id)); p != nil {
		name = p.Name(lang)
	}
	return &name
}
func runSupports(args []string, out io.Writer) {
	check(len(args) > 0, "Provide a Support command.")
	if args[0] == "catalog" {
		lang := language(args[1:])
		rows := []object{}
		for _, p := range catalog.SupportPairs {
			rows = append(rows, object{"Key": p.Key(), "FirstPersonId": p.FirstPersonId, "SecondPersonId": p.SecondPersonId, "FirstName": personName(&p.FirstPersonId, lang), "SecondName": personName(&p.SecondPersonId, lang), "Thresholds": p.Thresholds})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	if args[0] == "list" {
		lang := language(args[2:])
		rows := []object{}
		for _, v := range take(s.Supports()) {
			var first, second, maximum *string
			var thresholds []int
			if p := catalog.SupportPair(v.Key); p != nil {
				a, b := p.FirstPersonId, p.SecondPersonId
				first, second = &a, &b
				rank := supportRanks[take(s.MaximumSupportRank(v.Key))]
				maximum = &rank
				thresholds = p.Thresholds
			}
			rows = append(rows, object{"Key": v.Key, "FirstPersonId": first, "SecondPersonId": second, "FirstName": personName(first, lang), "SecondName": personName(second, lang), "Rank": supportRanks[v.Rank], "Points": v.Points, "Score": v.Score, "MaximumRank": maximum, "Thresholds": thresholds})
		}
		outputJSON(out, rows)
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	switch args[0] {
	case "max":
		o := parse(args[3:], "--all", "--pair")
		check(len(o) == 1, "Use --all or --pair.")
		s = take(s.MaximizeSupports(o["--pair"]))
	case "set":
		o := parse(args[3:], "--pair", "--rank", "--points")
		key := o.required("--pair")
		check(o.has("--rank") || o.has("--points"), "Provide --rank and/or --points.")
		var points *int
		if o.has("--points") {
			n := o.number("--points")
			check(n >= 0 && n <= 99, "Support points must be 0–99.")
			points = &n
		}
		rank := -1
		if text, ok := o["--rank"]; ok {
			for i, name := range supportRanks {
				if strings.EqualFold(name, text) {
					rank = i
				}
			}
			check(rank >= 0, "Use None, C, B, A or A+ for --rank.")
		} else {
			p := catalog.SupportPair(key)
			check(p != nil, "Unknown support pair.")
			rank = 0
			for _, threshold := range p.Thresholds {
				if threshold <= *points {
					rank++
				}
			}
			for _, v := range take(s.Supports()) {
				pair := catalog.SupportPair(v.Key)
				if pair != nil && pair.Key() == p.Key() && v.Rank == 4 {
					rank = 4
				}
			}
		}
		s = take(s.WithSupport(key, rank, points))
	default:
		fail("Unknown Support command.")
	}
	write(out, s, args[2])
}
