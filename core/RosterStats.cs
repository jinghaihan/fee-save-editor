namespace FeeEditor.Core;

public static class RosterStats
{
    public static CharacterStat Calculate(RosterStat stat, int personalValue, ClassDefinition? job, PersonDefinition? person)
    {
        if (!Enum.IsDefined(stat))
            throw new ArgumentOutOfRangeException(nameof(stat));
        int index = (int)stat;
        int? maximum = job is not null && person is not null
            ? Math.Clamp(job.Limits[index] + person.LimitModifiers[index], 0, 255) : null;
        if (stat == RosterStat.Movement && job is not null)
            maximum = job.BaseStats[index] + 2;
        int value = personalValue;
        if (job is not null && maximum.HasValue)
            value = Math.Clamp(personalValue + job.BaseStats[index], stat == RosterStat.HP ? 1 : 0, maximum.Value);
        return new CharacterStat(stat, value, maximum, personalValue);
    }

    public static (int Minimum, int Maximum) PersonalRange(RosterStat stat, ClassDefinition job)
    {
        if (!Enum.IsDefined(stat) || stat == RosterStat.Sight)
            throw new ArgumentException("Select an editable personal stat.", nameof(stat));
        int minimum = (stat == RosterStat.HP ? 1 : 0) - job.BaseStats[(int)stat];
        int maximum = stat == RosterStat.Movement ? 2 : sbyte.MaxValue;
        return (Math.Max(sbyte.MinValue, minimum), maximum);
    }

    public static IReadOnlyList<int> MaximumPersonalValues(uint personHash, int gender)
    {
        var person = RosterCatalog.Person(personHash) ?? throw new ArgumentException("Select a known playable character.");
        var classes = RosterCatalog.ClassesFor(personHash, gender);
        if (classes.Count == 0)
            throw new ArgumentException("This character has no verified playable classes.");
        var values = Enumerable.Range(0, 9).Select(stat => classes.Max(job =>
            Math.Clamp(job.Limits[stat] + person.LimitModifiers[stat], 0, 255) - job.BaseStats[stat])).ToArray();
        if (values.Any(value => value is < sbyte.MinValue or > sbyte.MaxValue))
            throw new InvalidDataException("A class cap cannot be represented by the personal stat storage.");
        return Array.AsReadOnly(values);
    }
}
