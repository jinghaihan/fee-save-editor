using System.Globalization;
using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class DonationsCommand
{
    public static int Run(string[] args)
    {
        if (args is ["donation-catalog", .. var catalogOptions])
        {
            string language = Language(catalogOptions);
            Print(DonationCatalog.Countries.Select(country => new { country.Id, Name = country.Name(language),
                country.Thresholds, country.MaximumLevel, MaximumAmount = DonationCatalog.MaximumAmount }));
            return 0;
        }
        if (args is ["donations", var input, .. var readOptions])
        {
            string language = Language(readOptions);
            Print(EngageSave.Load(input).ReadDonations().Select(row => new { row.Country.Id, Name = row.Country.Name(language),
                row.Level, row.Amount, row.Country.Thresholds, row.Country.MaximumLevel }));
            return 0;
        }
        if (args is not [var verb, var source, var output, .. var options] || verb is not ("donation-set" or "donations-max"))
            throw new ArgumentException("Unknown donation command. Run --help for usage.");
        var save = EngageSave.Load(source);
        if (verb == "donations-max" && options is ["--all"])
        {
            save.WithMaximumDonations().WriteCopy(output);
            Console.WriteLine(Path.GetFullPath(output));
            return 0;
        }
        string[] allowed = verb == "donations-max" ? ["--country"] : ["--country", "--level", "--amount"];
        if (options.Length % 2 != 0) throw new ArgumentException("Provide donation options and their values.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
            if (!allowed.Contains(options[index]) || !values.TryAdd(options[index], options[index + 1]))
                throw new ArgumentException($"Unknown or duplicate donation option: {options[index]}");
        string selector = values.GetValueOrDefault("--country") ?? throw new ArgumentException("Missing --country.");
        var country = DonationCatalog.Countries.SingleOrDefault(row => row.Id == selector
            || row.Name("en").Equals(selector, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Use Firene, Brodia, Elusia, Solm or an ID from main donation-catalog.");
        EngageSave edited;
        if (verb == "donations-max") edited = save.WithDonationLevel(country.Id, country.MaximumLevel);
        else
        {
            int? amount = values.TryGetValue("--amount", out string? text) ? Integer(text) : null;
            if (amount.HasValue) DonationCatalog.ValidateAmount(amount.Value);
            if (values.TryGetValue("--level", out string? levelText))
            {
                int level = Integer(levelText);
                int threshold = country.AmountForLevel(level);
                if (amount.HasValue && country.LevelForAmount(amount.Value) != level)
                    throw new ArgumentException("Donation level and amount must agree.");
                amount ??= threshold;
            }
            if (!amount.HasValue) throw new ArgumentException("Provide --level and/or --amount.");
            edited = save.WithDonations(new Dictionary<string, int> { [country.Id] = amount.Value });
        }
        edited.WriteCopy(output);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static int Integer(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
        ? value : throw new ArgumentException("Donation values must be whole numbers.");
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
