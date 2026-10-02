using System.Text.Json;

namespace FeeEditor.Core;

public enum AchievementCategory { Unit, Battle, Somniel, Shop, System }
// Native AchieveData.IsCanGet accepts 1 or 2; the reward exchange sets 3.
// Verified in main build 8C08B9719E085F91847B5E0F935D9488 at 0x27C7720 and 0x1B98864.
public enum AchievementStatus { None, Cleared, Showed, Completed }

public sealed record AchievementDefinition(string Id, AchievementCategory Category,
    Dictionary<string, string> Names, int Reward, string Chapter)
{
    public string Key => "G_実績_" + Id[4..];
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
}

public sealed record AchievementProgress(AchievementDefinition Definition, AchievementStatus Status)
{
    public bool Achieved => Status != AchievementStatus.None;
    public bool RewardClaimed => Status == AchievementStatus.Completed;
    public bool RewardAvailable => Status is AchievementStatus.Cleared or AchievementStatus.Showed;
}

public static class AchievementCatalog
{
    private sealed record CatalogData(AchievementDefinition[] Achievements);
    public static IReadOnlyList<AchievementDefinition> Achievements { get; } = Load();
    public static AchievementDefinition Achievement(string id) => Achievements.SingleOrDefault(row => row.Id == id)
        ?? throw new ArgumentException("Select an ID from the achievement catalog.");

    private static IReadOnlyList<AchievementDefinition> Load()
    {
        using var stream = typeof(AchievementCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.achievements.json")
            ?? throw new InvalidOperationException("The achievement catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid achievement catalog.");
        if (data.Achievements.Length != 765 || data.Achievements.Select(row => row.Id).Distinct().Count() != data.Achievements.Length
            || data.Achievements.Any(row => !row.Id.StartsWith("AID_", StringComparison.Ordinal) || row.Id.Length <= 4
                || !Enum.IsDefined(row.Category) || row.Reward < 0
                || new[] { "en", "zh-Hans" }.Any(language => !row.Names.TryGetValue(language, out string? name)
                    || string.IsNullOrWhiteSpace(name) || name.Contains("\\x", StringComparison.Ordinal))))
            throw new InvalidDataException("Invalid achievement IDs, categories or messages.");
        return Array.AsReadOnly(data.Achievements);
    }
}
