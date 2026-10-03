namespace FeeEditor.Core;

public sealed record EmblemCondition(uint InstanceId, uint BondHolderId, string EmblemId, int Dirtiness)
{
    public const int MaximumDirtiness = byte.MaxValue;
}

public sealed partial class EngageSave
{
    public EngageSave WithCleanedEmblems()
    {
        var dirty = ReadEmblemConditions().Where(row => row.Dirtiness != 0).Select(row => row.InstanceId).ToHashSet();
        if (dirty.Count == 0) return this;
        var section = EmblemLayout.GetSection(this, "GOD");
        byte[] payload = _bytes.AsSpan(section.PayloadOffset, section.Length).ToArray();
        foreach (var owned in OwnedEmblemLayout.Read(this, _bytes).Where(row => dirty.Contains(row.InstanceId)))
            payload[owned.DirtinessOffset - section.PayloadOffset] = 0;
        var edited = ReplaceSection(section, payload);
        if (edited.ReadEmblemConditions().Any(row => row.Dirtiness != 0))
            throw new InvalidDataException("Edited ring/bracelet dirtiness did not survive serialization.");
        return edited;
    }

    public IReadOnlyList<EmblemCondition> ReadEmblemConditions() => OwnedEmblemLayout.Read(this, _bytes)
        .Select(row => (Owned: row, Definition: EmblemCatalog.Emblems.FirstOrDefault(definition => ItemCatalog.Hash(definition.Id) == row.GodHash)))
        .Where(row => row.Definition is not null && row.Definition.Id != EmblemCatalog.AlearEmblemId)
        .Select(row => new EmblemCondition(row.Owned.InstanceId, row.Owned.BondHolderId, row.Definition!.Id, row.Owned.Dirtiness))
        .ToArray();

    public EngageSave WithEmblemDirtiness(uint instanceId, int dirtiness)
    {
        if (dirtiness is < 0 or > EmblemCondition.MaximumDirtiness)
            throw new ArgumentOutOfRangeException(nameof(dirtiness), "Ring/bracelet dirtiness must be 0–255; 0 is clean.");
        var condition = ReadEmblemConditions().SingleOrDefault(row => row.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an owned, known Emblem ring or bracelet.");
        if (condition.Dirtiness == dirtiness) return this;
        var owned = OwnedEmblemLayout.Read(this, _bytes).Single(row => row.InstanceId == instanceId);
        var section = EmblemLayout.GetSection(this, "GOD");
        byte[] payload = _bytes.AsSpan(section.PayloadOffset, section.Length).ToArray();
        payload[owned.DirtinessOffset - section.PayloadOffset] = (byte)dirtiness;
        var edited = ReplaceSection(section, payload);
        if (edited.ReadEmblemConditions().Single(row => row.InstanceId == instanceId).Dirtiness != dirtiness)
            throw new InvalidDataException("Edited ring/bracelet dirtiness did not survive serialization.");
        return edited;
    }
}
