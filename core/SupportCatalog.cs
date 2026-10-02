using System.Text.Json;

namespace FeeEditor.Core;

public enum SupportRank { None, C, B, A, APlus }

public sealed record SupportDefinition(string FirstPersonId, string SecondPersonId, IReadOnlyList<int> Thresholds)
{
    public string Key => FirstPersonId + SecondPersonId;
    public bool IncludesAlear => FirstPersonId == EmblemCatalog.AlearPersonId || SecondPersonId == EmblemCatalog.AlearPersonId;
    public string OtherPersonId => FirstPersonId == EmblemCatalog.AlearPersonId ? SecondPersonId : FirstPersonId;
    public int PointsForRank(SupportRank rank) => rank switch
    {
        SupportRank.None => 0,
        SupportRank.C or SupportRank.B or SupportRank.A => Thresholds[(int)rank - 1],
        SupportRank.APlus => 99,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };
    public int MaximumPoints(SupportRank rank) => rank switch
    {
        SupportRank.None or SupportRank.C or SupportRank.B => Thresholds[(int)rank] - 1,
        SupportRank.A or SupportRank.APlus => 99,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };
    public SupportRank RankForPoints(int points) => points is >= 0 and <= 99
        ? (SupportRank)Thresholds.Count(threshold => points >= threshold)
        : throw new ArgumentOutOfRangeException(nameof(points), "Support points must be 0–99.");
}

public static class SupportCatalog
{
    private sealed record CatalogData(SupportDefinition[] Pairs);
    public static IReadOnlyList<SupportDefinition> Pairs { get; } = Load();
    private static readonly IReadOnlyDictionary<string, SupportDefinition> ByKey = Pairs
        .SelectMany(pair => new[] { new KeyValuePair<string, SupportDefinition>(pair.Key, pair),
            new KeyValuePair<string, SupportDefinition>(pair.SecondPersonId + pair.FirstPersonId, pair) })
        .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
    public static SupportDefinition? Pair(string key) => ByKey.GetValueOrDefault(key);

    private static IReadOnlyList<SupportDefinition> Load()
    {
        using var source = typeof(SupportCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.supports.json")
            ?? throw new InvalidOperationException("The support catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(source) ?? throw new InvalidDataException("Invalid support catalog.");
        if (data.Pairs.Length != 231 || data.Pairs.Any(pair => pair.FirstPersonId == pair.SecondPersonId
            || RosterCatalog.Person(ItemCatalog.Hash(pair.FirstPersonId)) is null
            || RosterCatalog.Person(ItemCatalog.Hash(pair.SecondPersonId)) is null
            || pair.Thresholds.Count != 3 || pair.Thresholds[0] <= 0 || pair.Thresholds[2] > 100
            || pair.Thresholds.Zip(pair.Thresholds.Skip(1)).Any(row => row.First >= row.Second)))
            throw new InvalidDataException("Incomplete support pairs or invalid thresholds.");
        return Array.AsReadOnly(data.Pairs.Select(pair => pair with
        { Thresholds = Array.AsReadOnly(pair.Thresholds.ToArray()) }).ToArray());
    }
}
