using System.Buffers.Binary;
using FeeEditor.Core;

internal static class RosterStatTests
{
    public static void Run()
    {
        var save = EngageSave.Parse(RosterTests.Fixture(baseStrength: 100));
        var first = save.ReadRoster()[0];
        Check(first.Stats[1] is { PersonalValue: 100, Value: 42, Maximum: 42 }, "Capped personal values were hidden.");
        Check(ReferenceEquals(save, save.WithRosterPersonalStat(0, RosterStat.Strength, 100)), "A no-op flattened overflow.");
        var reduced = save.WithRosterPersonalStat(0, RosterStat.Strength, 99);
        Check(reduced.ReadRoster()[0].Stats[1] is { PersonalValue: 99, Value: 42 }, "A personal edit subtracted the class base.");
        Check(reduced.WithRosterPersonalStat(0, RosterStat.Strength, 100).Serialize().AsSpan().SequenceEqual(save.Serialize()),
            "Personal value reversal changed opaque bytes.");
        Check(save.WithRosterPersonalStat(0, RosterStat.Strength, -6).ReadRoster()[0].Stats[1].Value == 0,
            "A valid signed personal value did not round-trip.");
        var injured = save.WithRosterPersonalStat(0, RosterStat.HP, -21).ReadRoster()[0];
        Check(injured.Stats[0].Value == 1 && injured.Progress.CurrentHP == 1, "HP reduction left an impossible current HP.");
        foreach (var (stat, value) in new[] { (RosterStat.Strength, -7), (RosterStat.Strength, 128),
            (RosterStat.HP, -22), (RosterStat.Movement, 3), (RosterStat.Sight, 0), ((RosterStat)99, 0) })
            Reject(() => save.WithRosterPersonalStat(0, stat, value));
        Reject(() => save.MaximizeRosterStats(-1));
        Reject(() => save.MaximizeRosterStats(2));
        var max = save.MaximizeRosterStats(0);
        Check(max.ReadRoster()[0].Stats[1].PersonalValue == 100, "Maximizing lowered existing overflow.");
        Check(max.ReadRoster()[1].Stats.SequenceEqual(save.ReadRoster()[1].Stats), "Single maximum modified another character.");
        Check(max.ReadRoster()[0].Progress.CurrentHP == first.Progress.CurrentHP && max.ReadRoster()[0].Values == first.Values,
            "Maximizing changed HP, level or progression.");
        Check(ReferenceEquals(max, max.MaximizeRosterStats(0)), "A repeated maximum rewrote the save.");
        AssertOnlyPersonalBytes(save, max);
        foreach (var person in RosterCatalog.Persons)
        {
            var characterSave = EngageSave.Parse(RosterTests.Fixture(mutate: payload =>
            {
                Write32(payload, 34 + 46, person.Hash);
                Write32(payload, 34 + 52, ItemCatalog.Hash(person.BirthClass));
            }));
            var maximized = characterSave.MaximizeRosterStats(0);
            AssertAllClasses(maximized.ReadRoster()[0]);
            AssertOnlyPersonalBytes(characterSave, maximized);
        }
        foreach (int gender in new[] { 1, 2 })
        {
            var person = RosterCatalog.Person(ItemCatalog.Hash("PID_リュール"))!;
            var values = RosterStats.MaximumPersonalValues(person.Hash, gender);
            foreach (var job in RosterCatalog.ClassesFor(person.Hash, gender))
                for (int stat = 0; stat < 9; stat++)
                    Check(RosterStats.Calculate((RosterStat)stat, values[stat], job, person) is var preview
                        && preview.Value == preview.Maximum, "Alear gender-specific caps are wrong.");
        }
        var enemy = EngageSave.Parse(RosterTests.Fixture(force: UnitForce.Enemy));
        var temporary = EngageSave.Parse(RosterTests.Fixture(force: UnitForce.Temporary));
        Check(ReferenceEquals(enemy, enemy.MaximizeAllRosterStats()) && ReferenceEquals(temporary, temporary.MaximizeAllRosterStats()),
            "Batch maximum modified enemies or temporary units.");
        Reject(() => enemy.MaximizeRosterStats(0));
        var unknown = EngageSave.Parse(RosterTests.Fixture(mutate: payload => Write32(payload, 34 + 46, 0x12345678)));
        var knownOnly = unknown.MaximizeAllRosterStats();
        Check(knownOnly.ReadRoster()[0].Stats.SequenceEqual(unknown.ReadRoster()[0].Stats), "Batch maximum modified an unknown unit.");
        AssertAllClasses(knownOnly.ReadRoster()[1]);
        Reject(() => unknown.MaximizeRosterStats(0));
        CheckReal(EngageSave.Parse(RosterTests.Fixture()));
        Console.WriteLine("Personal stats: signed limits, all 41 owners/classes, gender, DLC, overflow and batch preservation passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var maximized = save.MaximizeAllRosterStats();
        foreach (var character in maximized.ReadRoster().Where(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary
            && RosterCatalog.Person(character.PersonHash) is not null))
            AssertAllClasses(character);
        AssertOnlyPersonalBytes(save, maximized);
        Check(ReferenceEquals(maximized, maximized.MaximizeAllRosterStats()), "Batch maximum was not idempotent.");
        var reclassed = maximized.WithRosterClass(0, "JID_ソードマスター", 2);
        Check(reclassed.ReadRoster()[0].Stats.Take(9).All(stat => stat.Value == stat.Maximum), "Reclassing lost maximum attributes.");
    }

    private static void AssertAllClasses(RosterCharacter character)
    {
        var person = RosterCatalog.Person(character.PersonHash)!;
        foreach (var job in RosterCatalog.ClassesFor(person.Hash, character.Progress.Gender))
            foreach (var stat in character.Stats.Take(9))
            {
                var preview = RosterStats.Calculate(stat.Stat, stat.PersonalValue, job, person);
                Check(preview.Value == preview.Maximum, $"{person.Id}/{job.Id}/{stat.Stat} is not capped.");
            }
    }

    private static void AssertOnlyPersonalBytes(EngageSave before, EngageSave after)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        Check(original.Length == edited.Length, "Maximizing relocated unrelated data.");
        var allowed = Enumerable.Range(original.Length - 4, 4).ToHashSet();
        var unit = before.Sections.Single(section => section.Name == "UNIT");
        int position = unit.PayloadOffset + 32;
        while (original[position] != 255)
        {
            int count = original[position + 1];
            position += 2;
            for (int index = 0; index < count; index++)
            {
                foreach (int offset in Enumerable.Range(position + 60, 9)) allowed.Add(offset);
                position += (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(position, 4));
            }
        }
        for (int index = 0; index < original.Length; index++)
            Check(original[index] == edited[index] || allowed.Contains(index), "Maximizing changed a non-attribute byte.");
    }

    private static void Write32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("An invalid personal stat operation was accepted.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
