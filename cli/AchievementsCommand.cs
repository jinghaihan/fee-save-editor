using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class AchievementsCommand
{
    public static int Run(string[] args)
    {
        if (args is ["catalog", .. var catalogOptions])
        {
            string language = Language(catalogOptions);
            Print(AchievementCatalog.Achievements.Select(row => new { row.Id, Name = row.Name(language),
                Category = row.Category.ToString(), row.Reward, row.Chapter }));
            return 0;
        }
        if (args is ["list", var input, .. var readOptions])
        {
            string language = Language(readOptions);
            Print(EngageSave.Load(input).ReadAchievements().Select(row => new { row.Definition.Id,
                Name = row.Definition.Name(language), Category = row.Definition.Category.ToString(),
                Status = row.Status.ToString(), row.Achieved, row.RewardAvailable, row.RewardClaimed,
                row.Definition.Reward, row.Definition.Chapter }));
            return 0;
        }
        if (args is not ["unlock", var source, var output, .. var options])
            throw new ArgumentException("Unknown achievement command. Run --help for usage.");
        string? id;
        if (options is ["--all"]) id = null;
        else if (options is ["--achievement", var selected]) id = selected;
        else throw new ArgumentException("Use --all or --achievement <AID>.");
        EngageSave.Load(source).WithUnlockedAchievements(id).WriteCopy(output);
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
                if (language is "en" or "zh-Hans") continue;
            }
            throw new ArgumentException("Use --json and/or --language en|zh-Hans, without duplicates.");
        }
        return language ?? "en";
    }
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
