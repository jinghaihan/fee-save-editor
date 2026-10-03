using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<PersonDefinition> ReadMissingRosterCharacters()
    {
        var layout = RosterLayout.Read(this, _bytes);
        ValidateOutOfBattleRoster(layout);
        if (layout.Characters.Count >= 250) return [];
        var present = layout.Characters.Select(entry => entry.Character.PersonHash).ToHashSet();
        return RosterCatalog.Persons.Where(person => person.Id != "PID_リュール" && !present.Contains(person.Hash)).ToArray();
    }

    public EngageSave WithAddedRosterCharacter(string personId)
    {
        var person = ReadMissingRosterCharacters().FirstOrDefault(row => row.Id == personId)
            ?? throw new ArgumentException("Choose a missing playable character; the protagonist and duplicates cannot be added.");
        ValidateEmblemCreation(OwnedEmblemLayout.Read(this, _bytes), ReadEmblems());
        var job = RosterCatalog.ClassesFor(person.Hash, person.Gender).Single(row => row.Id == person.BirthClass);
        var defaults = RosterCreationCatalog.Person(personId);
        int[] stats = defaults.PersonalStats[(int)ReadMainValues().Difficulty];
        byte[] record = NewRosterCharacter(person, job, defaults, stats);
        var edited = RewriteRosterPool(RosterLayout.Read(this, _bytes), null, UnitForce.Absent, record);
        return edited.WithNewCharacterBonds(personId);
    }

    private static byte[] NewRosterCharacter(PersonDefinition person, ClassDefinition job, RosterCreationDefaults defaults, int[] stats)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(0u); writer.Write(40u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]);
        writer.Write(0UL);
        WriteReference(writer, person.Hash); WriteReference(writer, job.Hash);
        writer.Write(0u);
        foreach (int value in stats) writer.Write(checked((sbyte)value));
        for (int growth = 0; growth < 2; growth++) { writer.Write(0u); writer.Write(new byte[11]); }
        writer.Write(RandomNumberGenerator.GetBytes(8));
        writer.Write((byte)1); writer.Write((byte)0);
        writer.Write(checked((byte)RosterStats.Calculate(RosterStat.HP, stats[0], job, person).Value));
        writer.Write(uint.MaxValue); writer.Write(0f); writer.Write(false); writer.Write(0u);
        writer.Write(2u); writer.Write((byte)8);
        for (int item = 0; item < 8; item++) { writer.Write(5u); writer.Write(false); }
        writer.Write(0u);
        for (int accessory = 0; accessory < 4; accessory++) WriteReference(writer, null);
        writer.Write(0u);
        for (int skills = 0; skills < 3; skills++) { writer.Write(1u); writer.Write(0u); }
        writer.Write(false);
        uint weapons = job.WeaponVariants()[0];
        writer.Write(defaults.OriginalProficiencies); writer.Write(defaults.Proficiencies | weapons); writer.Write(weapons);
        writer.Write(6u);
        for (int enhancement = 0; enhancement < 4; enhancement++)
        {
            if (enhancement == 3) writer.Write(1u);
            writer.Write(1u); writer.Write(11u); writer.Write(new byte[44]);
        }
        writer.Write(checked((sbyte)defaults.InternalLevel));
        writer.Write((byte)2); writer.Write(false); // No custom name or copied player identity.
        writer.Write((ushort)0); // No active Engage duration.
        writer.Write((byte)4); writer.Write((byte)0); writer.Write((ushort)0); // Empty battle records.
        writer.Write((byte)10); writer.Write(new byte[10]); writer.Write((ushort)0);
        writer.Write(new byte[16]); // No Emblem, Engage+ partner, bond ring or extra sight.
        writer.Write(false); // No fortune target.
        writer.Write(RandomNumberGenerator.GetBytes(4));
        writer.Write(byte.MaxValue); writer.Write((byte)0); // No relay player; map history starts empty.
        writer.Write(checked((short)defaults.SkillPoints)); writer.Write(0u); writer.Write((sbyte)0); writer.Write((sbyte)0);
        byte[] record = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(record, checked((uint)record.Length));
        return record;
    }

    private static void WriteReference(BinaryWriter writer, uint? hash)
    {
        writer.Write((ushort)(hash.HasValue ? 0xefcd : 0xccdb));
        if (hash.HasValue) writer.Write(hash.Value);
    }
}
