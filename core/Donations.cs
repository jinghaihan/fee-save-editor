using System.Text.Json;

namespace FeeEditor.Core;

public sealed record DonationCountry(string Id, string Key, Dictionary<string, string> Names, int[] Thresholds)
{
    public int MaximumLevel => Thresholds.Length;
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en"];
    public int AmountForLevel(int level)
    {
        if (level < 1 || level > MaximumLevel) throw new ArgumentException($"Donation level must be between 1 and {MaximumLevel}.");
        return Thresholds[level - 1];
    }
    public int LevelForAmount(int amount)
    {
        DonationCatalog.ValidateAmount(amount);
        return Thresholds.Count(threshold => threshold <= amount);
    }
}

public sealed record DonationProgress(DonationCountry Country, int Amount)
{
    public int Level => Country.LevelForAmount(Amount);
}

public static class DonationCatalog
{
    private sealed record CatalogData(DonationCountry[] Countries);
    public const int MaximumAmount = 9_999_999;
    public static IReadOnlyList<DonationCountry> Countries { get; } = Load();
    public static DonationCountry Country(string id) => Countries.SingleOrDefault(country => country.Id == id)
        ?? throw new ArgumentException("Select one of the four donation countries.");
    public static void ValidateAmount(int amount)
    {
        if (amount is < 0 or > MaximumAmount) throw new ArgumentException($"Donated gold must be between 0 and {MaximumAmount}.");
    }
    private static IReadOnlyList<DonationCountry> Load()
    {
        using var stream = typeof(DonationCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.donations.json")
            ?? throw new InvalidOperationException("The donation catalog is missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid donation catalog.");
        if (data.Countries.Length != 4 || data.Countries.Select(country => country.Id).Distinct().Count() != 4
            || data.Countries.Any(country => !country.Id.StartsWith("NID_", StringComparison.Ordinal)
                || country.Key != "G_投資_" + country.Id[4..] || country.Thresholds.Length != 5 || country.Thresholds[0] != 0
                || !country.Thresholds.SequenceEqual(country.Thresholds.Distinct().Order())
                || country.Thresholds.Any(value => value is < 0 or > MaximumAmount)
                || LanguageCatalog.Codes.Any(language => !country.Names.TryGetValue(language, out string? name)
                    || string.IsNullOrWhiteSpace(name))))
            throw new InvalidDataException("Invalid donation countries or thresholds.");
        return Array.AsReadOnly(data.Countries);
    }
}
