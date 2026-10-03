using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class ActivitiesCommand
{
    public static int Run(string[] args)
    {
        if (args is ["activities", var source, .. var flags] && flags is [] or ["--json"])
        {
            var values = EngageSave.Load(source).ReadSomnielActivities();
            Console.WriteLine(JsonSerializer.Serialize(new { values.TrainingRemaining, values.ArenaRemaining,
                MaximumTraining = SomnielActivities.MaxTraining, MaximumArena = SomnielActivities.MaxArena },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args is not [var verb, var input, var output, .. var options] || verb is not ("activities-set" or "activities-restore"))
            throw new ArgumentException("Unknown activities command. Run --help for usage.");
        var save = EngageSave.Load(input);
        EngageSave edited;
        if (verb == "activities-restore")
        {
            if (options.Length != 0) throw new ArgumentException("activities-restore takes no additional options.");
            edited = save.WithRestoredSomnielActivities();
        }
        else
        {
            if (options.Length == 0 || options.Length % 2 != 0)
                throw new ArgumentException("Provide --training-remaining and/or --arena-remaining with their values.");
            var values = new Dictionary<string, int>();
            for (int index = 0; index < options.Length; index += 2)
                if (options[index] is not ("--training-remaining" or "--arena-remaining")
                    || !int.TryParse(options[index + 1], out int value) || !values.TryAdd(options[index], value))
                    throw new ArgumentException("Unknown, duplicate or invalid activity option.");
            var current = save.ReadSomnielActivities();
            edited = save.WithSomnielActivities(new(values.GetValueOrDefault("--training-remaining", current.TrainingRemaining),
                values.GetValueOrDefault("--arena-remaining", current.ArenaRemaining)));
        }
        edited.WriteCopy(output);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }
}
