using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public EngageSave WithMissingSBondRings()
    {
        var layout = BondRingLayout.Read(this, _bytes);
        var rings = layout.Entries.Select(entry => entry.Ring).ToList();
        var acquired = new List<BondRingDefinition>();
        foreach (var definition in BondRingCatalog.SRings)
        {
            if (rings.Any(ring => ring.RingHash == definition.Hash && ring.StockCount > 0)) continue;
            AddBondRing(rings, definition);
            acquired.Add(definition);
        }
        if (acquired.Count == 0) return this;
        var edited = WriteBondRingPool(layout, rings);
        foreach (var definition in acquired)
            edited = edited.WithGameVariableFlag(definition.AcquisitionKey, 1 << definition.Rank);
        return edited;
    }

    public EngageSave WithMeldedBondRing(uint instanceId)
    {
        var layout = BondRingLayout.Read(this, _bytes);
        var selected = layout.Entries.FirstOrDefault(entry => entry.Ring.InstanceId == instanceId)?.Ring
            ?? throw new ArgumentException("Select an existing bond ring instance.");
        if (selected.OwnerIndex.HasValue) throw new ArgumentException("Equipped bond rings cannot be used as melding materials.");
        var meld = BondRingCatalog.Melding(selected.RingHash)
            ?? throw new ArgumentException("Only ordinary C, B or A bond rings can be melded.");
        var rings = layout.Entries.Select(entry => entry.Ring).ToList();
        var materials = rings.Where(ring => ring.RingHash == selected.RingHash && !ring.OwnerIndex.HasValue).ToArray();
        if (materials.Any(ring => ring.StockCount > meld.Source.MaxStock)
            || rings.Where(ring => ring.RingHash == selected.RingHash).Sum(ring => ring.StockCount) > meld.Source.MaxStock
            || materials.Sum(ring => ring.StockCount) < meld.RequiredRings)
            throw new ArgumentException("Not enough valid unequipped copies of this bond ring to meld.");
        var main = ReadMainValues();
        if (main.BondFragments < meld.BondFragments) throw new ArgumentException("Not enough bond fragments to meld this ring.");
        int remaining = meld.RequiredRings;
        foreach (var material in materials.OrderByDescending(ring => ring.InstanceId == instanceId))
        {
            int used = Math.Min(remaining, material.StockCount);
            if (used == 0) continue;
            int index = rings.FindIndex(ring => ring.InstanceId == material.InstanceId);
            if (used == material.StockCount) rings.RemoveAt(index);
            else rings[index] = material with { StockCount = material.StockCount - used };
            remaining -= used;
            if (remaining == 0) break;
        }
        AddBondRing(rings, meld.Result);
        var edited = WriteBondRingPool(layout, rings);
        edited = edited.WithGameVariableFlag(meld.Result.AcquisitionKey, 1 << meld.Result.Rank);
        return edited.WithMainValues(edited.ReadMainValues() with { BondFragments = main.BondFragments - meld.BondFragments });
    }

    private static void AddBondRing(List<BondRing> rings, BondRingDefinition definition)
    {
        if (rings.Sum(ring => ring.RingHash == definition.Hash ? ring.StockCount : 0) >= definition.MaxStock)
            throw new ArgumentException("This bond ring already has the maximum total stock of 99.");
        var existing = rings.FirstOrDefault(ring => ring.RingHash == definition.Hash && !ring.OwnerIndex.HasValue);
        if (existing is not null)
        {
            int index = rings.IndexOf(existing);
            rings[index] = existing with { StockCount = existing.StockCount + 1 };
            return;
        }
        if (rings.Count >= BondRingCatalog.MaxInstances
            || rings.Count(ring => !ring.OwnerIndex.HasValue) >= BondRingCatalog.MaxUnequippedInstances
            || rings.Count(ring => ring.OwnerIndex.HasValue) > BondRingCatalog.MaxEquippedInstances
            || rings.Any(ring => ring.InstanceId > BondRingCatalog.MaxInstances))
            throw new ArgumentException("The bond ring pool has no safe free slot.");
        var used = rings.Select(ring => ring.InstanceId).ToHashSet();
        uint instance = Enumerable.Range(1, BondRingCatalog.MaxInstances).Select(value => (uint)value).First(value => !used.Contains(value));
        rings.Add(new(instance, definition.Hash, 1, null));
    }

    private EngageSave WriteBondRingPool(BondRingLayout layout, IReadOnlyList<BondRing> rings)
    {
        byte[] payload = new byte[36 + rings.Count * 11];
        _bytes.AsSpan(layout.Section.PayloadOffset, 32).CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), (uint)rings.Count);
        for (int index = 0; index < rings.Count; index++)
        {
            int offset = 36 + index * 11;
            var ring = rings[index];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), ring.InstanceId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 4), 0xefcd);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 6), ring.RingHash);
            payload[offset + 10] = checked((byte)ring.StockCount);
        }
        return ReplaceSection(layout.Section, payload);
    }
}
