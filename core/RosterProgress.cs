namespace FeeEditor.Core;

public sealed record RosterSkill(uint Hash, int Age, int Category);
public sealed record RosterProgress(int CurrentHP, int InternalLevel, uint OriginalProficiencies,
    uint Proficiencies, uint SelectedWeapons, uint? ClassSkill,
    IReadOnlyList<RosterSkill> EquippedSkills, IReadOnlyList<RosterSkill> InheritedSkills);

internal sealed record RosterProgressLayout(RosterProgress Values, int EquippedStart, int EquippedEnd,
    int PoolStart, int PoolEnd, int ClassSkillStart, int ClassSkillEnd, int MasksOffset, int InternalLevelOffset,
    int HPBonus)
{
    public static RosterProgressLayout Read(SaveReader reader, int currentHP)
    {
        if (reader.UInt32() != 0)
            throw new InvalidDataException("Unsupported accessory list version.");
        for (int index = 0; index < 4; index++)
            reader.Reference();
        reader.Skip(4);
        int equippedStart = reader.Position;
        var equipped = ReadSkills(reader);
        int equippedEnd = reader.Position;
        ReadSkills(reader); // Private/personal skills are not inheritance slots.
        int poolStart = reader.Position;
        var pool = ReadSkills(reader);
        int poolEnd = reader.Position;
        int classSkillStart = reader.Position;
        byte present = reader.Byte();
        if (present > 1)
            throw new InvalidDataException("Invalid class skill presence flag.");
        uint? classSkill = present == 1 ? reader.Reference() : null;
        int classSkillEnd = reader.Position;
        int masksOffset = reader.Position;
        uint original = reader.UInt32(), aptitude = reader.UInt32(), weapons = reader.UInt32();
        if (reader.UInt32() != 6)
            throw new InvalidDataException("Unsupported enhancement factors version.");
        for (int index = 0; index < 3; index++)
            ReadEnhancement(reader);
        if (reader.UInt32() != 1)
            throw new InvalidDataException("Unsupported enhancement calculator version.");
        int hpBonus = ReadEnhancement(reader);
        int internalOffset = reader.Position;
        int internalLevel = (sbyte)reader.Byte();
        var values = new RosterProgress(currentHP, internalLevel, original, aptitude, weapons, classSkill, equipped, pool);
        return new(values, equippedStart, equippedEnd, poolStart, poolEnd, classSkillStart, classSkillEnd,
            masksOffset, internalOffset, hpBonus);
    }

    private static IReadOnlyList<RosterSkill> ReadSkills(SaveReader reader)
    {
        if (reader.UInt32() != 1)
            throw new InvalidDataException("Unsupported skill array version.");
        uint count = reader.UInt32();
        if (count > 1280 || count > reader.Remaining / 10)
            throw new InvalidDataException("Invalid skill array length.");
        var skills = new List<RosterSkill>();
        for (uint index = 0; index < count; index++)
        {
            uint hash = reader.Reference() ?? throw new InvalidDataException("A skill entry has no reference.");
            skills.Add(new(hash, unchecked((int)reader.UInt32()), unchecked((int)reader.UInt32())));
        }
        return skills.AsReadOnly();
    }

    private static int ReadEnhancement(SaveReader reader)
    {
        if (reader.UInt32() != 1 || reader.UInt32() != 11)
            throw new InvalidDataException("Unsupported enhancement capability version.");
        int hp = unchecked((int)reader.UInt32());
        reader.Skip(40);
        return hp;
    }
}
