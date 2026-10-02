using System.Text.Json;

namespace FeeEditor.Core;

public sealed record PersonDefinition(string Id, IReadOnlyDictionary<string, string> Names, IReadOnlyList<int> LimitModifiers)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record ClassDefinition(string Id, IReadOnlyDictionary<string, string> Names, int MaxLevel,
    IReadOnlyList<int> BaseStats, IReadOnlyList<int> Limits)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record ItemNameDefinition(string Id, IReadOnlyDictionary<string, string> Names, bool EngageOnly)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public static class RosterCatalog
{
    private sealed record CatalogData(PersonDefinition[] Persons, ClassDefinition[] Classes, ItemNameDefinition[] ItemNames);
    private static readonly CatalogData Data = Load();
    private static readonly IReadOnlyDictionary<uint, PersonDefinition> People = Data.Persons.ToDictionary(person => person.Hash);
    private static readonly IReadOnlyDictionary<uint, ClassDefinition> Jobs = Data.Classes.ToDictionary(job => job.Hash);
    private static readonly IReadOnlyDictionary<uint, ItemNameDefinition> ItemNames = Data.ItemNames.ToDictionary(item => item.Hash);
    public static PersonDefinition? Person(uint hash) => People.GetValueOrDefault(hash);
    public static ClassDefinition? Class(uint hash) => Jobs.GetValueOrDefault(hash);
    public static ItemNameDefinition? Item(uint hash) => ItemNames.GetValueOrDefault(hash);
    public static IReadOnlyList<PersonDefinition> Persons { get; } = Array.AsReadOnly(Data.Persons);
    public static IReadOnlyList<ClassDefinition> Classes { get; } = Array.AsReadOnly(Data.Classes);

    private static CatalogData Load()
    {
        using var source = typeof(RosterCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.roster.json")
            ?? throw new InvalidOperationException("The roster catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid roster catalog.");
        if (data.Persons.Length != 41 || data.Classes.Length == 0
            || data.Persons.Any(person => person.LimitModifiers.Count != 11 || !ValidNames(person.Names))
            || data.Classes.Any(job => job.BaseStats.Count != 11 || job.Limits.Count != 11
                || job.MaxLevel is < 1 or > 40 || !ValidNames(job.Names))
            || data.ItemNames.Length == 0 || data.ItemNames.Any(item => !ValidNames(item.Names)))
            throw new InvalidDataException("The roster catalog has missing translations or invalid limits.");
        return new CatalogData(data.Persons.Select(person => person with
        {
            Names = ReadOnlyNames(person.Names), LimitModifiers = Array.AsReadOnly(person.LimitModifiers.ToArray())
        }).ToArray(), data.Classes.Select(job => job with
        {
            Names = ReadOnlyNames(job.Names), BaseStats = Array.AsReadOnly(job.BaseStats.ToArray()),
            Limits = Array.AsReadOnly(job.Limits.ToArray())
        }).ToArray(), data.ItemNames.Select(item => item with { Names = ReadOnlyNames(item.Names) }).ToArray());
    }

    private static IReadOnlyDictionary<string, string> ReadOnlyNames(IReadOnlyDictionary<string, string> names) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(names.ToDictionary(pair => pair.Key, pair => pair.Value));

    private static bool ValidNames(IReadOnlyDictionary<string, string> names) =>
        new[] { "en", "zh-Hans" }.All(language => names.TryGetValue(language, out string? name) && !string.IsNullOrWhiteSpace(name));
}
