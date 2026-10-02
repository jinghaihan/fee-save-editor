using System.Text.Json;

namespace FeeEditor.Core;

public static class EmblemCreationCatalog
{
    private sealed record Creation(string Id, string[] Weapons);
    private sealed record CatalogData(Creation[] Emblems);
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> WeaponLists = Load();
    public const int MaxOwnedInstances = 128;
    public const int MaxBondHolders = 64;
    public const int MaxBondsPerHolder = 48;
    public static IReadOnlyList<EmblemDefinition> Emblems { get; } = Array.AsReadOnly(
        EmblemCatalog.Emblems.Where(row => WeaponLists.ContainsKey(row.Id)).ToArray());

    internal static IReadOnlyList<string> Weapons(string id) => WeaponLists.GetValueOrDefault(id)
        ?? throw new ArgumentException("Select a normal base-game or DLC Emblem. Engage+ cannot be created as a normal ring.");

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Load()
    {
        using var source = typeof(EmblemCreationCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.emblem-creation.json")
            ?? throw new InvalidOperationException("The Emblem creation catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid Emblem creation catalog.");
        if (data.Emblems.Length != 19 || data.Emblems.Select(row => row.Id).Distinct().Count() != 19
            || data.Emblems.Any(row => row.Id == EmblemCatalog.AlearEmblemId || EmblemCatalog.Emblem(row.Id) is null
                || row.Weapons.Length is < 1 or > 255 || row.Weapons.Distinct().Count() != row.Weapons.Length
                || row.Weapons.Any(id => !id.StartsWith("IID_", StringComparison.Ordinal))))
            throw new InvalidDataException("Invalid normal Emblem creation data.");
        return data.Emblems.ToDictionary(row => row.Id, row => (IReadOnlyList<string>)Array.AsReadOnly(row.Weapons));
    }
}
