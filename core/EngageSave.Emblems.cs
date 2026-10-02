using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<SavedEmblem> ReadEmblems() => EmblemLayout.Read(this, _bytes).Emblems;
    public IReadOnlyList<BondRing> ReadBondRings() => BondRingLayout.Read(this, _bytes).Entries.Select(entry => entry.Ring).ToArray();

    public IReadOnlyList<CharacterRingLinks> ReadCharacterRingLinks() =>
        CharacterEquipmentLayout.Read(this, _bytes).Select(location => location.Links).ToArray();

    public EngageSave WithEmblemBond(uint instanceId, string personId, int level, int? experience = null)
    {
        if (RosterCatalog.Person(ItemCatalog.Hash(personId)) is null)
            throw new ArgumentException("Select a known playable character bond.");
        var layout = EmblemLayout.Read(this, _bytes);
        var emblem = layout.Emblems.FirstOrDefault(row => row.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an existing Emblem instance.");
        var definition = EmblemCatalog.Emblem(emblem.EmblemId)
            ?? throw new ArgumentException("The selected Emblem is not in the verified catalog.");
        var location = layout.Bonds.FirstOrDefault(row => row.EmblemInstance == instanceId && row.Bond.PersonId == personId)
            ?? throw new ArgumentException("The selected character has no saved bond with this Emblem.");
        int threshold = EmblemCatalog.ExperienceForLevel(emblem, personId, level);
        int exp = experience ?? threshold;
        if (level == EmblemCatalog.PactBondLevel ? exp != threshold : EmblemCatalog.LevelForExperience(exp) != level)
            throw new ArgumentException("Bond level and EXP do not match the game's thresholds.");
        if (location.Bond.Level > EmblemCatalog.MaxBondLevel && level != EmblemCatalog.PactBondLevel)
            throw new ArgumentException("A special Pact Ring bond cannot be replaced with a normal bond level.");
        if (location.Bond.Level > EmblemCatalog.PactBondLevel)
            throw new ArgumentException("An unrecognized special bond cannot be edited.");
        bool bondChanged = location.Bond.Level != level || location.Bond.Experience != exp;
        if (!bondChanged)
        {
            var unchanged = this;
            if (level >= 11 && definition.LevelCapVariable is string capKey)
                unchanged = unchanged.WithEmblemCapVariable(capKey);
            return emblem.EmblemId == EmblemCatalog.AlearEmblemId ? unchanged.WithAlearBondSupport(personId, level) : unchanged;
        }
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int offset = location.ValuesOffset - layout.Section.PayloadOffset;
        payload[offset] = (byte)level;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 1), (ushort)exp);
        if (location.Bond.Level != level)
        {
            int flags = location.FlagsOffset - layout.Section.PayloadOffset;
            int talkFlags = (level >= 5 ? 2 : 0) | (level >= 10 ? 4 : 0) | (level >= EmblemCatalog.MaxBondLevel ? 8 : 0);
            payload[flags] = (byte)((payload[flags] & ~14) | talkFlags);
        }
        var edited = ReplaceSection(layout.Section, payload);
        if (level >= 11 && definition.LevelCapVariable is string key)
            edited = edited.WithEmblemCapVariable(key);
        if (emblem.EmblemId == EmblemCatalog.AlearEmblemId)
            edited = edited.WithAlearBondSupport(personId, level);
        return edited;
    }

    public EngageSave WithMaximumEmblemBonds(uint instanceId, string? personId = null)
    {
        var emblem = ReadEmblems().FirstOrDefault(row => row.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an existing Emblem instance.");
        if (EmblemCatalog.Emblem(emblem.EmblemId) is null)
            throw new ArgumentException("Unknown Emblem limits cannot be guessed.");
        var bonds = emblem.Bonds.Where(bond => personId is null || bond.PersonId == personId).ToArray();
        if (personId is not null && bonds.Length != 1)
            throw new ArgumentException("Select an existing character bond.");
        var edited = this;
        foreach (var bond in bonds)
        {
            if (RosterCatalog.Person(ItemCatalog.Hash(bond.PersonId)) is null || bond.Level > EmblemCatalog.PactBondLevel)
            {
                if (personId is not null) throw new ArgumentException("Unknown character or special bond limits cannot be guessed.");
                continue;
            }
            int maximum = EmblemCatalog.MaximumLevel(emblem, bond.PersonId);
            if (bond.Level > maximum)
            {
                if (personId is not null) throw new ArgumentException("The saved special bond does not match the verified Pact partner.");
                continue;
            }
            edited = edited.WithEmblemBond(instanceId, bond.PersonId, maximum);
        }
        return edited;
    }

    public int MaximumBondRingStock(uint instanceId)
    {
        var rings = ReadBondRings();
        var ring = rings.FirstOrDefault(row => row.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an existing bond ring instance.");
        return MaximumBondRingStock(ring, rings);
    }

    private static int MaximumBondRingStock(BondRing ring, IEnumerable<BondRing> rings)
    {
        var definition = EmblemCatalog.Ring(ring.RingHash)
            ?? throw new ArgumentException("Unknown bond ring limits cannot be guessed.");
        if (ring.OwnerIndex.HasValue) return 1;
        int otherCopies = rings.Where(row => row.RingHash == ring.RingHash && row.InstanceId != ring.InstanceId)
            .Sum(row => row.StockCount);
        return Math.Max(0, definition.MaxStock - otherCopies);
    }

    public EngageSave WithBondRingStock(uint instanceId, int stock)
    {
        var layout = BondRingLayout.Read(this, _bytes);
        var location = layout.Entries.FirstOrDefault(entry => entry.Ring.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an existing bond ring instance.");
        var ring = location.Ring;
        var definition = EmblemCatalog.Ring(ring.RingHash)
            ?? throw new ArgumentException("Unknown bond ring limits cannot be guessed.");
        int maximum = ring.OwnerIndex.HasValue ? 1 : definition.MaxStock;
        int minimum = ring.OwnerIndex.HasValue ? 1 : 0;
        if (stock < minimum || stock > maximum)
            throw new ArgumentOutOfRangeException(nameof(stock), $"Ring stock must be {minimum}–{maximum}.");
        if (stock == ring.StockCount) return this;
        maximum = MaximumBondRingStock(ring, layout.Entries.Select(entry => entry.Ring));
        if (stock > maximum)
            throw new ArgumentOutOfRangeException(nameof(stock), $"This stack can hold at most {maximum} copies; identical rings, including equipped copies, share a {definition.MaxStock}-copy limit.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        payload[location.StockOffset - layout.Section.PayloadOffset] = (byte)stock;
        return ReplaceSection(layout.Section, payload);
    }
}
