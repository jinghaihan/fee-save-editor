using System.Text.Json;

namespace FeeEditor.Core;

public sealed record BondRingMeldRule(int SourceRank, int RequiredRings, int BondFragments);
public sealed record BondRingMeld(BondRingDefinition Source, BondRingDefinition Result, int RequiredRings, int BondFragments);

public static class BondRingCatalog
{
    private sealed record CatalogData(BondRingMeldRule[] Rules);
    private static readonly BondRingMeldRule[] Rules = LoadRules();
    public const int MaxInstances = 750;
    public const int MaxUnequippedInstances = 700;
    public const int MaxEquippedInstances = 50;
    public static IReadOnlyList<BondRingDefinition> SRings { get; } = Array.AsReadOnly(EmblemCatalog.Rings.Where(ring => ring.Rank == 3).ToArray());

    public static BondRingMeld? Melding(uint hash)
    {
        var source = EmblemCatalog.Ring(hash);
        if (source is null || source.SingleRank || source.Rank == 3) return null;
        var result = EmblemCatalog.Rings.SingleOrDefault(ring => ring.Group == source.Group && ring.Rank == source.Rank + 1);
        if (result is null) return null;
        var rule = Rules.Single(rule => rule.SourceRank == source.Rank);
        return new(source, result, rule.RequiredRings, rule.BondFragments);
    }

    private static BondRingMeldRule[] LoadRules()
    {
        using var stream = typeof(BondRingCatalog).Assembly.GetManifestResourceStream("FeeEditor.Core.Data.bond-ring-melding.json")
            ?? throw new InvalidOperationException("The bond-ring melding rules are missing.");
        var data = JsonSerializer.Deserialize<CatalogData>(stream) ?? throw new InvalidDataException("Invalid bond-ring melding rules.");
        if (!data.Rules.Select(rule => rule.SourceRank).Order().SequenceEqual(new[] { 0, 1, 2 })
            || data.Rules.Any(rule => rule.RequiredRings is < 2 or > 99 || rule.BondFragments is < 0 or > MainLimits.MaxBondFragments))
            throw new InvalidDataException("Invalid bond-ring melding costs.");
        return data.Rules;
    }
}
