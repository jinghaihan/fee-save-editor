package cli

import (
	_ "embed"
	"encoding/json"
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"io"
	"math"
	"path/filepath"
	"slices"
	"strconv"
	"strings"
)

//go:embed help.txt
var help string
var Version = "0.4.0"

type commandError struct{ error }

func fail(message string) { panic(commandError{fmt.Errorf("%s", message)}) }
func check(ok bool, message string) {
	if !ok {
		fail(message)
	}
}
func take[T any](value T, err error) T {
	if err != nil {
		panic(commandError{err})
	}
	return value
}
func done(err error) {
	if err != nil {
		panic(commandError{err})
	}
}

type object = map[string]any
type options map[string]string

func parse(args []string, allowed ...string) options {
	out := options{}
	for i := 0; i < len(args); i++ {
		key := args[i]
		check(slices.Contains(allowed, key), "Unknown option: "+key)
		_, duplicate := out[key]
		check(!duplicate, "Duplicate option: "+key)
		if key == "--json" || key == "--all" {
			out[key] = "true"
			continue
		}
		i++
		check(i < len(args), "Missing value for "+key)
		out[key] = args[i]
	}
	return out
}
func (o options) required(key string) string { v, ok := o[key]; check(ok, "Missing "+key); return v }
func integer(text string) int                { return int(take(strconv.ParseInt(strings.TrimSpace(text), 10, 32))) }
func (o options) number(key string) int      { return integer(o.required(key)) }
func (o options) integerOr(key string, current int) int {
	if value, ok := o[key]; ok {
		return integer(value)
	}
	return current
}
func (o options) has(key string) bool { _, ok := o[key]; return ok }
func language(args []string) string {
	o := parse(args, "--json", "--language")
	lang := "en"
	if v, ok := o["--language"]; ok {
		lang = v
	}
	valid := false
	for _, l := range catalog.Languages {
		valid = valid || l.Code == lang
	}
	check(valid, "Choose a supported --language code.")
	return lang
}
func load(path string) *save.Save { return take(save.Load(path)) }
func outputJSON(out io.Writer, value any) {
	encoder := json.NewEncoder(out)
	encoder.SetIndent("", "  ")
	encoder.SetEscapeHTML(false)
	done(encoder.Encode(value))
}
func write(out io.Writer, s *save.Save, path string) {
	done(s.WriteCopy(path))
	fmt.Fprintln(out, take(filepath.Abs(path)))
}
func Run(args []string, out, errout io.Writer) (code int) {
	defer func() {
		if p := recover(); p != nil {
			if e, ok := p.(commandError); ok {
				fmt.Fprintln(errout, e.Error())
				code = 1
			} else {
				panic(p)
			}
		}
	}()
	if len(args) == 0 || slices.Equal(args, []string{"--help"}) {
		fmt.Fprint(out, help)
		return 0
	}
	if slices.Equal(args, []string{"--version"}) {
		fmt.Fprintln(out, Version)
		return 0
	}
	switch args[0] {
	case "inspect":
		check(len(args) >= 2, "Provide a save file.")
		parse(args[2:], "--json")
		s := load(args[1])
		sections := []object{}
		for _, sec := range s.Sections {
			sections = append(sections, object{"Name": sec.Name, "Offset": sec.Offset, "Length": sec.Length, "PayloadOffset": sec.PayloadOffset()})
		}
		outputJSON(out, object{"Kind": s.Kind, "FormatVersion": s.FormatVersion, "GameVersion": s.GameVersion, "Length": s.Length(), "Checksum": fmt.Sprintf("%08X", s.Checksum), "Sections": sections})
	case "copy":
		check(len(args) == 3, "Use copy <save> <new-file>.")
		write(out, load(args[1]), args[2])
	case "roster":
		runRoster(args[1:], out)
	case "emblems":
		runEmblems(args[1:], out)
	case "supports":
		runSupports(args[1:], out)
	case "items":
		runItems(args[1:], out)
	case "main":
		runMain(args[1:], out)
	case "variables", "animals", "amiibo", "achievements", "tickets":
		runProgress(args[0], args[1:], out)
	default:
		fail("Unknown command. Run --help for usage.")
	}
	return 0
}
func formatTime(v float32) string {
	seconds := strconv.FormatFloat(math.Mod(float64(v), 60), 'f', -1, 64)
	if math.Mod(float64(v), 60) < 10 {
		seconds = "0" + seconds
	}
	return fmt.Sprintf("%d:%02d:%s", int(v)/3600, int(v)/60%60, seconds)
}
func parseTime(text string) float32 {
	parts := strings.Split(text, ":")
	check(len(parts) == 3, "Play time must use HH:MM:SS.")
	for _, part := range parts[:2] {
		check(part != "" && strings.IndexFunc(part, func(r rune) bool { return r < '0' || r > '9' }) < 0, "Invalid play time.")
	}
	h, m := integer(parts[0]), integer(parts[1])
	check(parts[2] != "" && strings.IndexFunc(parts[2], func(r rune) bool { return (r < '0' || r > '9') && r != '.' }) < 0, "Invalid play time.")
	s := take(strconv.ParseFloat(parts[2], 64))
	check(h <= 999 && m <= 59 && s >= 0 && s < 60, "Play time is outside the native range.")
	total := float64(h*3600+m*60) + s
	check(total <= 3599999.5, "Play time cannot exceed 999:59:59.5.")
	return float32(total)
}
func runMain(args []string, out io.Writer) {
	check(len(args) > 0, "Provide a main command.")
	verb := args[0]
	if verb == "donation-catalog" {
		runDonations(args, out)
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	switch verb {
	case "show":
		parse(args[2:], "--json")
		v := take(s.Main())
		b := take(json.Marshal(v))
		var obj object
		done(json.Unmarshal(b, &obj))
		for _, sec := range s.Sections {
			if sec.Name == "TIME" {
				seconds := take(s.PlayTime())
				obj["PlayTimeSeconds"] = float64(seconds)
				obj["PlayTime"] = formatTime(seconds)
			}
		}
		outputJSON(out, obj)
	case "name":
		check(len(args) == 2, "Use main name <save>.")
		outputJSON(out, object{"PlayerName": take(s.ProtagonistName())})
	case "gender":
		check(len(args) == 2, "Use main gender <save>.")
		var name *string
		if n := take(s.ProtagonistGender()); n != nil {
			v := []string{"", "male", "female"}[*n]
			name = &v
		}
		outputJSON(out, object{"Gender": name})
	case "name-set", "gender-set":
		check(len(args) == 5, "Provide the new name or gender.")
		key := "--name"
		if verb == "gender-set" {
			key = "--gender"
		}
		o := parse(args[3:], key)
		if verb == "name-set" {
			s = take(s.WithProtagonistName(o.required(key)))
		} else {
			gender := strings.ToLower(o.required(key))
			check(gender == "male" || gender == "female", "Gender must be male or female.")
			n := 1
			if gender == "female" {
				n = 2
			}
			s = take(s.WithProtagonistGender(n))
		}
		write(out, s, args[2])
	case "set":
		check(len(args) >= 5, "Provide Main options and their values.")
		o := parse(args[3:], "--money", "--bond-fragments", "--iron", "--steel", "--silver", "--difficulty", "--mode", "--sommie-name", "--play-time")
		v := take(s.Main())
		v.Money = o.integerOr("--money", v.Money)
		v.BondFragments = o.integerOr("--bond-fragments", v.BondFragments)
		v.IronIngots = o.integerOr("--iron", v.IronIngots)
		v.SteelIngots = o.integerOr("--steel", v.SteelIngots)
		v.SilverIngots = o.integerOr("--silver", v.SilverIngots)
		if x, ok := o["--difficulty"]; ok {
			v.Difficulty = ""
			for _, d := range save.Difficulties {
				if strings.EqualFold(d, x) {
					v.Difficulty = d
				}
			}
		}
		if x, ok := o["--mode"]; ok {
			v.GameMode = ""
			for _, d := range save.GameModes {
				if strings.EqualFold(d, x) {
					v.GameMode = d
				}
			}
		}
		if x, ok := o["--sommie-name"]; ok {
			v.SommieName = x
		}
		s = take(s.WithMain(v))
		if x, ok := o["--play-time"]; ok {
			s = take(s.WithPlayTime(parseTime(x)))
		}
		write(out, s, args[2])
	case "activities":
		parse(args[2:], "--json")
		v := take(s.Activities())
		outputJSON(out, object{"TrainingRemaining": v.TrainingRemaining, "ArenaRemaining": v.ArenaRemaining, "MaximumTraining": 1, "MaximumArena": 3})
	case "activities-restore", "activities-set":
		check(len(args) >= 3, "Provide an output file.")
		v := take(s.Activities())
		if verb == "activities-restore" {
			check(len(args) == 3, "Unexpected option.")
			v = save.ActivityValues{TrainingRemaining: 1, ArenaRemaining: 3}
		} else {
			o := parse(args[3:], "--training-remaining", "--arena-remaining")
			check(len(o) > 0, "Provide at least one activity option.")
			v.TrainingRemaining = o.integerOr("--training-remaining", v.TrainingRemaining)
			v.ArenaRemaining = o.integerOr("--arena-remaining", v.ArenaRemaining)
		}
		write(out, take(s.WithActivities(v)), args[2])
	case "donations", "donation-set", "donations-max":
		runDonations(args, out)
	case "minigames", "minigame-set":
		runMinigames(args, out)
	default:
		fail("Unknown Main command.")
	}
}
