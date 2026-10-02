using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeeEditor.Core;

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
          FeeEditor.Cli --version
        """);
    return 0;
}

try
{
    switch (args)
    {
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
