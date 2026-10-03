using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeeEditor.Core;
using FeeEditor.Cli;

if (args is ["--version"])
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
    return 0;
}

if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("""
        Fire Emblem Engage Save Editor
        Usage:
          FeeEditor.Cli inspect <save> [--json]
          FeeEditor.Cli copy <save> <new-file>
          FeeEditor.Cli variables list <save> [--json]
          FeeEditor.Cli variables set <save> <new-file> --key <key> --value <signed-int32>
          FeeEditor.Cli main show <save> [--json]
          FeeEditor.Cli main set <save> <new-file> [options]
          FeeEditor.Cli main name <save>
          FeeEditor.Cli main name-set <save> <new-file> --name <name>
          FeeEditor.Cli main activities <save> [--json]
          FeeEditor.Cli main activities-set <save> <new-file> [--training-remaining <0-1>] [--arena-remaining <0-3>]
          FeeEditor.Cli main activities-restore <save> <new-file>
          FeeEditor.Cli main donation-catalog [--json] [--language <code>]
          FeeEditor.Cli main donations <save> [--json] [--language <code>]
          FeeEditor.Cli main minigames <save> [--json] [--language <code>]
          FeeEditor.Cli main minigame-set <save> <output> --record <key> [--value <n>] [--rank <rank>] [--best-size <cm>]
          FeeEditor.Cli main donation-set <save> <new-file> --country <Firene|Brodia|Elusia|Solm> [--level <1-5>] [--amount <0-9999999>]
          FeeEditor.Cli main donations-max <save> <new-file> --all|--country <name>
          FeeEditor.Cli items catalog [--json]
          FeeEditor.Cli items list <save> [--json]
          FeeEditor.Cli items quantity-catalog [--language <code>] [--json]
          FeeEditor.Cli items quantities <save> [--language <code>] [--json]
          FeeEditor.Cli items quantity-set <save> <new-file> --item <IID> --amount <value>
          FeeEditor.Cli items quantity-fill <save> <new-file> --category <ReclassItems|Materials|Ingredients|Gifts>
          FeeEditor.Cli items engravings [--json]
          FeeEditor.Cli items set <save> <new-file> --slot <index> [--item <IID>] [--uses <value>] [--refine <level>] [--engraving <GID|none>]
          FeeEditor.Cli items add <save> <new-file> --item <IID> [--uses <value>] [--refine <level>] [--engraving <GID|none>]
          FeeEditor.Cli items engrave <save> <new-file> --slot <index> --engraving <GID|none>
          FeeEditor.Cli items delete <save> <new-file> --slot <index>
          FeeEditor.Cli items restore <save> <new-file> --all|--slot <index>
          FeeEditor.Cli roster list <save> [--json] [--language <code>]
          FeeEditor.Cli roster catalog [--json] [--language <code>]
          FeeEditor.Cli roster missing <save> [--json] [--language <code>]
          FeeEditor.Cli roster add <save> <new-file> --person <PID>
          FeeEditor.Cli roster delete <save> <new-file> --character <index>
          FeeEditor.Cli roster move <save> <new-file> --character <index> --force Absent|Dead|Lost
          FeeEditor.Cli roster class-repair <save> <new-file> --character <index>
          FeeEditor.Cli roster classes-repair <save> <new-file> --all
          FeeEditor.Cli roster equipment <save> --character <index> [--json] [--language <code>]
          FeeEditor.Cli roster equipment-set <save> <new-file> --character <index> --emblem <instance>|--ring <instance>|--none true
          FeeEditor.Cli roster export <save> <character.json> --character <index>
          FeeEditor.Cli roster import <save> <new-file> --character <index> --file <character.json>
          FeeEditor.Cli roster class <save> <new-file> --character <index> --class <JID> [--weapons Sword,Lance]
          FeeEditor.Cli roster set <save> <new-file> --character <index> [--level <value>] [--experience <value>] [--sp <value>]
          FeeEditor.Cli roster stat <save> <new-file> --character <index> --stat <name> --value <value>
          FeeEditor.Cli roster personal-stat <save> <new-file> --character <index> --stat <name> --value <value>
          FeeEditor.Cli roster stats-max <save> <new-file> --all|--character <index>
          FeeEditor.Cli roster item-set <save> <new-file> --character <index> --slot <index> [--item <IID>] [--uses <value>] [--refine <level>] [--engraving <GID|none>]
          FeeEditor.Cli roster item-engrave <save> <new-file> --character <index> --slot <index> --engraving <GID|none>
          FeeEditor.Cli roster item-delete <save> <new-file> --character <index> --slot <index>
          FeeEditor.Cli roster restore <save> <new-file> --character <index>
          FeeEditor.Cli roster restore-character <save> <new-file> --character <index>
          FeeEditor.Cli roster condition <save> <new-file> --character <index> [--internal-level <value>] [--hp <value>]
          FeeEditor.Cli roster skill-unlock|skill-remove <save> <new-file> --character <index> --skill <SID>
          FeeEditor.Cli roster skills-equip <save> <new-file> --character <index> [--first <SID|none>] [--second <SID|none>]
          FeeEditor.Cli roster skills-max <save> <new-file> --character <index>
          FeeEditor.Cli roster class-skill <save> <new-file> --character <index> --unlocked true|false
          FeeEditor.Cli roster proficiencies <save> <new-file> --character <index> --weapons Sword,Lance
          FeeEditor.Cli emblems catalog [--json] [--language <code>]
          FeeEditor.Cli emblems list <save> [--json] [--language <code>]
          FeeEditor.Cli emblems missing <save> [--json] [--language <code>]
          FeeEditor.Cli emblems conditions <save> [--json] [--language <code>]
          FeeEditor.Cli emblems dirt-set <save> <new-file> --instance <owned-id> --value <0-255>
          FeeEditor.Cli emblems clean <save> <new-file> --instance <owned-id>
          FeeEditor.Cli emblems clean-all <save> <new-file>
          FeeEditor.Cli emblems add <save> <new-file> --emblem <GID>
          FeeEditor.Cli emblems remove <save> <new-file> --instance <owned-id>
          FeeEditor.Cli emblems rings <save> [--json] [--language <code>]
          FeeEditor.Cli emblems bond-set <save> <new-file> --instance <id> --person <PID> [--level <1-20>] [--experience <0-208>]
          FeeEditor.Cli emblems bond-max <save> <new-file> --instance <id> --person <PID>
          FeeEditor.Cli emblems bonds-max <save> <new-file> --instance <id>
          FeeEditor.Cli emblems ring-set <save> <new-file> --instance <id> --amount <0-99>
          FeeEditor.Cli emblems rings-fill-s <save> <new-file>
          FeeEditor.Cli emblems ring-meld <save> <new-file> --instance <id>
          FeeEditor.Cli supports catalog [--json] [--language <code>]
          FeeEditor.Cli supports list <save> [--json] [--language <code>]
          FeeEditor.Cli supports set <save> <new-file> --pair <key> [--rank None|C|B|A|A+] [--points <0-99>]
          FeeEditor.Cli supports max <save> <new-file> --all|--pair <key>
          FeeEditor.Cli achievements catalog [--json] [--language <code>]
          FeeEditor.Cli achievements list <save> [--json] [--language <code>]
          FeeEditor.Cli achievements unlock <save> <new-file> --all|--achievement <AID>
          FeeEditor.Cli --version
        Languages: en, zh-Hans, zh-Hant, ja, ko, de, fr, es, it (default: en)
        Main options:
          --money <amount> --bond-fragments <amount>
          --iron <amount> --steel <amount> --silver <amount>
          --difficulty normal|hard|maddening --mode casual|classic
          --sommie-name <name>
        """);
    return 0;
}

