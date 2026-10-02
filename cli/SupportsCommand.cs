using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class SupportsCommand
{
    public static int Run(string[] args)
    {
        if (args is ["list", var input, .. var readOptions])
        {
            string language = Language(readOptions);
            var save = EngageSave.Load(input);
            Print(save.ReadSupports().Select(row =>
            {
                var pair = SupportCatalog.Pair(row.Key);
                return new { row.Key, pair?.FirstPersonId, pair?.SecondPersonId,
                    FirstName = Name(pair?.FirstPersonId, language), SecondName = Name(pair?.SecondPersonId, language),
                    Rank = RankName(row.Rank), row.Points, row.Score,
                    MaximumRank = pair is null ? null : RankName(save.MaximumSupportRank(row.Key)),
                    Thresholds = pair?.Thresholds };
            }));
            return 0;
        }
        if (args is ["catalog", .. var catalogOptions])
        {
            string language = Language(catalogOptions);
            Print(SupportCatalog.Pairs.Select(row => new { row.Key, row.FirstPersonId, row.SecondPersonId,
                FirstName = Name(row.FirstPersonId, language), SecondName = Name(row.SecondPersonId, language), row.Thresholds }));
            return 0;
        }
        if (args is not [var verb, var source, var output, .. var options] || verb is not ("set" or "max"))
            throw new ArgumentException("Unknown Support command. Run --help for usage.");
        var saveFile = EngageSave.Load(source);
        if (verb == "max" && options is ["--all"])
        {
            saveFile.WithMaximumSupports().WriteCopy(output);
            Console.WriteLine(Path.GetFullPath(output));
            return 0;
        }
        string[] allowed = verb == "max" ? ["--pair"] : ["--pair", "--rank", "--points"];
        if (options.Length % 2 != 0) throw new ArgumentException("Provide Support options and their values.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
            if (!allowed.Contains(options[index]) || !values.TryAdd(options[index], options[index + 1]))
                throw new ArgumentException($"Unknown or duplicate Support option: {options[index]}");
        string key = values.GetValueOrDefault("--pair") ?? throw new ArgumentException("Missing --pair.");
        EngageSave edited;
        if (verb == "max") edited = saveFile.WithMaximumSupports(key);
        else
        {
            int? points = null;
            if (values.TryGetValue("--points", out string? number))
                points = int.TryParse(number, out int parsed) && parsed is >= 0 and <= 99
                    ? parsed : throw new ArgumentException("Support points must be a whole number from 0–99.");
            SupportRank rank;
            if (values.TryGetValue("--rank", out string? text)) rank = ParseRank(text);
            else if (points.HasValue)
            {
                var pair = SupportCatalog.Pair(key) ?? throw new ArgumentException("Unknown support pair.");
                var current = saveFile.ReadSupports().FirstOrDefault(row => SupportCatalog.Pair(row.Key) == pair);
                rank = current?.Rank == SupportRank.APlus ? SupportRank.APlus
                    : pair.RankForPoints(points.Value);
            }
            else throw new ArgumentException("Provide --rank and/or --points.");
            edited = saveFile.WithSupport(key, rank, points);
        }
        edited.WriteCopy(output);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static string RankName(SupportRank rank) => rank == SupportRank.APlus ? "A+" : rank.ToString();
    private static SupportRank ParseRank(string value) => value.ToUpperInvariant() switch
    {
        "NONE" => SupportRank.None, "C" => SupportRank.C, "B" => SupportRank.B, "A" => SupportRank.A, "A+" => SupportRank.APlus,
        _ => throw new ArgumentException("Use None, C, B, A or A+ for --rank.")
    };
    private static string? Name(string? pid, string language) => pid is null ? null : RosterCatalog.Person(ItemCatalog.Hash(pid))?.Name(language) ?? pid;
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
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
