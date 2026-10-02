using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class EmblemsCommand
{
    public static int Run(string[] args)
    {
        if (args is ["catalog", .. var catalogOptions])
        {
            string language = Language(catalogOptions);
            Print(new
            {
                Emblems = EmblemCatalog.Emblems.Select(row => new { row.Id, Name = row.Name(language), row.Dlc,
                    CanAdd = EmblemCreationCatalog.Emblems.Any(candidate => candidate.Id == row.Id) }),
                Rings = EmblemCatalog.Rings.Select(row => new { row.Id, Name = row.Name(language), Rank = row.RankName, row.MaxStock,
                    StatBonuses = row.StatBonuses.Select((value, index) => new { Stat = ((RosterStat)index).ToString(), Value = value })
                        .Where(bonus => bonus.Value != 0),
                    Skills = row.Skills.Select(skill => new { skill.Id, Name = skill.Name(language), Description = skill.Description(language) }),
                    Melding = BondRingCatalog.Melding(row.Hash) is { } meld
                        ? new { ResultId = meld.Result.Id, ResultRank = meld.Result.RankName, meld.RequiredRings, meld.BondFragments } : null })
            });
            return 0;
        }
        if (args is [var read, var source, .. var readOptions] && read is "list" or "rings" or "missing")
        {
            string language = Language(readOptions);
            var save = EngageSave.Load(source);
            if (read == "list")
                Print(save.ReadEmblems().Select(row => new
                {
                    row.InstanceId, Id = row.EmblemId, Name = EmblemCatalog.Emblem(row.EmblemId)?.Name(language) ?? row.EmblemId,
                    row.PactPartner, Bonds = row.Bonds.Select(bond => new
                    {
                        bond.PersonId, Name = RosterCatalog.Person(ItemCatalog.Hash(bond.PersonId))?.Name(language) ?? bond.PersonId,
                        bond.Level, bond.Experience, bond.InheritedSkills, bond.TalkFlags,
                        MaximumLevel = EmblemCatalog.Emblem(row.EmblemId) is null ? (int?)null : EmblemCatalog.MaximumLevel(row, bond.PersonId)
                    })
                }));
            else if (read == "missing")
                Print(save.ReadMissingEmblems().Select(row => new { row.Id, Name = row.Name(language), row.Dlc }));
            else
                Print(save.ReadBondRings().Select(row => new
                {
                    row.InstanceId, row.RingHash, Id = EmblemCatalog.Ring(row.RingHash)?.Id,
                    Name = EmblemCatalog.Ring(row.RingHash)?.Name(language) ?? $"Unknown ring (0x{row.RingHash:X8})",
                    Rank = EmblemCatalog.Ring(row.RingHash)?.RankName, row.StockCount, row.OwnerIndex,
                    MaximumStock = EmblemCatalog.Ring(row.RingHash) is null ? (int?)null : save.MaximumBondRingStock(row.InstanceId)
                }));
            return 0;
        }
        if (args is not [var verb, var input, var output, .. var options]
            || verb is not ("add" or "bond-set" or "bond-max" or "bonds-max" or "ring-set" or "rings-fill-s" or "ring-meld"))
            throw new ArgumentException("Unknown Emblems command. Run --help for usage.");
        var values = Options(options, verb);
        var current = EngageSave.Load(input);
        uint instance = verb is "add" or "rings-fill-s" ? 0 : checked((uint)Number(Required(values, "--instance")));
        EngageSave edited;
        if (verb == "add")
            edited = current.WithAddedEmblem(Required(values, "--emblem"));
        else if (verb == "rings-fill-s")
            edited = current.WithMissingSBondRings();
        else if (verb == "ring-meld")
            edited = current.WithMeldedBondRing(instance);
        else if (verb == "ring-set")
            edited = current.WithBondRingStock(instance, Number(Required(values, "--amount")));
        else if (verb is "bond-max" or "bonds-max")
            edited = current.WithMaximumEmblemBonds(instance, verb == "bond-max" ? Required(values, "--person") : null);
        else
        {
            string person = Required(values, "--person");
            int level;
            int? exp = values.TryGetValue("--experience", out string? text) ? Number(text) : null;
            if (values.TryGetValue("--level", out string? levelText)) level = Number(levelText);
            else if (exp.HasValue) level = EmblemCatalog.LevelForExperience(exp.Value);
            else throw new ArgumentException("Provide --level and/or --experience.");
            edited = current.WithEmblemBond(instance, person, level, exp);
        }
        edited.WriteCopy(output);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static string Language(string[] options)
    {
        bool json = false;
        string? language = null;
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] == "--json" && !json) { json = true; continue; }
            if (options[index] == "--language" && language is null && ++index < options.Length)
            {
                language = options[index];
                if (LanguageCatalog.Codes.Contains(language)) continue;
            }
            throw new ArgumentException("Use --json and/or --language en|zh-Hans|zh-Hant|ja|ko|de|fr|es|it, without duplicates.");
        }
        return language ?? "en";
    }

    private static Dictionary<string, string> Options(string[] options, string verb)
    {
        string[] allowed = verb switch
        {
            "add" => ["--emblem"],
            "bond-set" => ["--instance", "--person", "--level", "--experience"],
            "bond-max" => ["--instance", "--person"],
            "bonds-max" => ["--instance"],
            "ring-meld" => ["--instance"],
            "rings-fill-s" => [],
            _ => ["--instance", "--amount"]
        };
        if (options.Length % 2 != 0) throw new ArgumentException("Provide Emblem options and their values.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
            if (!allowed.Contains(options[index]) || !result.TryAdd(options[index], options[index + 1]))
                throw new ArgumentException($"Unknown or duplicate Emblem option: {options[index]}");
        return result;
    }
    private static int Number(string value) => int.TryParse(value, out int number) && number >= 0
        ? number : throw new ArgumentException("Emblem values must be nonnegative whole numbers within their game limits.");
    private static string Required(Dictionary<string, string> options, string key) => options.GetValueOrDefault(key)
        ?? throw new ArgumentException($"Missing Emblem option: {key}");
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));
}
