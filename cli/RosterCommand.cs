using System.Text.Json;
using System.Text.Json.Serialization;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class RosterCommand
{
    public static int Run(string[] args)
    {
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
}
