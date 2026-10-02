namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<DonationProgress> ReadDonations()
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        return DonationCatalog.Countries.Select(country =>
        {
            int amount = layout.IntegerOffset(country.Key) is int offset ? unchecked((int)ReadUInt32(_bytes, offset)) : 0;
            DonationCatalog.ValidateAmount(amount);
            return new DonationProgress(country, amount);
        }).ToArray();
    }

    public EngageSave WithDonations(IReadOnlyDictionary<string, int> amounts)
    {
        ReadDonations();
        var values = new Dictionary<string, int>();
        foreach (var (id, amount) in amounts)
        {
            DonationCatalog.ValidateAmount(amount);
            values.Add(DonationCatalog.Country(id).Key, amount);
        }
        var edited = WithGameIntegerValues(values);
        var actual = edited.ReadDonations();
        if (amounts.Any(pair => actual.Single(row => row.Country.Id == pair.Key).Amount != pair.Value))
            throw new InvalidDataException("Edited donation amounts did not survive serialization.");
        return edited;
    }

    public EngageSave WithDonationLevel(string country, int level) =>
        WithDonations(new Dictionary<string, int> { [country] = DonationCatalog.Country(country).AmountForLevel(level) });

    public EngageSave WithMaximumDonations() => WithDonations(DonationCatalog.Countries.ToDictionary(
        country => country.Id, country => country.AmountForLevel(country.MaximumLevel)));
}
