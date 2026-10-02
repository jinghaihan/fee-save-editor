namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<CharacterSupport> ReadSupports() => SupportLayout.Read(this, _bytes).Entries.Select(row => row.Support).ToArray();

    public SupportRank MaximumSupportRank(string key)
    {
        var pair = SupportCatalog.Pair(key) ?? throw new ArgumentException("The support pair is not in the verified catalog.");
        if (!pair.IncludesAlear || !Sections.Any(section => section.Name == "GDBD")) return SupportRank.A;
        var emblem = ReadEmblems().FirstOrDefault(row => row.EmblemId == EmblemCatalog.AlearEmblemId);
        return emblem?.PactPartner == pair.OtherPersonId ? SupportRank.APlus : SupportRank.A;
    }

    public EngageSave WithSupport(string key, SupportRank rank, int? points = null)
    {
        var layout = SupportLayout.Read(this, _bytes);
        var pair = SupportCatalog.Pair(key) ?? throw new ArgumentException("Select a verified support pair.");
        var location = layout.Entries.FirstOrDefault(row => SupportCatalog.Pair(row.Support.Key)?.Key == pair.Key)
            ?? throw new ArgumentException("The pair has no saved support; missing relationships are not added.");
        int value = points ?? pair.PointsForRank(rank);
        if (rank < SupportRank.None || rank > MaximumSupportRank(key))
            throw new ArgumentOutOfRangeException(nameof(rank), "The rank exceeds this pair's legal maximum.");
        if (value < 0 || value > pair.MaximumPoints(rank))
            throw new ArgumentOutOfRangeException(nameof(points), $"Support points must be 0–{pair.MaximumPoints(rank)} at this rank.");
        if (location.Support.Rank == SupportRank.APlus && rank != SupportRank.APlus)
            throw new ArgumentException("A Pact support cannot be downgraded or reassigned.");
        bool rankChanged = location.Support.Rank != rank;
        EngageSave edited = this;
        if (rankChanged || location.Support.Points != value)
        {
            byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
            int offset = location.ValuesOffset - layout.Section.PayloadOffset;
            payload[offset] = (byte)rank;
            payload[offset + 1] = (byte)value;
            edited = ReplaceSection(layout.Section, payload);
        }
        if (rankChanged && pair.IncludesAlear && Sections.Any(section => section.Name == "GDBD"))
        {
            var emblem = edited.ReadEmblems().FirstOrDefault(row => row.EmblemId == EmblemCatalog.AlearEmblemId);
            if (emblem is not null && emblem.Bonds.Any(bond => bond.PersonId == pair.OtherPersonId))
            {
                int level = rank switch { SupportRank.None => 1, SupportRank.C => 5, SupportRank.B => 10,
                    SupportRank.A => 20, SupportRank.APlus => 21, _ => throw new ArgumentOutOfRangeException(nameof(rank)) };
                edited = edited.WithEmblemBond(emblem.InstanceId, pair.OtherPersonId, level);
            }
        }
        return edited;
    }

    public EngageSave WithMaximumSupports(string? key = null)
    {
        var entries = ReadSupports();
        if (key is not null)
        {
            var pair = SupportCatalog.Pair(key) ?? throw new ArgumentException("Select a verified support pair.");
            var entry = entries.FirstOrDefault(row => SupportCatalog.Pair(row.Key)?.Key == pair.Key)
                ?? throw new ArgumentException("Select an existing support pair.");
            var maximum = MaximumSupportRank(key);
            return entry.Rank == maximum ? this : WithSupport(entry.Key, maximum);
        }
        var edited = this;
        foreach (var support in entries.Where(row => SupportCatalog.Pair(row.Key) is not null))
        {
            var maximum = edited.MaximumSupportRank(support.Key);
            if (support.Rank < maximum) edited = edited.WithSupport(support.Key, maximum);
        }
        return edited;
    }
}