try
{
    switch (args)
    {
        case ["main", var activityVerb, ..] when activityVerb is "activities" or "activities-set" or "activities-restore":
            return ActivitiesCommand.Run(args[1..]);
        case ["main", var minigameVerb, ..] when minigameVerb is "minigames" or "minigame-set":
            return MinigamesCommand.Run(args[1..]);
        case ["main", var verb, ..] when verb is "donations" or "donation-catalog" or "donation-set" or "donations-max":
            return DonationsCommand.Run(args[1..]);
        case ["variables", .. var variableOptions]:
            return VariablesCommand.Run(variableOptions);
        case ["items", .. var options]:
            return ItemsCommand.Run(options);
        case ["roster", .. var options]:
            return RosterCommand.Run(options);
        case ["emblems", .. var options]:
            return EmblemsCommand.Run(options);
        case ["supports", .. var options]:
            return SupportsCommand.Run(options);
        case ["achievements", .. var options]:
            return AchievementsCommand.Run(options);
        case ["inspect", var source, .. var options] when options is [] or ["--json"]:
            var save = EngageSave.Load(source);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                save.Kind, save.FormatVersion, save.GameVersion, save.Length,
                Checksum = $"{save.Checksum:X8}", save.Sections
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return 0;
        case ["copy", var source, var destination]:
            EngageSave.Load(source).WriteCopy(destination);
            Console.WriteLine(Path.GetFullPath(destination));
            return 0;
        case ["main", "show", var source, .. var options] when options is [] or ["--json"]:
            Console.WriteLine(JsonSerializer.Serialize(EngageSave.Load(source).ReadMainValues(),
                new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
            return 0;
        case ["main", "name", var source]:
            Console.WriteLine(JsonSerializer.Serialize(new { PlayerName = EngageSave.Load(source).ReadProtagonistName() }));
            return 0;
        case ["main", "name-set", var source, var destination, "--name", var name]:
            EngageSave.Load(source).WithProtagonistName(name).WriteCopy(destination);
            Console.WriteLine(Path.GetFullPath(destination));
            return 0;
        case ["main", "set", var source, var destination, .. var options]:
            var current = EngageSave.Load(source);
            current.WithMainValues(PatchMain(current.ReadMainValues(), options)).WriteCopy(destination);
            Console.WriteLine(Path.GetFullPath(destination));
            return 0;
        default:
            Console.Error.WriteLine("Unknown command. Run --help for usage.");
            return 1;
    }
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static MainValues PatchMain(MainValues values, string[] options)
{
    if (options.Length == 0 || options.Length % 2 != 0)
        throw new ArgumentException("Provide at least one Main option and its value.");
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (int index = 0; index < options.Length; index += 2)
    {
        string key = options[index];
        string value = options[index + 1];
        if (!seen.Add(key))
            throw new ArgumentException($"Duplicate option: {key}");
        values = key switch
        {
            "--money" => values with { Money = Amount(value) },
            "--bond-fragments" => values with { BondFragments = Amount(value) },
            "--iron" => values with { IronIngots = Amount(value) },
            "--steel" => values with { SteelIngots = Amount(value) },
            "--silver" => values with { SilverIngots = Amount(value) },
            "--difficulty" => values with { Difficulty = value.ToLowerInvariant() switch
                {
                    "normal" => Difficulty.Normal, "hard" => Difficulty.Hard,
                    "maddening" => Difficulty.Maddening,
                    _ => throw new ArgumentException("Difficulty must be normal, hard or maddening.")
                } },
            "--mode" => values with { GameMode = value.ToLowerInvariant() switch
                {
                    "casual" => GameMode.Casual, "classic" => GameMode.Classic,
                    _ => throw new ArgumentException("Mode must be casual or classic.")
                } },
            "--sommie-name" => values with { SommieName = value },
            _ => throw new ArgumentException($"Unknown Main option: {key}")
        };
    }
    return values;
}

static int Amount(string value)
{
    if (!int.TryParse(value, out int amount) || amount < 0)
        throw new ArgumentException("Resource amounts must be nonnegative whole numbers within their game limits.");
    return amount;
}
