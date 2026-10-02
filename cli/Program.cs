using System.Reflection;

if (args is ["--version"])
{
    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
    return 0;
}

Console.WriteLine("Fire Emblem Engage Save Editor\nUsage: FeeEditor.Cli --version");
return args.Length == 0 || args is ["--help"] ? 0 : 1;
