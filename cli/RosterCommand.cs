using System.Text.Json;
using System.Text.Json.Serialization;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class RosterCommand
{
    public static int Run(string[] args)
    {
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
        if (args is ["list", var input, .. var displayOptions])
        {
            string language = DisplayLanguage(displayOptions);
            var characters = EngageSave.Load(input).ReadRoster();
            Console.WriteLine(JsonSerializer.Serialize(characters.Select(character => new
            {
                character.Index, character.Force,
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
                    slot.Item
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
        if (language is not "en" and not "zh-Hans")
            throw new ArgumentException("Language must be en or zh-Hans.");
        return language;
    }

    private static EngageSave Edit(EngageSave save, string verb, string[] options)
    {
        string[] allowed = verb switch
        {
            "set" => ["--character", "--level", "--experience", "--sp"],
            "class" => ["--character", "--class", "--weapons"],
            "condition" => ["--character", "--internal-level", "--hp"],
            "skill-unlock" or "skill-remove" => ["--character", "--skill"],
            "skills-max" => ["--character"],
            "skills-equip" => ["--character", "--first", "--second"],
            "class-skill" => ["--character", "--unlocked"],
            "proficiencies" => ["--character", "--weapons"],
            "stat" => ["--character", "--stat", "--value"],
            "item-set" => ["--character", "--slot", "--item", "--uses", "--refine"],
            "item-delete" => ["--character", "--slot"],
            "restore" => ["--character"],
            _ => throw new ArgumentException("Unknown roster command. Run --help for usage.")
        };
        var values = Options(options, allowed);
        int index = Number(Required(values, "--character"));
        var characters = save.ReadRoster();
        if (index >= characters.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var character = characters[index];
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
        if (verb == "stat")
        {
            string name = Required(values, "--stat");
            if (!Enum.TryParse<RosterStat>(name, ignoreCase: true, out var stat) || int.TryParse(name, out _))
                throw new ArgumentException("Use a character stat name, such as Strength or HP.");
            return save.WithRosterStat(index, stat, Number(Required(values, "--value")));
        }
        if (verb == "restore")
            return save.RestoreRosterUses(index);
        int slot = Number(Required(values, "--slot"));
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
        return save.WithRosterItem(index, slot, id, uses, refine);
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
