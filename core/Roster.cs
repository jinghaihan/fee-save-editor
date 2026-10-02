namespace FeeEditor.Core;

public enum RosterStat { HP, Strength, Dexterity, Speed, Luck, Defense, Magic, Resistance, Build, Sight, Movement }
public enum UnitForce { Player, Enemy, Ally, Absent, Dead, Lost, Temporary }
public sealed record RosterValue(int Level, int Experience, int SkillPoints);
public sealed record CharacterStat(RosterStat Stat, int Value, int? Maximum);
public sealed record RosterCharacter(int Index, UnitForce Force, uint PersonHash, uint ClassHash, RosterValue Values,
    IReadOnlyList<CharacterStat> Stats, IReadOnlyList<InventorySlot> Items, RosterProgress Progress);

internal sealed record CharacterLayout(RosterCharacter Character, int Start, int End, int BaseStatsOffset,
    int LevelOffset, int[] ItemStarts, int[] ItemEnds, RosterProgressLayout Progress);

internal sealed class RosterLayout
{
    public required SaveSection Section { get; init; }
    public required IReadOnlyList<CharacterLayout> Characters { get; init; }

    public static RosterLayout Read(EngageSave save, byte[] bytes)
    {
        if (save.Kind != SaveKind.Game || save.FormatVersion != 9)
            throw new InvalidDataException("Roster editing requires an Engage format-version 9 game save.");
        var sections = save.Sections.Where(section => section.Name == "UNIT").ToArray();
        if (sections.Length != 1)
            throw new InvalidDataException("The save must contain one UNIT section.");
        var section = sections[0];
        var reader = new SaveReader(bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.UInt32() != 0 || reader.UInt32() != 0xcdcdcdcd)
            throw new InvalidDataException("Unsupported UNIT section version.");
        reader.Skip(24);
        var result = new List<CharacterLayout>();
        int lastForce = -1;
        while (true)
        {
            int force = reader.Byte();
            if (force == 255)
                break;
            if (force <= lastForce || force > 6)
                throw new InvalidDataException("Invalid or duplicate unit force.");
            lastForce = force;
            int count = reader.Byte();
            if (count == 0 || result.Count + count > 250)
                throw new InvalidDataException("Invalid unit pool count.");
            for (int index = 0; index < count; index++)
            {
                int start = reader.Position;
                uint length = reader.UInt32();
                if (length < 160 || length > reader.Remaining + 4)
                    throw new InvalidDataException("Invalid character record length.");
                int end = checked(start + (int)length);
                var unit = new SaveReader(bytes, start + 4, end);
                if (unit.UInt32() != 40 || unit.UInt32() != 0xcdcdcdcd)
                    throw new InvalidDataException("Unsupported character record version.");
                unit.Skip(24 + 8);
                uint person = unit.Reference() ?? throw new InvalidDataException("A character has no person reference.");
                uint job = unit.Reference() ?? throw new InvalidDataException("A character has no class reference.");
                if (unit.UInt32() != 0)
                    throw new InvalidDataException("Unsupported base capability version.");
                int baseOffset = unit.Position;
                int[] baseStats = Enumerable.Range(0, 11).Select(_ => (int)(sbyte)unit.Byte()).ToArray();
                for (int capability = 0; capability < 2; capability++)
                {
                    if (unit.UInt32() != 0)
                        throw new InvalidDataException("Unsupported growth capability version.");
                    unit.Skip(11);
                }
                unit.Skip(8);
                int levelOffset = unit.Position;
                int level = unit.Byte();
                int experience = unit.Byte();
                int currentHP = unit.Byte();
                unit.Skip(4 + 4);
                byte hasTarget = unit.Byte();
                if (hasTarget > 1)
                    throw new InvalidDataException("Invalid optional target presence flag.");
                if (hasTarget == 1)
                    unit.Reference();
                unit.Skip(4);
                if (unit.UInt32() != 2 || unit.Byte() != 8)
                    throw new InvalidDataException("Unsupported character item list.");
                var items = new InventorySlot[8];
                var starts = new int[8];
                var ends = new int[8];
                for (int slot = 0; slot < 8; slot++)
                {
                    starts[slot] = unit.Position;
                    if (unit.UInt32() != 5)
                        throw new InvalidDataException("Unsupported character item version.");
                    byte present = unit.Byte();
                    if (present > 1)
                        throw new InvalidDataException("Invalid character item presence flag.");
                    InventoryItem? item = null;
                    if (present == 1)
                    {
                        uint hash = unit.Reference() ?? throw new InvalidDataException("An occupied item has no item reference.");
                        item = new InventoryItem(hash, unit.Byte(), unit.Byte(), unit.UInt32(), unit.Reference());
                    }
                    items[slot] = new InventorySlot(slot, item);
                    ends[slot] = unit.Position;
                }
                var progress = RosterProgressLayout.Read(unit, currentHP);
                if (unit.Remaining < 10)
                    throw new InvalidDataException("The character trailer is truncated.");
                var tail = new SaveReader(bytes, end - 8, end);
                int sp = (short)tail.UInt16();
                var character = new RosterCharacter(result.Count, (UnitForce)force, person, job,
                    new RosterValue(level, experience, sp), CharacterStats(person, job, baseStats), Array.AsReadOnly(items), progress.Values);
                result.Add(new CharacterLayout(character, start, end, baseOffset, levelOffset, starts, ends, progress));
                reader.Skip(end - reader.Position);
            }
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException("The unit pool contains unrecognized trailing data.");
        return new RosterLayout { Section = section, Characters = result.AsReadOnly() };
    }

    private static IReadOnlyList<CharacterStat> CharacterStats(uint person, uint job, int[] stored)
    {
        var definition = RosterCatalog.Class(job);
        var owner = RosterCatalog.Person(person);
        var stats = Enumerable.Range(0, 11).Select(stat =>
        {
            int? maximum = definition is not null && owner is not null
                ? Math.Clamp(definition.Limits[stat] + owner.LimitModifiers[stat], 0, 255) : null;
            if (stat == (int)RosterStat.Movement && definition is not null)
                maximum = definition.BaseStats[stat] + 2;
            int value = stored[stat];
            if (definition is not null && maximum.HasValue)
                value = Math.Clamp(value + definition.BaseStats[stat], stat == 0 ? 1 : 0, maximum.Value);
            return new CharacterStat((RosterStat)stat, value, maximum);
        }).ToArray();
        return Array.AsReadOnly(stats);
    }
}
