using System.Text.Json;

namespace FeeEditor.Core;

public sealed record PersonDefinition(string Id, IReadOnlyDictionary<string, string> Names, IReadOnlyList<int> LimitModifiers,
    int Gender, string BirthClass)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record ClassDefinition(string Id, IReadOnlyDictionary<string, string> Names, int MaxLevel,
    IReadOnlyList<int> BaseStats, IReadOnlyList<int> Limits, int Flags, bool Advanced, string Promotion,
    string LearningSkill, IReadOnlyList<int> Weapons)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
    public IReadOnlyList<uint> WeaponVariants()
    {
        uint mandatory = 0;
        var options = new List<uint>();
        int count = 0;
        for (int index = 1; index < Weapons.Count; index++)
        {
            if (Weapons[index] == 1) mandatory |= 1u << index;
            if (Weapons[index] is 2 or 3) { options.Add(1u << index); count = Weapons[index] - 1; }
        }
        if (count == 0) return [mandatory];
        if (count == 1) return options.Select(mask => mandatory | mask).ToArray();
        return options.SelectMany((mask, index) => options.Skip(index + 1).Select(other => mandatory | mask | other)).ToArray();
    }
}

public sealed record ItemNameDefinition(string Id, IReadOnlyDictionary<string, string> Names, bool EngageOnly)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record SkillDefinition(string Id, IReadOnlyDictionary<string, string> Names, bool Inheritable, string Family, int Tier)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public static class RosterCatalog
{
    private sealed record CatalogData(PersonDefinition[] Persons, ClassDefinition[] Classes, ItemNameDefinition[] ItemNames, SkillDefinition[] Skills);
    private static readonly CatalogData Data = Load();
    private static readonly IReadOnlyDictionary<uint, PersonDefinition> People = Data.Persons.ToDictionary(person => person.Hash);
    private static readonly IReadOnlyDictionary<uint, ClassDefinition> Jobs = Data.Classes.ToDictionary(job => job.Hash);
    private static readonly IReadOnlyDictionary<uint, ItemNameDefinition> ItemNames = Data.ItemNames.ToDictionary(item => item.Hash);
    private static readonly IReadOnlyDictionary<uint, SkillDefinition> SkillNames = Data.Skills.ToDictionary(skill => skill.Hash);
    public static PersonDefinition? Person(uint hash) => People.GetValueOrDefault(hash);
    public static ClassDefinition? Class(uint hash) => Jobs.GetValueOrDefault(hash);
    public static ItemNameDefinition? Item(uint hash) => ItemNames.GetValueOrDefault(hash);
    public static SkillDefinition? Skill(uint hash) => SkillNames.GetValueOrDefault(hash);
    public static IReadOnlyList<SkillDefinition> Skills { get; } = Array.AsReadOnly(Data.Skills);
    public static IReadOnlyList<PersonDefinition> Persons { get; } = Array.AsReadOnly(Data.Persons);
    public static IReadOnlyList<ClassDefinition> Classes { get; } = Array.AsReadOnly(Data.Classes);
    public static IReadOnlyList<ClassDefinition> ClassesFor(uint personHash, int gender)
    {
        var person = Person(personHash) ?? throw new ArgumentException("Select a known playable character.");
        var birth = Class(ItemCatalog.Hash(person.BirthClass));
        return Classes.Where(job => (job.Flags & 1) != 0 && ((job.Flags & 2) != 0
            || job.Id == person.BirthClass || job.Id == birth?.Promotion)
            && ((job.Flags & 4) == 0 || gender == 2)).ToArray();
    }

    private static CatalogData Load()
    {
        using var source = typeof(RosterCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.roster.json")
            ?? throw new InvalidOperationException("The roster catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid roster catalog.");
        if (data.Persons.Length != 41 || data.Classes.Length == 0
            || data.Persons.Any(person => person.LimitModifiers.Count != 11 || !ValidNames(person.Names))
            || data.Classes.Any(job => job.BaseStats.Count != 11 || job.Limits.Count != 11 || job.Weapons.Count != 10
                || job.MaxLevel is < 1 or > 40 || !ValidNames(job.Names))
            || data.ItemNames.Length == 0 || data.ItemNames.Any(item => !ValidNames(item.Names))
            || data.Skills.Length == 0 || data.Skills.Any(skill => !ValidNames(skill.Names)))
            throw new InvalidDataException("The roster catalog has missing translations or invalid limits.");
        return new CatalogData(data.Persons.Select(person => person with
        {
            Names = ReadOnlyNames(person.Names), LimitModifiers = Array.AsReadOnly(person.LimitModifiers.ToArray())
        }).ToArray(), data.Classes.Select(job => job with
        {
            Names = ReadOnlyNames(job.Names), BaseStats = Array.AsReadOnly(job.BaseStats.ToArray()),
            Limits = Array.AsReadOnly(job.Limits.ToArray()), Weapons = Array.AsReadOnly(job.Weapons.ToArray())
        }).ToArray(), data.ItemNames.Select(item => item with { Names = ReadOnlyNames(item.Names) }).ToArray(),
            data.Skills.Select(skill => skill with { Names = ReadOnlyNames(skill.Names) }).ToArray());
    }

    private static IReadOnlyDictionary<string, string> ReadOnlyNames(IReadOnlyDictionary<string, string> names) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(names.ToDictionary(pair => pair.Key, pair => pair.Value));

    private static bool ValidNames(IReadOnlyDictionary<string, string> names) =>
        LanguageCatalog.Codes.All(language => names.TryGetValue(language, out string? name) && !string.IsNullOrWhiteSpace(name));
}
