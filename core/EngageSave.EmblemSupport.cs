namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private EngageSave WithAlearBondSupport(string personId, int bondLevel)
    {
        if (personId == EmblemCatalog.AlearPersonId) return this;
        var layout = SupportLayout.Read(this, _bytes);
        var location = layout.Entries.FirstOrDefault(row => row.Support.Key == EmblemCatalog.AlearPersonId + personId
            || row.Support.Key == personId + EmblemCatalog.AlearPersonId)
            ?? throw new ArgumentException("The character has no saved support with Alear; a missing relationship cannot be invented.");
        byte rankValue = bondLevel switch { 1 => 0, 5 => 1, 10 => 2, 20 => 3, 21 => 4,
            _ => throw new ArgumentException("Alear's bond must correspond to a support rank.") };
        if (location.Support.Rank == SupportRank.APlus && rankValue != 4)
            throw new ArgumentException("A Pact support cannot be downgraded through an ordinary bond edit.");
        if ((byte)location.Support.Rank == rankValue) return this;
        var pair = SupportCatalog.Pair(location.Support.Key) ?? throw new ArgumentException("Unverified Alear support pair.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int selected = location.ValuesOffset - layout.Section.PayloadOffset;
        payload[selected] = rankValue;
        payload[selected + 1] = (byte)pair.PointsForRank((SupportRank)rankValue);
        return ReplaceSection(layout.Section, payload);
    }
}
