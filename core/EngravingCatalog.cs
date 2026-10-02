using System.Text.Json;

namespace FeeEditor.Core;

public sealed record EngravingDefinition(string Id, IReadOnlyList<string> Aliases,
    int Power, int Weight, int Hit, int Critical, int Avoid, int Secure)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public string Name(string language) => EmblemCatalog.Emblem(Id)!.Name(language);
}

public static class EngravingCatalog
{
    private sealed record CatalogData(EngravingDefinition[] Engravings, string[] Weapons);
    private static readonly CatalogData Data = Load();
    private static readonly IReadOnlyDictionary<uint, EngravingDefinition> ByHash = Data.Engravings
        .SelectMany(row => row.Aliases.Prepend(row.Id).Select(id => new KeyValuePair<uint, EngravingDefinition>(ItemCatalog.Hash(id), row)))
        .ToDictionary(row => row.Key, row => row.Value);
    private static readonly HashSet<uint> Weapons = Data.Weapons.Select(ItemCatalog.Hash).ToHashSet();
    public static IReadOnlyList<EngravingDefinition> Engravings { get; } = Array.AsReadOnly(Data.Engravings);
    public static EngravingDefinition? Find(uint hash) => ByHash.GetValueOrDefault(hash);
    public static EngravingDefinition Get(string id) => Find(ItemCatalog.Hash(id)) is { } row
        && row.Aliases.Prepend(row.Id).Contains(id, StringComparer.Ordinal) ? row
        : throw new ArgumentException("Choose a verified Emblem engraving.");
    public static bool CanEngrave(uint itemHash) => Weapons.Contains(itemHash);

    private static CatalogData Load()
    {
        using var source = typeof(EngravingCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.engravings.json")
            ?? throw new InvalidOperationException("The engraving catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid engraving catalog.");
        if (data.Engravings.Length != 20 || data.Weapons.Length == 0
            || data.Engravings.Any(row => EmblemCatalog.Emblem(row.Id) is null
                || new[] { row.Power, row.Weight, row.Hit, row.Critical, row.Avoid, row.Secure }.Any(value => value is < -100 or > 100)))
            throw new InvalidDataException("Incomplete engraving identities or invalid effects.");
        return data with { Engravings = data.Engravings.Select(row => row with
            { Aliases = Array.AsReadOnly(row.Aliases.ToArray()) }).ToArray() };
    }
}
