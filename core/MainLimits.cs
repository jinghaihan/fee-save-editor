namespace FeeEditor.Core;

public static class MainLimits
{
    public const int MaxMoney = 9_999_999;
    public const int MaxBondFragments = 9_999_999;
    public const int MaxIngots = 9_999;

    public static void ValidateAmounts(MainValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Validate(values.Money, MaxMoney, nameof(values.Money));
        Validate(values.BondFragments, MaxBondFragments, nameof(values.BondFragments));
        Validate(values.IronIngots, MaxIngots, nameof(values.IronIngots));
        Validate(values.SteelIngots, MaxIngots, nameof(values.SteelIngots));
        Validate(values.SilverIngots, MaxIngots, nameof(values.SilverIngots));
    }

    private static void Validate(int value, int maximum, string field)
    {
        if (value < 0 || value > maximum)
            throw new ArgumentOutOfRangeException(field, value, $"{field} must be between 0 and {maximum}.");
    }
}
