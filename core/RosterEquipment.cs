namespace FeeEditor.Core;

public enum RosterEquipmentKind { None, Emblem, BondRing }
public sealed record RosterEquipmentSelection(RosterEquipmentKind Kind, uint InstanceId);
public sealed record RosterEquipmentOption(RosterEquipmentSelection Selection, string? EmblemId,
    uint? RingHash, int? OwnerIndex, int StockCount);

internal sealed record CharacterEquipmentLocation(CharacterRingLinks Links, int LinksOffset,
    int EngageOffset, int StatusOffset, uint? Target);

internal static class CharacterEquipmentLayout
{
    public static IReadOnlyList<CharacterEquipmentLocation> Read(EngageSave save, byte[] bytes)
    {
        if (!save.Sections.Any(section => section.Name == "UNIT")) return [];
        var locations = new List<CharacterEquipmentLocation>();
        var usedRings = new HashSet<uint>();
        foreach (var character in RosterLayout.Read(save, bytes).Characters)
        {
            var reader = new SaveReader(bytes, character.Progress.TailStart, character.End);
            reader.Skip(2);
            if (reader.Byte() != 4) throw new InvalidDataException("Unsupported unit battle-data version.");
            int stats = reader.Byte();
            if (stats > 43) throw new InvalidDataException("Invalid unit battle-data count.");
            reader.Skip(stats * 8 + 2);
            int weapons = reader.Byte();
            if (weapons > 10) throw new InvalidDataException("Invalid unit weapon-rank count.");
            reader.Skip(weapons + 2);
            int offset = reader.Position;
            uint emblem = reader.UInt32(), partner = reader.UInt32(), ring = reader.UInt32();
            reader.Skip(4);
            byte target = reader.Byte();
            if (target > 1) throw new InvalidDataException("Invalid optional unit target.");
            uint? targetHash = target == 1 ? reader.Reference() : null;
            reader.Skip(14);
            if (reader.Remaining != 0) throw new InvalidDataException("Unrecognized character equipment trailer.");
            if (ring != 0 && !usedRings.Add(ring)) throw new InvalidDataException("A ring instance has multiple owners.");
            locations.Add(new(new(character.Character.Index, emblem, partner, ring), offset,
                character.Progress.TailStart, character.Start + 36, targetHash));
        }
        return locations.AsReadOnly();
    }
}

internal sealed record OwnedEmblem(uint InstanceId, uint GodHash, uint BondHolderId,
    bool Darkness, bool Reserved, bool Escaping, byte Dirtiness, int DirtinessOffset, int Start, int End);

internal static class OwnedEmblemLayout
{
    public static IReadOnlyList<OwnedEmblem> Read(EngageSave save, byte[] bytes)
    {
        var section = EmblemLayout.GetSection(save, "GOD");
        var reader = EmblemLayout.PoolReader(section, bytes, 8);
        uint count = reader.UInt32();
        if (count > EmblemCreationCatalog.MaxOwnedInstances) throw new InvalidDataException("Invalid owned Emblem count.");
        var result = new List<OwnedEmblem>();
        var instances = new HashSet<uint>();
        for (uint index = 0; index < count; index++)
        {
            int start = reader.Position;
            uint instance = reader.UInt32();
            if (instance == 0 || !instances.Add(instance)) throw new InvalidDataException("Invalid owned Emblem instance.");
            uint hash = reader.Reference() ?? throw new InvalidDataException("An Emblem has no game-data reference.");
            uint holder = reader.UInt32();
            byte dark = reader.Byte(), reserved = reader.Byte(), escaping = reader.Byte();
            if (dark > 1 || reserved > 1 || escaping > 1) throw new InvalidDataException("Invalid owned Emblem flags.");
            int dirtinessOffset = reader.Position;
            byte dirtiness = reader.Byte();
            int synchro = reader.Byte();
            for (int row = 0; row < synchro; row++)
            {
                if (reader.String() is null) throw new InvalidDataException("Invalid Emblem synchro key.");
                reader.Skip(2);
            }
            int weapons = reader.Byte();
            for (int row = 0; row < weapons; row++)
            {
                if (reader.String() is null) throw new InvalidDataException("Invalid Emblem weapon key.");
                reader.Skip(10);
                reader.String();
            }
            result.Add(new(instance, hash, holder, dark != 0, reserved != 0, escaping != 0, dirtiness, dirtinessOffset, start, reader.Position));
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unrecognized owned Emblem data.");
        return result.AsReadOnly();
    }
}
