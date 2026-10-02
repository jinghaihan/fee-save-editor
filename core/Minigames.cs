using System.Text.Json;

namespace FeeEditor.Core;

public sealed record MinigameRecordDefinition(string Key, Dictionary<string, string> Names)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
    public bool IsFishing => Key.StartsWith("G_Fishing_", StringComparison.Ordinal);
    public string? RankKey
    {
        get
        {
            if (IsFishing) return Key[..^6] + "_BestRank";
            if (Key.StartsWith("G_DragonRide", StringComparison.Ordinal)) return Key[..^5] + "RankNum";
            return null;
        }
    }
    public string? SizeKey => IsFishing ? Key[..^6] + "_BestSize" : null;
    public IReadOnlyList<string> Ranks
    {
        get
        {
            if (IsFishing) return MinigameCatalog.FishRanks;
            if (RankKey is not null) return MinigameCatalog.WyvernRanks;
            return Array.Empty<string>();
        }
    }
    public int MaximumRank => Ranks.Count - 1;
}

public sealed record MinigameGroup(string Id, Dictionary<string, string> Names, MinigameRecordDefinition[] Records)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record MinigameValues(int Value, int? Rank = null, int? BestSize = null);
public sealed record MinigameRecord(MinigameRecordDefinition Definition, int Value, int? Rank = null, int? BestSize = null)
{
    public MinigameValues Values => new(Value, Rank, BestSize);
}

public static class MinigameCatalog
{
    private sealed record CatalogData(MinigameGroup[] Groups);
    public static IReadOnlyList<MinigameGroup> Groups { get; } = Load();
    public static IReadOnlyList<string> WyvernRanks { get; } = Array.AsReadOnly(new[] { "None", "SSS", "SS", "S", "A", "B", "C", "D", "E", "F" });
    public static IReadOnlyList<string> FishRanks { get; } = Array.AsReadOnly(new[] { "Tiny", "Small", "Middle", "Large", "Big", "Giant" });

    private static IReadOnlyList<MinigameGroup> Load()
    {
        using var stream = typeof(MinigameCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.minigames.json")
            ?? throw new InvalidOperationException("The minigame catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid minigame catalog.");
        var records = data.Groups.SelectMany(group => group.Records).ToArray();
        if (!data.Groups.Select(group => group.Id).SequenceEqual(new[] { "PushUps", "SitUps", "Squats", "WyvernRide", "Fishing" })
            || !data.Groups.Select(group => group.Records.Length).SequenceEqual(new[] { 4, 4, 4, 3, 20 })
            || records.Select(row => row.Key).Distinct().Count() != 35
            || records.Any(row => !row.Key.StartsWith("G_", StringComparison.Ordinal))
            || data.Groups.Select(group => group.Names).Concat(records.Select(row => row.Names))
                .Any(names => new[] { "en", "zh-Hans" }.Any(language => !names.TryGetValue(language, out string? name)
                    || string.IsNullOrWhiteSpace(name))))
            throw new InvalidDataException("Invalid minigame groups, record keys or names.");
        return Array.AsReadOnly(data.Groups);
    }
}

public sealed partial class EngageSave
{
    public IReadOnlyList<MinigameRecord> ReadMinigameRecords()
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        return MinigameCatalog.Groups.SelectMany(group => group.Records).Select(definition =>
        {
            int value = ReadRecordInteger(layout, definition.Key);
            int? rank = definition.RankKey is string rankKey ? ReadRecordInteger(layout, rankKey) : null;
            int? size = definition.SizeKey is string sizeKey ? ReadRecordInteger(layout, sizeKey) : null;
            if (rank > definition.MaximumRank) throw new InvalidDataException($"Invalid minigame rank: {definition.Key}");
            return new MinigameRecord(definition, value, rank, size);
        }).ToArray();
    }

    private int ReadRecordInteger(GameVariableLayout layout, string key)
    {
        int value = layout.IntegerOffset(key) is int offset ? unchecked((int)ReadUInt32(_bytes, offset)) : 0;
        if (value < 0) throw new InvalidDataException($"Negative minigame record: {key}");
        return value;
    }

    public EngageSave WithMinigameRecords(IReadOnlyDictionary<string, MinigameValues> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var current = ReadMinigameRecords().ToDictionary(row => row.Definition.Key, StringComparer.Ordinal);
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, record) in records)
        {
            if (!current.TryGetValue(key, out var row)) throw new ArgumentException($"Unknown minigame record: {key}");
            ArgumentNullException.ThrowIfNull(record);
            if (record.Value < 0 || record.BestSize < 0 || record.Rank < 0 || record.Rank > row.Definition.MaximumRank)
                throw new ArgumentOutOfRangeException(nameof(records), "Minigame values must be nonnegative and ranks must be valid.");
            values.Add(key, record.Value);
            if (record.Rank is int rank)
                values.Add(row.Definition.RankKey ?? throw new ArgumentException("This record has no stored rank."), rank);
            if (record.BestSize is int size)
                values.Add(row.Definition.SizeKey ?? throw new ArgumentException("Only fishing records store a best size."), size);
        }
        var edited = WithGameIntegerValues(values);
        var actual = edited.ReadMinigameRecords().ToDictionary(row => row.Definition.Key, StringComparer.Ordinal);
        foreach (var (key, record) in records)
        {
            var row = actual[key];
            if (row.Value != record.Value || (record.Rank.HasValue && row.Rank != record.Rank)
                || (record.BestSize.HasValue && row.BestSize != record.BestSize))
                throw new InvalidDataException("Edited minigame records did not survive serialization.");
        }
        return edited;
    }
}
