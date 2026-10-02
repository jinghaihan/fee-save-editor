using System.Text.Json;

namespace FeeEditor.Core;

public sealed record MinigameRecordDefinition(string Key, Dictionary<string, string> Names)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record MinigameGroup(string Id, Dictionary<string, string> Names, MinigameRecordDefinition[] Records)
{
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record MinigameRecord(MinigameRecordDefinition Definition, int Value);

public static class MinigameCatalog
{
    private sealed record CatalogData(MinigameGroup[] Groups);
    public static IReadOnlyList<MinigameGroup> Groups { get; } = Load();

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
            int value = layout.IntegerOffset(definition.Key) is int offset ? unchecked((int)ReadUInt32(_bytes, offset)) : 0;
            if (value < 0) throw new InvalidDataException($"Negative minigame record: {definition.Key}");
            return new MinigameRecord(definition, value);
        }).ToArray();
    }
}
