using System.Buffers.Binary;
using System.Text;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<EmblemDefinition> ReadMissingEmblems()
    {
        var gods = OwnedEmblemLayout.Read(this, _bytes);
        var holders = ReadEmblems();
        ValidateEmblemCreation(gods, holders);
        var owned = gods.Select(god => god.GodHash).ToHashSet();
        return EmblemCreationCatalog.Emblems.Where(row => !owned.Contains(ItemCatalog.Hash(row.Id))).ToArray();
    }

    public EngageSave WithAddedEmblem(string emblemId)
    {
        var weapons = EmblemCreationCatalog.Weapons(emblemId);
        var gods = OwnedEmblemLayout.Read(this, _bytes);
        var holders = ReadEmblems();
        ValidateEmblemCreation(gods, holders);
        uint hash = ItemCatalog.Hash(emblemId);
        if (gods.Any(god => god.GodHash == hash)) return this;
        uint instance = FreeEmblemInstance(gods.Select(god => god.InstanceId), EmblemCreationCatalog.MaxOwnedInstances);
        var holder = holders.FirstOrDefault(row => row.EmblemId == emblemId);
        var edited = this;
        uint holderId;
        if (holder is not null)
            holderId = holder.InstanceId;
        else
        {
            holderId = FreeEmblemInstance(holders.Select(row => row.InstanceId), EmblemCreationCatalog.MaxBondHolders);
            string[] people = ReadRoster().Where(row => row.Force is UnitForce.Player or UnitForce.Absent or UnitForce.Dead or UnitForce.Lost)
                .Select(row => RosterCatalog.Person(row.PersonHash)?.Id).OfType<string>().Distinct().ToArray();
            if (people.Length > EmblemCreationCatalog.MaxBondsPerHolder)
                throw new InvalidDataException("The new Emblem has too many character bonds.");
            edited = edited.AppendEmblemRecord("GDBD", NewBondHolder(holderId, emblemId, people), holders.Count);
        }
        edited = edited.AppendEmblemRecord("GOD", NewOwnedEmblem(instance, hash, holderId, weapons), gods.Count);
        if (edited.ReadMissingEmblems().Any(row => row.Id == emblemId))
            throw new InvalidDataException("The added Emblem did not survive serialization.");
        return edited;
    }

    private static byte[] NewBondHolder(uint holderId, string emblemId, IReadOnlyList<string> people)
    {
        using var record = new MemoryStream();
        using (var writer = new BinaryWriter(record, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(holderId);
            WriteEmblemString(writer, emblemId);
            writer.Write((byte)0); // Normal rings have no Pact Ring association.
            writer.Write(checked((ushort)people.Count));
            foreach (string person in people)
            {
                WriteEmblemString(writer, person);
                writer.Write(3u);
                writer.Write((byte)1);
                writer.Write((ushort)0);
                writer.Write(2u);
                writer.Write(0u); // Empty inherited-skill set.
                writer.Write((byte)0);
            }
        }
        return record.ToArray();
    }

    private static byte[] NewOwnedEmblem(uint instance, uint hash, uint holderId, IReadOnlyList<string> weapons)
    {
        using var godRecord = new MemoryStream();
        using (var writer = new BinaryWriter(godRecord, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(instance);
            writer.Write((ushort)0xefcd);
            writer.Write(hash);
            writer.Write(holderId);
            writer.Write(new byte[5]); // Darkness, reservation, escaping, cleaning and synchro count.
            writer.Write(checked((byte)weapons.Count));
            foreach (string weapon in weapons)
            {
                WriteEmblemString(writer, weapon);
                writer.Write(new byte[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
                writer.Write(uint.MaxValue); // No refinement skill.
            }
        }
        return godRecord.ToArray();
    }

    private void ValidateEmblemCreation(IReadOnlyList<OwnedEmblem> gods, IReadOnlyList<SavedEmblem> holders)
    {
        if (gods.Count > EmblemCreationCatalog.MaxOwnedInstances || holders.Count > EmblemCreationCatalog.MaxBondHolders
            || gods.Any(row => row.InstanceId is 0 or > EmblemCreationCatalog.MaxOwnedInstances)
            || holders.Any(row => row.InstanceId is 0 or > EmblemCreationCatalog.MaxBondHolders
                || row.Bonds.Count > EmblemCreationCatalog.MaxBondsPerHolder)
            || gods.GroupBy(row => row.GodHash).Any(group => group.Count() != 1)
            || gods.Any(god => !holders.Any(holder => holder.InstanceId == god.BondHolderId
                && ItemCatalog.Hash(holder.EmblemId) == god.GodHash)))
            throw new InvalidDataException("Invalid Emblem ownership, bond references or pool capacity.");
        var links = ReadCharacterRingLinks();
        ValidateEquipmentLinks(links, gods);
        if (links.Any(link => link.PartnerEmblemInstance != 0 && gods.All(god => god.InstanceId != link.PartnerEmblemInstance)))
            throw new InvalidDataException("An Engage+ link references a missing Emblem instance.");
    }

    private static uint FreeEmblemInstance(IEnumerable<uint> instances, int capacity)
    {
        var used = instances.ToHashSet();
        uint free = Enumerable.Range(1, capacity).Select(value => (uint)value).FirstOrDefault(value => !used.Contains(value));
        return free != 0 ? free : throw new ArgumentException("The Emblem pool has no free slot.");
    }

    private EngageSave AppendEmblemRecord(string name, byte[] record, int count)
    {
        var section = EmblemLayout.GetSection(this, name);
        byte[] payload = new byte[section.Length + record.Length];
        _bytes.AsSpan(section.PayloadOffset, section.Length).CopyTo(payload);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), checked((uint)count + 1));
        record.CopyTo(payload, section.Length);
        return ReplaceSection(section, payload);
    }

    private static void WriteEmblemString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        writer.Write(checked((uint)bytes.Length));
        writer.Write(bytes);
    }
}
