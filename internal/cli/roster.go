package cli

import (
	"fmt"
	"github.com/jinghaihan/fee-save-editor/internal/catalog"
	"github.com/jinghaihan/fee-save-editor/internal/save"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

func skillObject(hash uint32, lang string) object {
	var id *string
	name := fmt.Sprintf("Unknown skill (0x%08X)", hash)
	if d := catalog.Skill(hash); d != nil {
		text := d.Id
		id = &text
		name = d.Name(lang)
	}
	return object{"Hash": hash, "Id": id, "Name": name}
}
func equipmentObject(s save.EquipmentSelection) object {
	return object{"Kind": []string{"None", "Emblem", "BondRing"}[s.Kind], "InstanceId": s.InstanceId}
}
func runRoster(args []string, out io.Writer) {
	check(len(args) > 0, "Provide a roster command.")
	verb := args[0]
	if verb == "catalog" {
		lang := language(args[1:])
		classes, skills := []object{}, []object{}
		for _, d := range catalog.Classes {
			if d.Flags&1 != 0 {
				classes = append(classes, object{"Id": d.Id, "Name": d.Name(lang), "MaxLevel": d.MaxLevel, "WeaponVariants": d.WeaponVariants()})
			}
		}
		for _, d := range catalog.Skills {
			if d.Inheritable {
				skills = append(skills, object{"Id": d.Id, "Name": d.Name(lang), "Family": d.Family, "Tier": d.Tier})
			}
		}
		outputJSON(out, object{"Classes": classes, "Skills": skills})
		return
	}
	check(len(args) >= 2, "Provide a save file.")
	s := load(args[1])
	switch verb {
	case "missing":
		lang := language(args[2:])
		rows := []object{}
		for _, d := range take(s.MissingRosterCharacters()) {
			rows = append(rows, object{"Id": d.Id, "Name": d.Name(lang)})
		}
		outputJSON(out, rows)
		return
	case "list":
		lang := language(args[2:])
		rows := []object{}
		for _, c := range take(s.Roster()) {
			var personID, classID *string
			name := fmt.Sprintf("Unknown character (0x%08X)", c.PersonHash)
			job := fmt.Sprintf("Unknown class (0x%08X)", c.ClassHash)
			if d := catalog.Person(c.PersonHash); d != nil {
				id := d.Id
				personID = &id
				name = d.Name(lang)
			}
			if d := catalog.Class(c.ClassHash); d != nil {
				id := d.Id
				classID = &id
				job = d.Name(lang)
			}
			stats, equipped, inherited, items := []object{}, []object{}, []object{}, []object{}
			for _, v := range c.Stats {
				stats = append(stats, object{"Stat": save.StatNames[v.Stat], "Value": v.Value, "Maximum": v.Maximum, "PersonalValue": v.PersonalValue})
			}
			for _, v := range c.Progress.EquippedSkills {
				equipped = append(equipped, skillObject(v.Hash, lang))
			}
			for _, v := range c.Progress.InheritedSkills {
				inherited = append(inherited, skillObject(v.Hash, lang))
			}
			for _, slot := range c.Items {
				var id, name, engravedID, engravedName any
				if slot.Item != nil {
					obj := itemObject(slot, lang)
					id, name, engravedID, engravedName = obj["Id"], obj["Name"], obj["EngravingId"], obj["Engraving"]
					if catalog.Item(slot.Item.ItemHash) == nil {
						if d := catalog.RosterItem(slot.Item.ItemHash); d != nil {
							name = d.Name(lang)
						}
					}
				}
				items = append(items, object{"Slot": slot.Slot, "Id": id, "Name": name, "Item": slot.Item, "EngravingId": engravedID, "Engraving": engravedName})
			}
			rows = append(rows, object{"Index": c.Index, "Force": []string{"Player", "Enemy", "Ally", "Absent", "Dead", "Lost", "Temporary"}[c.Force], "Availability": c.Availability(), "PersonId": personID, "Name": name, "ClassId": classID, "Class": job, "Values": c.Values, "Stats": stats, "Progress": c.Progress, "EquippedSkills": equipped, "InheritedSkills": inherited, "Items": items})
		}
		outputJSON(out, rows)
		return
	case "equipment":
		check(len(args) >= 4 && args[2] == "--character", "Provide --character <index>.")
		index := integer(args[3])
		lang := language(args[4:])
		current := take(s.RosterEquipment(index))
		links := take(s.CharacterRingLinks())
		check(index >= 0 && index < len(links), "Invalid character index.")
		options := []object{}
		for _, v := range take(s.RosterEquipmentOptions(index)) {
			name := map[string]string{"en": "None", "zh-Hans": "无", "zh-Hant": "無", "ja": "なし", "ko": "없음", "de": "Keine", "fr": "Aucun", "es": "Ninguno", "it": "Nessuno"}[lang]
			var rank *string
			if v.EmblemId != nil {
				name = catalog.Emblem(*v.EmblemId).Name(lang)
			}
			if v.RingHash != nil {
				d := catalog.BondRing(*v.RingHash)
				name = d.Name(lang)
				r := d.RankName()
				rank = &r
			}
			options = append(options, object{"Selection": equipmentObject(v.Selection), "EmblemId": v.EmblemId, "RingHash": v.RingHash, "OwnerIndex": v.OwnerIndex, "StockCount": v.StockCount, "Name": name, "Rank": rank})
		}
		outputJSON(out, object{"Current": equipmentObject(current), "Links": links[index], "Options": options})
		return
	case "export":
		check(len(args) == 5 && args[3] == "--character", "Provide a file and --character <index>.")
		b := take(s.ExportRosterCharacter(integer(args[4])))
		done(save.WriteRosterTransfer(args[2], b))
		fmt.Fprintln(out, take(filepath.Abs(args[2])))
		return
	}
	check(len(args) >= 3, "Provide an output file.")
	write(out, editRoster(s, verb, args[3:]), args[2])
}
func weaponMask(text string) uint32 {
	if text == "none" {
		return 0
	}
	names := []string{"", "Sword", "Lance", "Axe", "Bow", "Dagger", "Magic", "Staff", "Arts", "Special"}
	var mask uint32
	for _, part := range strings.Split(text, ",") {
		found := -1
		for i, name := range names {
			if i > 0 && strings.EqualFold(name, part) {
				found = i
			}
		}
		check(found > 0, "Use weapon names separated by commas, such as Sword,Lance.")
		bit := uint32(1 << found)
		check(mask&bit == 0, "Duplicate weapon type.")
		mask |= bit
	}
	return mask
}
func selectedSkill(id string) *uint32 {
	if id == "none" {
		return nil
	}
	d := catalog.Skill(catalog.Hash(id))
	check(d != nil && d.Id == id, "Select a known skill ID, or none to clear a slot.")
	hash := d.Hash()
	return &hash
}
func editRoster(s *save.Save, verb string, args []string) *save.Save {
	if verb == "stats-max" && len(args) == 1 && args[0] == "--all" {
		return take(s.MaximizeRosterStats(nil))
	}
	if verb == "classes-repair" && len(args) == 1 && args[0] == "--all" {
		return take(s.RepairAllRosterClasses())
	}
	if verb == "add" {
		o := parse(args, "--person")
		return take(s.WithAddedRosterCharacter(o.required("--person")))
	}
	allowed := map[string][]string{"set": {"--character", "--level", "--experience", "--sp"}, "class": {"--character", "--class", "--weapons"}, "condition": {"--character", "--internal-level", "--hp"}, "skill-unlock": {"--character", "--skill"}, "skill-remove": {"--character", "--skill"}, "skills-max": {"--character"}, "stats-max": {"--character"}, "skills-equip": {"--character", "--first", "--second"}, "class-skill": {"--character", "--unlocked"}, "proficiencies": {"--character", "--weapons"}, "stat": {"--character", "--stat", "--value"}, "personal-stat": {"--character", "--stat", "--value"}, "item-set": {"--character", "--slot", "--item", "--uses", "--refine", "--engraving"}, "item-engrave": {"--character", "--slot", "--engraving"}, "item-delete": {"--character", "--slot"}, "restore": {"--character"}, "restore-character": {"--character"}, "delete": {"--character"}, "class-repair": {"--character"}, "move": {"--character", "--force"}, "import": {"--character", "--file"}, "equipment-set": {"--character", "--emblem", "--ring", "--none"}}
	keys, ok := allowed[verb]
	check(ok, "Unknown roster command.")
	o := parse(args, keys...)
	index := o.number("--character")
	rows := take(s.Roster())
	check(index >= 0 && index < len(rows), "Invalid character index.")
	c := rows[index]
	switch verb {
	case "delete":
		return take(s.WithoutRosterCharacter(index))
	case "class-repair":
		return take(s.RepairRosterClass(index))
	case "restore-character":
		return take(s.RestoreRosterCharacter(index))
	case "move":
		force, ok := map[string]save.UnitForce{"Absent": save.Absent, "Dead": save.Dead, "Lost": save.Lost}[o.required("--force")]
		check(ok, "Choose Absent, Dead or Lost.")
		return take(s.WithRosterForce(index, force))
	case "equipment-set":
		check(len(o) == 2, "Choose exactly one of --emblem, --ring or --none.")
		if o.has("--none") {
			check(o["--none"] == "true", "Use --none true to unequip.")
			return take(s.WithRosterEquipment(index, save.EquipmentSelection{}))
		}
		key := "--emblem"
		kind := save.EquipmentEmblem
		if !o.has(key) {
			key = "--ring"
			kind = save.EquipmentBondRing
		}
		id := take(strconv.ParseUint(o.required(key), 10, 32))
		check(id > 0, "An equipment instance must be a positive integer.")
		return take(s.WithRosterEquipment(index, save.EquipmentSelection{Kind: kind, InstanceId: uint32(id)}))
	case "import":
		path := o.required("--file")
		info := take(os.Stat(path))
		check(info.Size() <= 1048576, "A character file must not exceed 1 MiB.")
		return take(s.ImportRosterCharacter(index, take(os.ReadFile(path))))
	case "stats-max":
		return take(s.MaximizeRosterStats(&index))
	case "skills-max":
		return take(s.UnlockAllRosterSkills(index))
	case "condition":
		check(len(o) > 1, "Provide --internal-level or --hp.")
		return take(s.WithRosterCondition(index, o.integerOr("--internal-level", c.Progress.InternalLevel), o.integerOr("--hp", c.Progress.CurrentHP)))
	case "skill-unlock", "skill-remove":
		return take(s.WithRosterSkill(index, o.required("--skill"), verb == "skill-unlock"))
	case "class-skill":
		text := strings.ToLower(o.required("--unlocked"))
		check(text == "true" || text == "false", "Use true or false for --unlocked.")
		return take(s.WithRosterClassSkill(index, text == "true"))
	case "skills-equip":
		check(len(o) > 1, "Provide --first or --second.")
		var first, second *uint32
		if len(c.Progress.EquippedSkills) > 0 {
			v := c.Progress.EquippedSkills[0].Hash
			first = &v
		}
		if len(c.Progress.EquippedSkills) > 1 {
			v := c.Progress.EquippedSkills[1].Hash
			second = &v
		}
		if o.has("--first") {
			first = selectedSkill(o["--first"])
		}
		if o.has("--second") {
			second = selectedSkill(o["--second"])
		}
		return take(s.WithRosterEquippedSkills(index, first, second))
	case "proficiencies":
		return take(s.WithRosterProficiencies(index, weaponMask(o.required("--weapons"))))
	case "class":
		id := o.required("--class")
		var job *catalog.ClassDefinition
		for _, d := range take(s.RosterClasses(index)) {
			if d.Id == id {
				copy := d
				job = &copy
			}
		}
		check(job != nil, "This character cannot use the selected class.")
		mask := job.WeaponVariants()[0]
		if o.has("--weapons") {
			mask = weaponMask(o["--weapons"])
		}
		return take(s.WithRosterClass(index, id, mask))
	case "set":
		check(len(o) > 1, "Provide at least one character value to edit.")
		v := c.Values
		v.Level = o.integerOr("--level", v.Level)
		v.Experience = o.integerOr("--experience", v.Experience)
		v.SkillPoints = o.integerOr("--sp", v.SkillPoints)
		return take(s.WithRosterValues(index, v))
	case "stat", "personal-stat":
		name := o.required("--stat")
		stat := -1
		for i, s := range save.StatNames {
			if strings.EqualFold(s, name) {
				stat = i
			}
		}
		check(stat >= 0, "Use a character stat name, such as Strength or HP.")
		return take(s.WithRosterStat(index, save.RosterStat(stat), o.number("--value"), verb == "personal-stat"))
	case "restore":
		return take(s.RestoreRosterUses(index))
	}
	slot := o.number("--slot")
	check(slot >= 0 && slot < len(c.Items), "Invalid character item slot.")
	switch verb {
	case "item-engrave":
		return take(s.WithRosterEngraving(index, slot, engravingID(o.required("--engraving"))))
	case "item-delete":
		return take(s.DeleteRosterItem(index, slot))
	}
	old := c.Items[slot].Item
	id := o["--item"]
	if id == "" && old != nil {
		if d := catalog.Item(old.ItemHash); d != nil {
			id = d.Id
		}
	}
	check(id != "", "Choose an item with --item for an empty or unknown slot.")
	d := take(catalog.ItemByID(id))
	uses, refine := d.MaxUses, 0
	if old != nil && old.ItemHash == d.Hash() {
		uses, refine = old.Uses, old.RefineLevel
	}
	uses = o.integerOr("--uses", uses)
	refine = o.integerOr("--refine", refine)
	if o.has("--engraving") {
		return take(s.WithEngravedItem(&index, slot, id, uses, refine, engravingID(o["--engraving"])))
	}
	return take(s.WithRosterItem(index, slot, id, uses, refine))
}
