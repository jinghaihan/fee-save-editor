using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class MinigamesCommand
{
    public static int Run(string[] args)
    {
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
            ReadOnly = true,
            Records = group.Records.Select(definition => new { definition.Key, Name = definition.Name(language),
                records[definition.Key].Value })
        }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
