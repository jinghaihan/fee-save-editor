using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<SavedEmblem> ReadEmblems() => EmblemLayout.Read(this, _bytes).Emblems;
    public IReadOnlyList<BondRing> ReadBondRings() => BondRingLayout.Read(this, _bytes).Entries.Select(entry => entry.Ring).ToArray();

    public IReadOnlyList<CharacterRingLinks> ReadCharacterRingLinks()
    {
        if (!Sections.Any(section => section.Name == "UNIT")) return Array.Empty<CharacterRingLinks>();
        var links = new List<CharacterRingLinks>();
        var usedRings = new HashSet<uint>();
        foreach (var character in RosterLayout.Read(this, _bytes).Characters)
        {
            var reader = new SaveReader(_bytes, character.Progress.TailStart, character.End);
            reader.Skip(2);
            if (reader.Byte() != 4) throw new InvalidDataException("Unsupported unit battle-data version.");
            int stats = reader.Byte();
            if (stats > 43) throw new InvalidDataException("Invalid unit battle-data count.");
            reader.Skip(stats * 8 + 2);
            int weapons = reader.Byte();
            if (weapons > 10) throw new InvalidDataException("Invalid unit weapon-rank count.");
            reader.Skip(weapons + 2);
            uint emblem = reader.UInt32(), partnerEmblem = reader.UInt32(), ring = reader.UInt32();
            reader.Skip(4);
            byte target = reader.Byte();
            if (target > 1) throw new InvalidDataException("Invalid optional unit target.");
            if (target == 1) reader.Reference();
            reader.Skip(14);
            if (reader.Remaining != 0) throw new InvalidDataException("Unrecognized character equipment trailer.");
            if (ring != 0 && !usedRings.Add(ring)) throw new InvalidDataException("A ring instance has multiple owners.");
            links.Add(new(character.Character.Index, emblem, partnerEmblem, ring));
        }
        return links.AsReadOnly();
    }

    public EngageSave WithEmblemBond(uint instanceId, string personId, int level, int? experience = null)
    {
        if (RosterCatalog.Person(ItemCatalog.Hash(personId)) is null)
            throw new ArgumentException("Select a known playable character bond.");
        int exp = experience ?? EmblemCatalog.ExperienceForLevel(level);
        if (EmblemCatalog.LevelForExperience(exp) != level)
            throw new ArgumentException("Bond level and EXP do not match the game's thresholds.");
        var layout = EmblemLayout.Read(this, _bytes);
        var emblem = layout.Emblems.FirstOrDefault(row => row.InstanceId == instanceId)
            ?? throw new ArgumentException("Select an existing Emblem instance.");
        var definition = EmblemCatalog.Emblem(emblem.EmblemId)
            ?? throw new ArgumentException("The selected Emblem is not in the verified catalog.");
        var location = layout.Bonds.FirstOrDefault(row => row.EmblemInstance == instanceId && row.Bond.PersonId == personId)
            ?? throw new ArgumentException("The selected character has no saved bond with this Emblem.");
        if (location.Bond.Level > 20)
            throw new ArgumentException("A special Pact Ring bond cannot be replaced with a normal bond level.");
        if (location.Bond.Level == level && location.Bond.Experience == exp) return this;
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int offset = location.ValuesOffset - layout.Section.PayloadOffset;
        payload[offset] = (byte)level;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 1), (ushort)exp);
        if (location.Bond.Level != level)
        {
            int flags = location.FlagsOffset - layout.Section.PayloadOffset;
            int talkFlags = (level >= 5 ? 2 : 0) | (level >= 10 ? 4 : 0) | (level >= 20 ? 8 : 0);
            payload[flags] = (byte)((payload[flags] & ~14) | talkFlags);
        }
        var edited = ReplaceSection(layout.Section, payload);
        if (level >= 11 && definition.LevelCapVariable is string key)
            edited = edited.WithEmblemCapVariable(key);
        return edited;
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
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        payload[location.StockOffset - layout.Section.PayloadOffset] = (byte)stock;
        return ReplaceSection(layout.Section, payload);
    }
}
