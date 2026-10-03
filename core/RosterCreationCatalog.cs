using System.Text.Json;

namespace FeeEditor.Core;

internal sealed record RosterCreationDefaults(string Id, int InternalLevel, int SkillPoints,
    uint OriginalProficiencies, uint Proficiencies, int[][] PersonalStats);

internal static class RosterCreationCatalog
{
    private sealed record CatalogData(RosterCreationDefaults[] Persons);
    private static readonly IReadOnlyDictionary<string, RosterCreationDefaults> Defaults = Load();
    public static RosterCreationDefaults Person(string id) => Defaults.GetValueOrDefault(id)
        ?? throw new ArgumentException("Select a playable character with verified creation defaults.");

    private static IReadOnlyDictionary<string, RosterCreationDefaults> Load()
    {
        using var stream = typeof(RosterCreationCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.roster-creation.json")
            ?? throw new InvalidOperationException("The roster creation catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid roster creation catalog.");
        if (data.Persons.Length != 41 || data.Persons.Any(row => RosterCatalog.Person(ItemCatalog.Hash(row.Id)) is null
            || row.InternalLevel is < -100 or > 100 || row.SkillPoints is < 0 or > 9999
            || row.PersonalStats.Length != 3 || row.PersonalStats.Any(stats => stats.Length != 11 || stats.Any(value => value is < -128 or > 127))))
            throw new InvalidDataException("The roster creation catalog has invalid character defaults.");
        return data.Persons.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }
}
