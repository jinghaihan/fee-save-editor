using System.Globalization;
using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class VariablesCommand
{
    public static int Run(string[] args)
    {
        if (args is ["list", var source, .. var options] && options is [] or ["--json"])
        {
            Console.WriteLine(JsonSerializer.Serialize(EngageSave.Load(source).ReadGameVariables(),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args is ["set", var input, var output, "--key", var key, "--value", var value]
            && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
        {
            EngageSave.Load(input).WithGameVariable(key, number).WriteCopy(output);
            Console.WriteLine(Path.GetFullPath(output));
            return 0;
        }
        throw new ArgumentException("Use variables list <save> [--json] or variables set <save> <new-file> --key <key> --value <signed-int32>.");
    }
}
