using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeeEditor.Core;

public enum QuantityItemCategory { ReclassItems, Materials, Ingredients, Gifts, KeyItems }

public sealed record QuantityItemDefinition(string Id, IReadOnlyDictionary<string, string> Names,
    QuantityItemCategory Category, int Maximum)
{
    public string Key => "G_所持_" + Id;
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record QuantityItem(QuantityItemDefinition Definition, int Amount);

public static class QuantityItemCatalog
{
    private sealed record CatalogData(QuantityItemDefinition[] Items);
    public static IReadOnlyList<QuantityItemDefinition> Items { get; } = Load();
    private static readonly IReadOnlyDictionary<string, QuantityItemDefinition> ById = Items.ToDictionary(row => row.Id);
    public static QuantityItemDefinition Get(string id) => ById.GetValueOrDefault(id)
        ?? throw new ArgumentException($"Unknown quantity item: {id}");

    private static IReadOnlyList<QuantityItemDefinition> Load()
    {
        using var stream = typeof(QuantityItemCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.quantity-items.json")
            ?? throw new InvalidOperationException("The quantity item catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        }) ?? throw new InvalidDataException("Invalid quantity item catalog.");
        if (data.Items.Length != 105 || data.Items.Select(row => row.Id).Distinct().Count() != data.Items.Length
            || data.Items.Any(row => !row.Id.StartsWith("IID_", StringComparison.Ordinal) || !Enum.IsDefined(row.Category)
                || row.Maximum is not (999 or 9999) || LanguageCatalog.Codes.Any(language =>
                    !row.Names.TryGetValue(language, out string? name) || string.IsNullOrWhiteSpace(name))))
            throw new InvalidDataException("Invalid quantity item identities, names or limits.");
        return Array.AsReadOnly(data.Items.Select(row => row with
        {
            Names = new ReadOnlyDictionary<string, string>(row.Names.ToDictionary(pair => pair.Key, pair => pair.Value))
        }).ToArray());
    }
}

public sealed partial class EngageSave
{
    public IReadOnlyList<QuantityItem> ReadQuantityItems()
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        return QuantityItemCatalog.Items.Select(definition =>
        {
            int amount = layout.IntegerOffset(definition.Key) is int offset ? unchecked((int)ReadUInt32(_bytes, offset)) : 0;
            if (amount < 0) throw new InvalidDataException($"Negative quantity for {definition.Id}.");
            // Preserve existing out-of-range cheat values on read; validate only requested writes.
            return new QuantityItem(definition, amount);
        }).ToArray();
    }

    public EngageSave WithQuantityItem(string itemId, int amount)
    {
        var definition = QuantityItemCatalog.Get(itemId);
        if (amount < 0 || amount > definition.Maximum)
            throw new ArgumentOutOfRangeException(nameof(amount), $"Amount must be 0–{definition.Maximum} for {definition.Name("en")}.");
        return WithGameIntegerValues(new Dictionary<string, int> { [definition.Key] = amount });
    }

    public EngageSave FillQuantityItems(QuantityItemCategory category)
    {
        if (!Enum.IsDefined(category) || category == QuantityItemCategory.KeyItems)
            throw new ArgumentException("Choose reclass items, materials, ingredients or gifts.", nameof(category));
        return WithGameIntegerValues(QuantityItemCatalog.Items.Where(row => row.Category == category)
            .ToDictionary(row => row.Key, row => row.Maximum));
    }
}
