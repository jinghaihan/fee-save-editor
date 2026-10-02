using System.Globalization;
using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class MinigamesCommand
{
    public static int Run(string[] args)
    {
        if (args is ["minigame-set", var source, var output, .. var edits]) return Edit(source, output, edits);
        if (args is not ["minigames", var input, .. var options])
            throw new ArgumentException("Use main minigames <save> [--json] [--language en|zh-Hans].");
        string language = "en";
        bool json = false, translated = false;
        for (int index = 0; index < options.Length; index++)
        {
            if (options[index] == "--json" && !json) { json = true; continue; }
            if (options[index] == "--language" && !translated && ++index < options.Length && options[index] is "en" or "zh-Hans")
            {
                translated = true;
                language = options[index];
                continue;
            }
            throw new ArgumentException("Use --json and/or --language en|zh-Hans, without duplicates.");
        }
        var records = EngageSave.Load(input).ReadMinigameRecords().ToDictionary(row => row.Definition.Key);
        Console.WriteLine(JsonSerializer.Serialize(MinigameCatalog.Groups.Select(group => new
        {
            group.Id,
            Name = group.Name(language),
            ReadOnly = false,
            Records = group.Records.Select(definition => new { definition.Key, Name = definition.Name(language),
                records[definition.Key].Value, records[definition.Key].Rank, records[definition.Key].BestSize,
                MaximumValue = int.MaxValue,
                Ranks = definition.RankKey is null ? null : definition.Ranks })
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int Edit(string source, string output, string[] options)
    {
        string[] allowed = ["--record", "--value", "--rank", "--best-size"];
        if (options.Length % 2 != 0) throw new ArgumentException("Provide minigame options and their values.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
            if (!allowed.Contains(options[index]) || !values.TryAdd(options[index], options[index + 1]))
                throw new ArgumentException($"Unknown or duplicate minigame option: {options[index]}");
        string key = values.GetValueOrDefault("--record") ?? throw new ArgumentException("Missing --record.");
        var definition = MinigameCatalog.Groups.SelectMany(group => group.Records).SingleOrDefault(row => row.Key == key)
            ?? throw new ArgumentException("Use a record key from main minigames.");
        var save = EngageSave.Load(source);
        var current = save.ReadMinigameRecords().Single(row => row.Definition.Key == key);
        if (!values.Keys.Any(option => option is "--value" or "--rank" or "--best-size"))
            throw new ArgumentException("Provide --value, --rank and/or --best-size.");
        int value = values.TryGetValue("--value", out string? text) ? Integer(text) : current.Value;
        int? rank = null, size = null;
        if (values.TryGetValue("--rank", out string? rankText))
        {
            if (definition.RankKey is null) throw new ArgumentException("Strength-training scores have no stored rank.");
            var ranks = definition.Ranks;
            int named = Enumerable.Range(0, ranks.Count).FirstOrDefault(index => ranks[index].Equals(rankText, StringComparison.OrdinalIgnoreCase), -1);
            rank = named >= 0 ? named : Integer(rankText);
        }
        if (values.TryGetValue("--best-size", out string? sizeText)) size = Integer(sizeText);
        save.WithMinigameRecords(new Dictionary<string, MinigameValues> { [key] = new(value, rank, size) }).WriteCopy(output);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static int Integer(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
        ? value : throw new ArgumentException("Minigame values must be 32-bit whole numbers.");
}
