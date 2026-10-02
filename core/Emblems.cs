namespace FeeEditor.Core;

public sealed record EmblemBond(string PersonId, int Level, int Experience, IReadOnlyList<uint> InheritedSkills, byte TalkFlags);
public sealed record SavedEmblem(uint InstanceId, string EmblemId, string? PactPartner, IReadOnlyList<EmblemBond> Bonds);
public sealed record BondRing(uint InstanceId, uint RingHash, int StockCount, int? OwnerIndex);
public sealed record CharacterRingLinks(int CharacterIndex, uint EmblemInstance, uint PartnerEmblemInstance, uint RingInstance);
internal sealed record BondLocation(uint EmblemInstance, EmblemBond Bond, int ValuesOffset, int FlagsOffset);

internal sealed class EmblemLayout
{
    public required SaveSection Section { get; init; }
    public required IReadOnlyList<SavedEmblem> Emblems { get; init; }
    public required IReadOnlyList<BondLocation> Bonds { get; init; }

    public static EmblemLayout Read(EngageSave save, byte[] bytes)
    {
        var section = GetSection(save, "GDBD");
        var reader = PoolReader(section, bytes, 2);
        uint count = reader.UInt32();
        if (count > EmblemCreationCatalog.MaxBondHolders || count > reader.Remaining / 11)
            throw new InvalidDataException("Invalid Emblem bond holder count.");
        var emblems = new List<SavedEmblem>();
        var locations = new List<BondLocation>();
        var instances = new HashSet<uint>();
        for (uint index = 0; index < count; index++)
        {
            uint instance = reader.UInt32();
            string gid = reader.String() ?? throw new InvalidDataException("An Emblem has no ID.");
            if (instance == 0 || !instances.Add(instance) || emblems.Any(row => row.EmblemId == gid))
                throw new InvalidDataException("Duplicate or empty Emblem instance.");
            byte special = reader.Byte();
            if (special > 1 || special == 1 && reader.UInt32() != 0)
                throw new InvalidDataException("Unsupported Pact Ring record.");
            string? partner = special == 1 ? reader.String() : null;
            int bonds = reader.UInt16();
            if (bonds > 250 || bonds > reader.Remaining / 20)
                throw new InvalidDataException("Invalid Emblem bond count.");
            var entries = new List<EmblemBond>();
            var people = new HashSet<string>();
            for (int bond = 0; bond < bonds; bond++)
            {
                string pid = reader.String() ?? throw new InvalidDataException("A bond has no character ID.");
                if (!people.Add(pid) || reader.UInt32() != 3)
                    throw new InvalidDataException("Duplicate character bond or unsupported bond version.");
                int offset = reader.Position;
                int level = reader.Byte(), experience = reader.UInt16();
                if (reader.UInt32() != 2)
                    throw new InvalidDataException("Unsupported Emblem inherited-skill version.");
                uint skills = reader.UInt32();
                if (skills > 1280 || skills > (reader.Remaining - 1) / 4)
                    throw new InvalidDataException("Invalid Emblem inherited-skill count.");
                var hashes = new uint[skills];
                for (int skill = 0; skill < hashes.Length; skill++) hashes[skill] = reader.UInt32();
                int flagsOffset = reader.Position;
                var entry = new EmblemBond(pid, level, experience, Array.AsReadOnly(hashes), reader.Byte());
                entries.Add(entry);
                locations.Add(new(instance, entry, offset, flagsOffset));
            }
            emblems.Add(new(instance, gid, partner, entries.AsReadOnly()));
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException("The Emblem bond pool has unrecognized trailing data.");
        return new() { Section = section, Emblems = emblems.AsReadOnly(), Bonds = locations.AsReadOnly() };
    }

    internal static SaveSection GetSection(EngageSave save, string name)
    {
        if (save.Kind != SaveKind.Game || save.FormatVersion != 9)
            throw new InvalidDataException("Emblem editing requires an Engage format-version 9 game save.");
        var sections = save.Sections.Where(section => section.Name == name).ToArray();
        return sections.Length == 1 ? sections[0] : throw new InvalidDataException($"The save must contain one {name} section.");
    }

    internal static SaveReader PoolReader(SaveSection section, byte[] bytes, uint version)
    {
        var reader = new SaveReader(bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.UInt32() != version || reader.UInt32() != 0xcdcdcdcd)
            throw new InvalidDataException($"Unsupported {section.Name} pool version.");
        reader.Skip(24);
        return reader;
    }
}

internal sealed record RingLocation(BondRing Ring, int StockOffset);
internal sealed record BondRingLayout(SaveSection Section, IReadOnlyList<RingLocation> Entries)
{
    public static BondRingLayout Read(EngageSave save, byte[] bytes)
    {
        var section = EmblemLayout.GetSection(save, "RING");
        var reader = EmblemLayout.PoolReader(section, bytes, 3);
        uint count = reader.UInt32();
        if (count > 750 || count != reader.Remaining / 11 || reader.Remaining % 11 != 0)
            throw new InvalidDataException("Invalid bond ring pool size.");
        var owners = save.ReadCharacterRingLinks().Where(link => link.RingInstance != 0)
            .ToDictionary(link => link.RingInstance, link => link.CharacterIndex);
        var instances = new HashSet<uint>();
        var entries = new List<RingLocation>();
        for (uint index = 0; index < count; index++)
        {
            uint instance = reader.UInt32();
            if (instance == 0 || !instances.Add(instance))
                throw new InvalidDataException("Duplicate or empty bond ring instance.");
            uint hash = reader.Reference() ?? throw new InvalidDataException("A bond ring has no data reference.");
            int offset = reader.Position;
            int stock = reader.Byte();
            entries.Add(new(new(instance, hash, stock, owners.TryGetValue(instance, out int owner) ? owner : null), offset));
        }
        if (owners.Keys.Any(instance => !instances.Contains(instance)))
            throw new InvalidDataException("An equipped bond ring is missing from the ring pool.");
        return new(section, entries.AsReadOnly());
    }
}
