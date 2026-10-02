using System.Text.Json;

namespace FeeEditor.Core;

public sealed record ItemDefinition(string Id, string English, string Chinese, int Kind, int MaxUses, int MaxRefine)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public bool UnlimitedUses => MaxUses == byte.MaxValue;
    public string Name(string language) => language switch
    {
        "en" => English,
        "zh-Hans" => Chinese,
        _ => throw new ArgumentException("Unsupported item language.", nameof(language))
    };
}

public static class ItemCatalog
{
    private sealed record CatalogData(string SourceRevision, ItemDefinition[] Items);
    private static readonly IReadOnlyList<ItemDefinition> Definitions = Load();
    private static readonly IReadOnlyDictionary<uint, ItemDefinition> ByHash = Definitions.ToDictionary(item => item.Hash);
    private static readonly IReadOnlyDictionary<string, ItemDefinition> ById = Definitions.ToDictionary(item => item.Id);
    public static IReadOnlyList<ItemDefinition> Items => Definitions;
    public static ItemDefinition? Find(uint hash) => ByHash.GetValueOrDefault(hash);
    public static ItemDefinition Get(string id) => ById.TryGetValue(id, out var item) ? item
        : throw new ArgumentException($"Unknown or unsupported convoy item: {id}");

    public static uint Hash(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        uint hash = 2166136261;
        foreach (char character in id)
            hash = unchecked(hash * 16777619) ^ character;
        return hash;
    }

    private static IReadOnlyList<ItemDefinition> Load()
    {
        using var source = typeof(ItemCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.items.json")
            ?? throw new InvalidOperationException("The item catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source)
            ?? throw new InvalidDataException("The item catalog is invalid.");
        if (data.Items.Length == 0 || data.Items.Any(item => string.IsNullOrWhiteSpace(item.Id)
            || string.IsNullOrWhiteSpace(item.English) || string.IsNullOrWhiteSpace(item.Chinese)
            || item.MaxUses is < 1 or > 255 || item.MaxRefine is < 0 or > 5)
            || data.Items.Select(item => item.Hash).Distinct().Count() != data.Items.Length)
            throw new InvalidDataException("The item catalog has invalid bounds or ambiguous hashes.");
        return Array.AsReadOnly(data.Items);
    }
}
