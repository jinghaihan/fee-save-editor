namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<AchievementProgress> ReadAchievements()
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        return AchievementCatalog.Achievements.Select(definition =>
        {
            int status = layout.IntegerOffset(definition.Key) is int offset ? unchecked((int)ReadUInt32(_bytes, offset)) : 0;
            if (status is < 0 or > 3) throw new InvalidDataException($"Invalid achievement status: {definition.Id}");
            return new AchievementProgress(definition, (AchievementStatus)status);
        }).ToArray();
    }

    public EngageSave WithUnlockedAchievements(string? id = null)
    {
        if (id is not null) AchievementCatalog.Achievement(id);
        var selected = ReadAchievements().Where(row => id is null || row.Definition.Id == id);
        var values = selected.Where(row => !row.Achieved)
            .ToDictionary(row => row.Definition.Key, _ => (int)AchievementStatus.Cleared);
        if (values.Count == 0) return this;
        var edited = WithGameIntegerValues(values);
        if (edited.ReadAchievements().Any(row => values.ContainsKey(row.Definition.Key) && row.Status != AchievementStatus.Cleared))
            throw new InvalidDataException("Unlocked achievements did not survive serialization.");
        return edited;
    }
}
