using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public RosterEquipmentSelection ReadRosterEquipment(int index)
    {
        var link = ReadCharacterRingLinks().FirstOrDefault(link => link.CharacterIndex == index)
            ?? throw new ArgumentOutOfRangeException(nameof(index));
        if (link.RingInstance != 0 && link.EmblemInstance != 0)
            throw new InvalidDataException("A character has both an Emblem and a bond ring equipped.");
        if (link.EmblemInstance != 0) return new(RosterEquipmentKind.Emblem, link.EmblemInstance);
        if (link.RingInstance != 0) return new(RosterEquipmentKind.BondRing, link.RingInstance);
        return new(RosterEquipmentKind.None, 0);
    }

    public IReadOnlyList<RosterEquipmentOption> ReadRosterEquipmentOptions(int index)
    {
        var roster = RosterLayout.Read(this, _bytes);
        var character = GetCharacter(roster, index).Character;
        var links = ReadCharacterRingLinks();
        var gods = OwnedEmblemLayout.Read(this, _bytes);
        var bonds = ReadEmblems();
        var rings = ReadBondRings();
        ValidateEquipmentLinks(links, gods);
        var options = new List<RosterEquipmentOption> { new(new(RosterEquipmentKind.None, 0), null, null, null, 0) };
        if (!EquipmentCharacter(character) || links[index].PartnerEmblemInstance != 0) return options;
        var person = RosterCatalog.Person(character.PersonHash)!;
        foreach (var god in gods)
        {
            var holder = bonds.FirstOrDefault(bond => bond.InstanceId == god.BondHolderId);
            if (god.Darkness || god.Reserved || god.Escaping || holder is null
                || ItemCatalog.Hash(holder.EmblemId) != god.GodHash || EmblemCatalog.Emblem(holder.EmblemId) is null
                || holder.EmblemId == EmblemCatalog.AlearEmblemId || holder.Bonds.All(bond => bond.PersonId != person.Id)
                || links.Any(link => link.PartnerEmblemInstance == god.InstanceId)) continue;
            var owner = links.FirstOrDefault(link => link.EmblemInstance == god.InstanceId);
            if (owner is not null && (!EquipmentCharacter(roster.Characters[owner.CharacterIndex].Character)
                || owner.PartnerEmblemInstance != 0)) continue;
            options.Add(new(new(RosterEquipmentKind.Emblem, god.InstanceId), holder.EmblemId, null, owner?.CharacterIndex, 1));
        }
        foreach (var ring in rings)
        {
            if (ring.StockCount < 1 || ring.StockCount > 99 || EmblemCatalog.Ring(ring.RingHash) is null) continue;
            if (ring.OwnerIndex is int owner && (!EquipmentCharacter(roster.Characters[owner].Character)
                || links[owner].PartnerEmblemInstance != 0)) continue;
            options.Add(new(new(RosterEquipmentKind.BondRing, ring.InstanceId), null, ring.RingHash, ring.OwnerIndex, ring.StockCount));
        }
        return options.AsReadOnly();
    }

    public EngageSave WithRosterEquipment(int index, RosterEquipmentSelection selection)
    {
        if (!Enum.IsDefined(selection.Kind) || (selection.Kind == RosterEquipmentKind.None) != (selection.InstanceId == 0))
            throw new ArgumentException("Choose none, an existing Emblem, or an existing bond ring.");
        var roster = RosterLayout.Read(this, _bytes);
        var character = GetCharacter(roster, index).Character;
        var locations = CharacterEquipmentLayout.Read(this, _bytes);
        var current = ReadRosterEquipment(index);
        if (selection == current) return this;
        if (!EquipmentCharacter(character)) throw new ArgumentException("Select a known playable character.");
        if (locations[index].Links.PartnerEmblemInstance != 0)
            throw new ArgumentException("End the character's Engage+ link in-game before changing equipment.");
        var options = ReadRosterEquipmentOptions(index);
        var selected = options.FirstOrDefault(option => option.Selection == selection)
            ?? throw new ArgumentException("This equipment is unavailable for the selected character.");
        var ringLayout = BondRingLayout.Read(this, _bytes);
        var rings = ringLayout.Entries.Select(entry => entry.Ring).ToList();
        ValidateRingEquipment(rings);
        var originalTotals = RingTotals(rings);
        byte[] unit = _bytes.AsSpan(roster.Section.PayloadOffset, roster.Section.Length).ToArray();
        void SetEquipment(int owner, uint emblem, uint ring)
        {
            var location = locations[owner];
            int offset = location.LinksOffset - roster.Section.PayloadOffset;
            BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(offset), emblem);
            BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(offset + 8), ring);
            unit.AsSpan(location.EngageOffset - roster.Section.PayloadOffset, 2).Clear();
            int status = location.StatusOffset - roster.Section.PayloadOffset;
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(unit.AsSpan(status));
            BinaryPrimitives.WriteUInt64LittleEndian(unit.AsSpan(status), flags & ~0x07800080UL);
        }
        if (current.Kind == RosterEquipmentKind.Emblem && options.All(option => option.Selection != current))
            throw new ArgumentException("The current special or unknown Emblem cannot be reassigned.");
        if (current.Kind == RosterEquipmentKind.BondRing)
            ReturnEquippedRing(rings, current.InstanceId);
        SetEquipment(index, 0, 0);
        uint equippedRing = 0;
        if (selection.Kind == RosterEquipmentKind.BondRing)
        {
            var ring = rings.Single(row => row.InstanceId == selection.InstanceId);
            if (selected.OwnerIndex is int owner)
            {
                SetEquipment(owner, 0, 0);
                rings[rings.IndexOf(ring)] = ring with { OwnerIndex = index };
                equippedRing = ring.InstanceId;
            }
            else if (ring.StockCount == 1)
            {
                rings[rings.IndexOf(ring)] = ring with { OwnerIndex = index };
                equippedRing = ring.InstanceId;
            }
            else
            {
                rings[rings.IndexOf(ring)] = ring with { StockCount = ring.StockCount - 1 };
                equippedRing = FreeRingInstance(rings);
                rings.Add(new(equippedRing, ring.RingHash, 1, index));
            }
        }
        else if (selection.Kind == RosterEquipmentKind.Emblem && selected.OwnerIndex is int owner)
            SetEquipment(owner, 0, 0);
        SetEquipment(index, selection.Kind == RosterEquipmentKind.Emblem ? selection.InstanceId : 0, equippedRing);
        ValidateRingEquipment(rings);
        // Write both sections before validating ownership: neither intermediate view is exposed.
        var edited = WriteBondRingPool(ringLayout, rings);
        edited = edited.ReplaceSection(EmblemLayout.GetSection(edited, "UNIT"), unit);
        var expected = selection.Kind == RosterEquipmentKind.BondRing ? selection with { InstanceId = equippedRing } : selection;
        if (edited.ReadRosterEquipment(index) != expected)
            throw new InvalidDataException("Equipment did not survive serialization.");
        var after = edited.ReadBondRings();
        if (!originalTotals.OrderBy(pair => pair.Key).SequenceEqual(RingTotals(after).OrderBy(pair => pair.Key)))
            throw new InvalidDataException("Equipment changed the saved ring stock.");
        edited.ReadRosterEquipmentOptions(index);
        return edited;
    }

    private static bool EquipmentCharacter(RosterCharacter character) =>
        character.Force is not UnitForce.Enemy and not UnitForce.Temporary && RosterCatalog.Person(character.PersonHash) is not null;

    private static void ValidateEquipmentLinks(IReadOnlyList<CharacterRingLinks> links, IReadOnlyList<OwnedEmblem> gods)
    {
        var used = new HashSet<uint>();
        foreach (var link in links)
        {
            if (link.EmblemInstance != 0 && (!used.Add(link.EmblemInstance) || gods.All(god => god.InstanceId != link.EmblemInstance)))
                throw new InvalidDataException("An Emblem link is missing or has multiple owners.");
            if (link.RingInstance != 0 && link.EmblemInstance != 0)
                throw new InvalidDataException("A character has both an Emblem and a bond ring equipped.");
        }
    }

    private static Dictionary<uint, int> RingTotals(IEnumerable<BondRing> rings) =>
        rings.GroupBy(ring => ring.RingHash).ToDictionary(group => group.Key, group => group.Sum(ring => ring.StockCount));

    private static void ValidateRingEquipment(IReadOnlyList<BondRing> rings)
    {
        if (rings.Count > BondRingCatalog.MaxInstances || rings.Any(ring => ring.InstanceId is 0 or > BondRingCatalog.MaxInstances
            || ring.StockCount is < 0 or > 99 || ring.OwnerIndex.HasValue && ring.StockCount != 1)
            || rings.Count(ring => ring.OwnerIndex.HasValue) > BondRingCatalog.MaxEquippedInstances
            || rings.Count(ring => !ring.OwnerIndex.HasValue) > BondRingCatalog.MaxUnequippedInstances
            || RingTotals(rings).Any(pair => pair.Value > 99))
            throw new InvalidDataException("Invalid bond ring stock, ownership or pool capacity.");
    }

    private static uint FreeRingInstance(IReadOnlyList<BondRing> rings)
    {
        var used = rings.Select(ring => ring.InstanceId).ToHashSet();
        uint instance = Enumerable.Range(1, BondRingCatalog.MaxInstances).Select(value => (uint)value).FirstOrDefault(value => !used.Contains(value));
        if (instance == 0) throw new ArgumentException("The bond ring pool has no free equipment slot.");
        return instance;
    }

    private static void ReturnEquippedRing(List<BondRing> rings, uint instance)
    {
        var ring = rings.Single(row => row.InstanceId == instance);
        if (EmblemCatalog.Ring(ring.RingHash) is null)
            throw new ArgumentException("The current unknown bond ring cannot be reassigned.");
        var stock = rings.FirstOrDefault(row => row.RingHash == ring.RingHash && !row.OwnerIndex.HasValue);
        if (stock is null) rings[rings.IndexOf(ring)] = ring with { OwnerIndex = null };
        else
        {
            rings[rings.IndexOf(stock)] = stock with { StockCount = stock.StockCount + 1 };
            rings.Remove(ring);
        }
    }
}
