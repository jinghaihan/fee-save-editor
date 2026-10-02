using System.Collections.ObjectModel;
using System.Text.Json;

namespace FeeEditor.Core;

public sealed record EmblemDefinition(string Id, IReadOnlyDictionary<string, string> Names, string? LevelCapVariable, bool Dlc)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record BondRingSkill(string Id, IReadOnlyDictionary<string, string> Names, IReadOnlyDictionary<string, string> Descriptions)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
    public string Description(string language) => Descriptions.GetValueOrDefault(language) ?? Descriptions["en"];
}

public sealed record BondRingDefinition(string Id, IReadOnlyDictionary<string, string> Names, string EmblemId, int Rank, bool SingleRank,
    IReadOnlyList<int> StatBonuses, IReadOnlyList<BondRingSkill> Skills)
{
    public uint Hash => ItemCatalog.Hash(Id);
    public int MaxStock => 99;
    public string RankName => new[] { "C", "B", "A", "S" }[Rank];
    public string Group => Id[5..^2];
    public string AcquisitionKey => "G_指輪_" + Group;
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public static class EmblemCatalog
{
    private sealed record CatalogData(EmblemDefinition[] Emblems, BondRingDefinition[] Rings, int[] BondExperience);
    private static readonly CatalogData Data = Load();
    private static readonly IReadOnlyDictionary<string, EmblemDefinition> Gods = Data.Emblems.ToDictionary(row => row.Id);
    private static readonly IReadOnlyDictionary<uint, BondRingDefinition> RingHashes = Data.Rings.ToDictionary(row => row.Hash);
    public static IReadOnlyList<EmblemDefinition> Emblems { get; } = Array.AsReadOnly(Data.Emblems);
    public static IReadOnlyList<BondRingDefinition> Rings { get; } = Array.AsReadOnly(Data.Rings);
    public const int MaxBondLevel = 20;
    public const string AlearEmblemId = "GID_リュール";
    public const string AlearPersonId = "PID_リュール";
    public const int PactBondLevel = MaxBondLevel + 1;
    public static int MaxBondExperience => Data.BondExperience[^1];
    public static EmblemDefinition? Emblem(string id) => Gods.GetValueOrDefault(id);
    public static BondRingDefinition? Ring(uint hash) => RingHashes.GetValueOrDefault(hash);
    public static int ExperienceForLevel(int level) => level is >= 1 and <= MaxBondLevel
        ? Data.BondExperience[level - 1] : throw new ArgumentOutOfRangeException(nameof(level), "Bond level must be 1–20.");
    public static int LevelForExperience(int experience)
    {
        if (experience < 0 || experience > MaxBondExperience)
            throw new ArgumentOutOfRangeException(nameof(experience), "Bond EXP must be 0–208.");
        return Array.FindLastIndex(Data.BondExperience, threshold => threshold <= experience) + 1;
    }
    public static int MaximumLevel(SavedEmblem emblem, string personId) =>
        emblem.EmblemId == AlearEmblemId && !string.IsNullOrEmpty(emblem.PactPartner) && emblem.PactPartner == personId
            ? PactBondLevel : MaxBondLevel;
    public static int[] SelectableLevels(SavedEmblem emblem, string personId)
    {
        if (emblem.EmblemId != AlearEmblemId) return Enumerable.Range(1, MaxBondLevel).ToArray();
        return MaximumLevel(emblem, personId) == PactBondLevel ? [1, 5, 10, 20, 21] : [1, 5, 10, 20];
    }
    public static int ExperienceForLevel(SavedEmblem emblem, string personId, int level)
    {
        if (!SelectableLevels(emblem, personId).Contains(level))
            throw new ArgumentException("This bond level is not available for the selected character and Emblem.");
        return level == PactBondLevel ? MaxBondExperience + 1 : ExperienceForLevel(level);
    }

    private static CatalogData Load()
    {
        using var source = typeof(EmblemCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.emblems.json")
            ?? throw new InvalidOperationException("The Emblem catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid Emblem catalog.");
        if (data.Emblems.Length != 20 || data.Rings.Length == 0 || data.BondExperience.Length != MaxBondLevel
            || data.BondExperience[0] != 0 || data.BondExperience[^1] != 208
            || data.BondExperience.Zip(data.BondExperience.Skip(1)).Any(pair => pair.First >= pair.Second)
            || data.Emblems.Any(row => !ValidNames(row.Names))
            || data.Rings.Any(row => row.Rank is < 0 or > 3 || !ValidNames(row.Names)
                || row.StatBonuses is null || row.StatBonuses.Count != Enum.GetValues<RosterStat>().Length
                || row.StatBonuses.Any(value => value < 0) || row.Skills is null
                || row.Skills.Any(skill => string.IsNullOrWhiteSpace(skill.Id) || !ValidNames(skill.Names) || !ValidNames(skill.Descriptions))
                || !row.Id.StartsWith("RNID_", StringComparison.Ordinal) || !row.Id.EndsWith("_" + row.RankName, StringComparison.Ordinal)))
            throw new InvalidDataException("The Emblem catalog has invalid names or limits.");
        return new(data.Emblems.Select(row => row with { Names = Freeze(row.Names) }).ToArray(),
            data.Rings.Select(row => row with
            {
                Names = Freeze(row.Names), StatBonuses = Array.AsReadOnly(row.StatBonuses.ToArray()),
                Skills = Array.AsReadOnly(row.Skills.Select(skill => skill with
                { Names = Freeze(skill.Names), Descriptions = Freeze(skill.Descriptions) }).ToArray())
            }).ToArray(), data.BondExperience);
    }

    private static bool ValidNames(IReadOnlyDictionary<string, string> names) => LanguageCatalog.Codes
        .All(language => names.TryGetValue(language, out string? name) && !string.IsNullOrWhiteSpace(name));
    private static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> names) =>
        new ReadOnlyDictionary<string, string>(names.ToDictionary(pair => pair.Key, pair => pair.Value));
}
