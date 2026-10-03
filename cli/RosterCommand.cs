using System.Text.Json;
using System.Text.Json.Serialization;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class RosterCommand
{
    public static int Run(string[] args)
    {
        if (args is ["equipment", var equipmentSource, "--character", var equipmentIndex, .. var equipmentDisplay])
        {
            string language = DisplayLanguage(equipmentDisplay);
            var loaded = EngageSave.Load(equipmentSource);
            int index = Number(equipmentIndex);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Current = loaded.ReadRosterEquipment(index),
                Links = loaded.ReadCharacterRingLinks().Single(link => link.CharacterIndex == index),
                Options = loaded.ReadRosterEquipmentOptions(index).Select(option => new
                {
                    option.Selection, option.EmblemId, option.RingHash, option.OwnerIndex, option.StockCount,
                    Name = EquipmentName(option, language),
                    Rank = option.RingHash is uint ringHash ? EmblemCatalog.Ring(ringHash)!.RankName : null
                })
            }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            return 0;
        }
        if (args is ["export", var exportSource, var exportPath, "--character", var exportIndex])
        {
            RosterTransfer.WriteNew(exportPath, EngageSave.Load(exportSource).ExportRosterCharacter(Number(exportIndex)));
            Console.WriteLine(Path.GetFullPath(exportPath));
            return 0;
        }
        if (args is ["catalog", .. var catalogOptions])
        {
            string language = DisplayLanguage(catalogOptions);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Classes = RosterCatalog.Classes.Where(job => (job.Flags & 1) != 0).Select(job => new
                    { job.Id, Name = job.Name(language), job.MaxLevel, WeaponVariants = job.WeaponVariants() }),
                Skills = RosterCatalog.Skills.Where(skill => skill.Inheritable).Select(skill => new
                    { skill.Id, Name = skill.Name(language), skill.Family, skill.Tier })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args is ["missing", var missingSource, .. var missingOptions])
        {
            string language = DisplayLanguage(missingOptions);
            Console.WriteLine(JsonSerializer.Serialize(EngageSave.Load(missingSource).ReadMissingRosterCharacters()
                .Select(person => new { person.Id, Name = person.Name(language) }), new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args is ["list", var input, .. var displayOptions])
        {
            string language = DisplayLanguage(displayOptions);
            var characters = EngageSave.Load(input).ReadRoster();
            Console.WriteLine(JsonSerializer.Serialize(characters.Select(character => new
            {
                character.Index, character.Force, character.Availability,
                PersonId = RosterCatalog.Person(character.PersonHash)?.Id,
                Name = RosterCatalog.Person(character.PersonHash)?.Name(language) ?? $"Unknown character (0x{character.PersonHash:X8})",
                ClassId = RosterCatalog.Class(character.ClassHash)?.Id,
                Class = RosterCatalog.Class(character.ClassHash)?.Name(language) ?? $"Unknown class (0x{character.ClassHash:X8})",
                character.Values, character.Stats, character.Progress,
                EquippedSkills = character.Progress.EquippedSkills.Select(skill => new
                    { skill.Hash, Id = RosterCatalog.Skill(skill.Hash)?.Id, Name = SkillName(skill.Hash, language) }),
                InheritedSkills = character.Progress.InheritedSkills.Select(skill => new
                    { skill.Hash, Id = RosterCatalog.Skill(skill.Hash)?.Id, Name = SkillName(skill.Hash, language) }),
                Items = character.Items.Select(slot => new
                {
                    slot.Slot, Id = ItemCatalog.Find(slot.Item?.ItemHash ?? 0)?.Id,
                    Name = slot.Item is null ? null : ItemCatalog.Find(slot.Item.ItemHash)?.Name(language)
                        ?? RosterCatalog.Item(slot.Item.ItemHash)?.Name(language) ?? $"Unknown item (0x{slot.Item.ItemHash:X8})",
                    slot.Item,
                    EngravingId = slot.Item?.EngravingHash is uint hash ? EngravingCatalog.Find(hash)?.Id : null,
                    Engraving = slot.Item?.EngravingHash is uint engraving ? EngravingCatalog.Find(engraving)?.Name(language) ?? $"0x{engraving:X8}" : null
                })
            }), new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            return 0;
        }
        if (args is not [var verb, var source, var destination, .. var options])
            throw new ArgumentException("Unknown roster command. Run --help for usage.");
        var save = EngageSave.Load(source);
        Edit(save, verb, options).WriteCopy(destination);
        Console.WriteLine(Path.GetFullPath(destination));
        return 0;
    }

    private static string EquipmentName(RosterEquipmentOption option, string language)
    {
        if (option.EmblemId is string gid) return EmblemCatalog.Emblem(gid)!.Name(language);
        if (option.RingHash is uint hash) return EmblemCatalog.Ring(hash)!.Name(language);
        return language switch
        {
            "zh-Hans" => "无", "zh-Hant" => "無",
            "ja" => "なし", "ko" => "없음", "de" => "Keine",
            "fr" => "Aucun", "es" => "Ninguno", "it" => "Nessuno", _ => "None"
        };
    }

    private static string DisplayLanguage(string[] options)
    {
        string language = "en";
        bool json = false;
        bool localized = false;
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] == "--json" && !json)
                json = true;
            else if (options[index] == "--language" && !localized && index + 1 < options.Length)
            {
                language = options[++index];
                localized = true;
            }
            else
                throw new ArgumentException($"Invalid roster display option: {options[index]}");
        }
        if (!LanguageCatalog.Codes.Contains(language))
            throw new ArgumentException("Choose a supported language: en, zh-Hans, zh-Hant, ja, ko, de, fr, es or it.");
        return language;
    }

    private static EngageSave Edit(EngageSave save, string verb, string[] options)
    {
        if (verb == "stats-max" && options is ["--all"])
            return save.MaximizeAllRosterStats();
        if (verb == "classes-repair" && options is ["--all"])
            return save.RepairAllRosterClasses();
        if (verb == "add" && options is ["--person", var person])
            return save.WithAddedRosterCharacter(person);
        string[] allowed = verb switch
        {
            "set" => ["--character", "--level", "--experience", "--sp"],
            "class" => ["--character", "--class", "--weapons"],
            "condition" => ["--character", "--internal-level", "--hp"],
            "skill-unlock" or "skill-remove" => ["--character", "--skill"],
            "skills-max" or "stats-max" => ["--character"],
            "skills-equip" => ["--character", "--first", "--second"],
            "class-skill" => ["--character", "--unlocked"],
            "proficiencies" => ["--character", "--weapons"],
            "stat" or "personal-stat" => ["--character", "--stat", "--value"],
            "item-set" => ["--character", "--slot", "--item", "--uses", "--refine", "--engraving"],
            "item-engrave" => ["--character", "--slot", "--engraving"],
            "item-delete" => ["--character", "--slot"],
            "restore" or "restore-character" or "delete" or "class-repair" => ["--character"],
            "move" => ["--character", "--force"],
            "import" => ["--character", "--file"],
            "equipment-set" => ["--character", "--emblem", "--ring", "--none"],
            _ => throw new ArgumentException("Unknown roster command. Run --help for usage.")
        };
        var values = Options(options, allowed);
        int index = Number(Required(values, "--character"));
        var characters = save.ReadRoster();
        if (index >= characters.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var character = characters[index];
        if (verb == "delete") return save.WithoutRosterCharacter(index);
        if (verb == "class-repair") return save.RepairRosterClass(index);
        if (verb == "move")
        {
            string force = Required(values, "--force");
            if (!Enum.TryParse<UnitForce>(force, ignoreCase: false, out var destination)
                || force != destination.ToString()) throw new ArgumentException("Choose Absent, Dead or Lost.");
            return save.WithRosterForce(index, destination);
        }
        if (verb == "restore-character") return save.RestoreRosterCharacter(index);
        if (verb == "equipment-set")
        {
            if (values.Count != 2) throw new ArgumentException("Choose exactly one of --emblem, --ring or --none.");
            if (values.TryGetValue("--none", out string? none))
            {
                if (none != "true") throw new ArgumentException("Use --none true to unequip.");
                return save.WithRosterEquipment(index, new(RosterEquipmentKind.None, 0));
            }
            var kind = values.ContainsKey("--emblem") ? RosterEquipmentKind.Emblem : RosterEquipmentKind.BondRing;
            string key = kind == RosterEquipmentKind.Emblem ? "--emblem" : "--ring";
            if (!uint.TryParse(Required(values, key), out uint instance) || instance == 0)
                throw new ArgumentException("An equipment instance must be a positive integer.");
            return save.WithRosterEquipment(index, new(kind, instance));
        }
        if (verb == "import")
        {
            string path = Required(values, "--file");
            if (new FileInfo(path).Length > RosterTransfer.MaximumFileSize)
                throw new InvalidDataException("A character file must not exceed 1 MiB.");
            return save.ImportRosterCharacter(index, File.ReadAllBytes(path));
        }
        if (verb == "stats-max") return save.MaximizeRosterStats(index);
        if (verb == "skills-max") return save.UnlockAllRosterSkills(index);
        if (verb == "condition")
        {
            if (values.Count == 1) throw new ArgumentException("Provide --internal-level or --hp.");
            int internalLevel = character.Progress.InternalLevel, hp = character.Progress.CurrentHP;
            if (values.TryGetValue("--internal-level", out string? text))
                internalLevel = int.TryParse(text, out int value) ? value : throw new ArgumentException("Internal level must be a whole number.");
            if (values.TryGetValue("--hp", out string? currentHP)) hp = Number(currentHP);
            return save.WithRosterCondition(index, internalLevel, hp);
        }
        if (verb is "skill-unlock" or "skill-remove")
            return save.WithRosterSkill(index, Required(values, "--skill"), verb == "skill-unlock");
        if (verb == "class-skill")
        {
            if (!bool.TryParse(Required(values, "--unlocked"), out bool unlocked))
                throw new ArgumentException("Use true or false for --unlocked.");
            return save.WithRosterClassSkill(index, unlocked);
        }
        if (verb == "skills-equip")
        {
            if (values.Count == 1) throw new ArgumentException("Provide --first or --second.");
            uint? first = character.Progress.EquippedSkills.ElementAtOrDefault(0)?.Hash;
            uint? second = character.Progress.EquippedSkills.ElementAtOrDefault(1)?.Hash;
            if (values.TryGetValue("--first", out string? firstId)) first = SkillHash(firstId);
            if (values.TryGetValue("--second", out string? secondId)) second = SkillHash(secondId);
            return save.WithRosterEquippedSkills(index, first, second);
        }
        if (verb == "proficiencies")
            return save.WithRosterProficiencies(index, WeaponMask(Required(values, "--weapons")));
        if (verb == "class")
        {
            string classId = Required(values, "--class");
            var job = save.ReadRosterClasses(index).FirstOrDefault(job => job.Id == classId)
                ?? throw new ArgumentException("This character cannot use the selected class.");
            uint mask = job.WeaponVariants().First();
            if (values.TryGetValue("--weapons", out string? text))
            {
                mask = WeaponMask(text);
            }
            return save.WithRosterClass(index, classId, mask);
        }
        if (verb == "set")
        {
            if (values.Count == 1)
                throw new ArgumentException("Provide at least one character value to edit.");
            var updated = character.Values;
            if (values.TryGetValue("--level", out string? level))
                updated = updated with { Level = Number(level) };
            if (values.TryGetValue("--experience", out string? experience))
                updated = updated with { Experience = Number(experience) };
            if (values.TryGetValue("--sp", out string? sp))
                updated = updated with { SkillPoints = Number(sp) };
            return save.WithRosterValues(index, updated);
        }
        if (verb is "stat" or "personal-stat")
        {
            string name = Required(values, "--stat");
            if (!Enum.TryParse<RosterStat>(name, ignoreCase: true, out var stat) || int.TryParse(name, out _))
                throw new ArgumentException("Use a character stat name, such as Strength or HP.");
            string text = Required(values, "--value");
            if (verb == "personal-stat")
            {
                if (!int.TryParse(text, out int value))
                    throw new ArgumentException("Personal stats must be whole numbers within their storage limits.");
                return save.WithRosterPersonalStat(index, stat, value);
            }
            return save.WithRosterStat(index, stat, Number(text));
        }
        if (verb == "restore")
            return save.RestoreRosterUses(index);
        int slot = Number(Required(values, "--slot"));
        if (verb == "item-engrave")
            return save.WithRosterEngraving(index, slot, ItemsCommand.EngravingId(Required(values, "--engraving")));
        if (verb == "item-delete")
            return save.DeleteRosterItem(index, slot);
        if (slot >= character.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var old = character.Items[slot].Item;
        string id = values.GetValueOrDefault("--item") ?? ItemCatalog.Find(old?.ItemHash ?? 0)?.Id
            ?? throw new ArgumentException("Choose an item with --item for an empty or unknown slot.");
        var item = ItemCatalog.Get(id);
        bool same = old?.ItemHash == item.Hash;
        int uses = item.MaxUses;
        int refine = 0;
        if (same)
        {
            uses = old!.Uses;
            refine = old.RefineLevel;
        }
        if (values.TryGetValue("--uses", out string? remaining))
            uses = Number(remaining);
        if (values.TryGetValue("--refine", out string? refinement))
            refine = Number(refinement);
        return values.TryGetValue("--engraving", out string? selected)
            ? save.WithRosterItem(index, slot, id, uses, refine, ItemsCommand.EngravingId(selected))
            : save.WithRosterItem(index, slot, id, uses, refine);
    }

    private static Dictionary<string, string> Options(string[] options, string[] allowed)
    {
        if (options.Length == 0 || options.Length % 2 != 0)
            throw new ArgumentException("Provide roster options and their values.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
        {
            string key = options[index];
            if (!allowed.Contains(key))
                throw new ArgumentException($"Unknown roster option: {key}");
            if (!result.TryAdd(key, options[index + 1]))
                throw new ArgumentException($"Duplicate option: {key}");
        }
        return result;
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.GetValueOrDefault(key) ?? throw new ArgumentException($"Missing roster option: {key}");

    private static int Number(string text) => int.TryParse(text, out int value) && value >= 0
        ? value : throw new ArgumentException("Roster values must be nonnegative whole numbers within their game limits.");

    private static uint? SkillHash(string id)
    {
        if (id == "none") return null;
        return RosterCatalog.Skills.FirstOrDefault(skill => skill.Id == id)?.Hash
            ?? throw new ArgumentException("Select a known skill ID, or none to clear a slot.");
    }

    private static string SkillName(uint hash, string language) => RosterCatalog.Skill(hash)?.Name(language) ?? $"Unknown skill (0x{hash:X8})";

    private static uint WeaponMask(string text)
    {
        if (text == "none") return 0;
        uint mask = 0;
        foreach (string name in text.Split(','))
        {
            if (!Enum.TryParse<WeaponType>(name, ignoreCase: true, out var type)
                || !Enum.IsDefined(type) || int.TryParse(name, out _))
                throw new ArgumentException("Use weapon names separated by commas, such as Sword,Lance.");
            uint bit = 1u << (int)type;
            if ((mask & bit) != 0) throw new ArgumentException("Duplicate weapon type.");
            mask |= bit;
        }
        return mask;
    }
}
