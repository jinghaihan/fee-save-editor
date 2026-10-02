using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class BondRingTests
{
    public static void Run()
    {
        Check(BondRingCatalog.SRings.Count == 123 && BondRingCatalog.SRings.Count(ring => ring.SingleRank) == 3,
            "The S-ring collection must include 120 ordinary rings and three Heroes bonus rings.");
        foreach (var ring in EmblemCatalog.Rings)
        {
            var meld = BondRingCatalog.Melding(ring.Hash);
            if (ring.Rank == 3)
                Check(meld is null, "An S ring can be melded beyond its highest rank.");
            else
            {
                Check(meld is not null && meld.Result.Group == ring.Group && meld.Result.EmblemId == ring.EmblemId
                    && meld.Result.Rank == ring.Rank + 1 && meld.RequiredRings == ring.Rank + 2
                    && meld.BondFragments == new[] { 100, 1000, 10000 }[ring.Rank], "Incorrect ring melding rule.");
            }
        }
        var source = EngageSave.Parse(EmblemTests.Fixture());
        var full = source.WithMissingSBondRings();
        Check(BondRingCatalog.SRings.All(definition => full.ReadBondRings().Any(ring => ring.RingHash == definition.Hash && ring.StockCount > 0)),
            "Fill S left a missing S ring.");
        Check(full.ReadBondRings().Count == 124 && source.ReadBondRings().All(ring => full.ReadBondRings().Contains(ring)),
            "Fill S replaced an existing ring or created duplicate S copies.");
        Check(full.ReadMainValues() == source.ReadMainValues(), "Fill S consumed resources or changed Main values.");
        Unrelated(source, full, "RING", "USER");
        Check(ReferenceEquals(full, full.WithMissingSBondRings()), "Fill S must be idempotent.");
        var zero = source.WithBondRingStock(11, 0);
        var restored = zero.WithMissingSBondRings();
        Check(restored.ReadBondRings().Single(ring => ring.InstanceId == 11).StockCount == 1, "A zero-stock S row was not reused.");
        Check(Flag(restored, "G_指輪_紋章_シーダ") == 8, "The acquired S ring was not recorded in its collection flag.");
        var empty = Fixture([]);
        Check(empty.WithMissingSBondRings().ReadBondRings().Count == 123, "An empty ring pool could not be filled.");
        var unknown = Fixture([new(1, 0x12345678, 42, null)]);
        Check(unknown.WithMissingSBondRings().ReadBondRings().Contains(unknown.ReadBondRings()[0]), "Fill S erased an unknown ring.");
        var occupied = Enumerable.Range(1, 700).Select(id => new BondRing((uint)id, 0x12345678, 1, null)).ToArray();
        var capacity = Fixture(occupied);
        Reject(() => capacity.WithMissingSBondRings());
        Check(capacity.ReadBondRings().Count == 700, "A failed batch partially changed its source.");
        Reject(() => Fixture([new(751, 0x12345678, 1, null)]).WithMissingSBondRings());

        for (int rank = 0; rank < 3; rank++)
        {
            var definition = EmblemCatalog.Rings.Single(ring => ring.Group == "紋章_シーダ" && ring.Rank == rank);
            var rule = BondRingCatalog.Melding(definition.Hash)!;
            var save = Fixture([new(10, definition.Hash, rule.RequiredRings + 1, null)])
                .WithMainValues(source.ReadMainValues() with { BondFragments = 20000 });
            var melded = save.WithMeldedBondRing(10);
            Check(melded.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 1
                && melded.ReadBondRings().Single(ring => ring.RingHash == rule.Result.Hash).StockCount == 1,
                "Melding consumed the wrong materials or created the wrong rank.");
            Check(melded.ReadMainValues() == save.ReadMainValues() with { BondFragments = 20000 - rule.BondFragments },
                "Melding deducted the wrong fragment amount.");
            Check((Flag(melded, definition.AcquisitionKey) & (1 << (rank + 1))) != 0, "Melding missed the collection flag.");
            Unrelated(save, melded, "RING", "USER");
            Reject(() => save.WithBondRingStock(10, rule.RequiredRings - 1).WithMeldedBondRing(10));
            Reject(() => save.WithMainValues(save.ReadMainValues() with { BondFragments = rule.BondFragments - 1 }).WithMeldedBondRing(10));
        }
        var caeda = EmblemCatalog.Ring(ItemCatalog.Hash(EmblemTests.Caeda))!;
        var result = BondRingCatalog.Melding(caeda.Hash)!.Result;
        var exact = Fixture([new(10, caeda.Hash, 2, null)]).WithMeldedBondRing(10);
        Check(exact.ReadBondRings().Count == 1 && exact.ReadBondRings()[0].RingHash == result.Hash
            && exact.ReadBondRings()[0].InstanceId == 1, "Consumed rows were not removed or free native IDs were not reused.");
        var next = exact.WithBondRingStock(1, 3).WithMeldedBondRing(1);
        Check(Flag(next, caeda.AcquisitionKey) == 6, "Melding erased an already acquired lower-rank flag.");
        var multiple = Fixture([new(10, caeda.Hash, 1, null), new(11, caeda.Hash, 1, null), new(12, result.Hash, 5, null)]);
        Check(multiple.WithMeldedBondRing(10).ReadBondRings().Single() == new BondRing(12, result.Hash, 6, null),
            "Melding did not combine same-rank stocks and reuse the next-rank stack.");
        Reject(() => source.WithMeldedBondRing(11));
        Reject(() => source.WithMeldedBondRing(999));
        Reject(() => unknown.WithMeldedBondRing(1));
        var capped = Fixture([new(10, caeda.Hash, 2, null), new(11, result.Hash, 99, null)]);
        Reject(() => capped.WithMeldedBondRing(10));
        var malformed = Fixture([new(10, caeda.Hash, 100, null)]);
        Reject(() => malformed.WithMeldedBondRing(10));
        var equipped = Fixture([new(10, caeda.Hash, 1, null), new(11, caeda.Hash, 2, null)], RosterTests.Fixture(validEquipment: true));
        Reject(() => equipped.WithMeldedBondRing(10));
        var protectedResult = equipped.WithMeldedBondRing(11);
        Check(protectedResult.ReadBondRings().Single(ring => ring.InstanceId == 10) == equipped.ReadBondRings()[0]
            && protectedResult.ReadCharacterRingLinks().SequenceEqual(equipped.ReadCharacterRingLinks()), "Melding consumed or unequipped a worn ring.");
        var insufficient = equipped.WithBondRingStock(11, 1);
        Reject(() => insufficient.WithMeldedBondRing(11));
        Console.WriteLine("Bond rings: full S collection, native IDs/capacity, all melding ranks/costs and equipped protection passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var full = save.WithMissingSBondRings();
        Check(full.ReadBondRings().Where(ring => EmblemCatalog.Ring(ring.RingHash)?.Rank == 3 && ring.StockCount > 0)
            .Select(ring => ring.RingHash).Distinct().Count() == 123, "Real S collection is incomplete.");
        Check(save.ReadCharacterRingLinks().SequenceEqual(full.ReadCharacterRingLinks()), "Real ring owners changed.");
        Check(save.ReadMainValues() == full.ReadMainValues(), "Real fill S changed resources.");
        Unrelated(save, full, "RING", "USER");
        var ring = save.ReadBondRings().First(ring => !ring.OwnerIndex.HasValue && BondRingCatalog.Melding(ring.RingHash) is not null);
        var rule = BondRingCatalog.Melding(ring.RingHash)!;
        var prepared = save.WithBondRingStock(ring.InstanceId, rule.RequiredRings)
            .WithMainValues(save.ReadMainValues() with { BondFragments = 20000 });
        var melded = prepared.WithMeldedBondRing(ring.InstanceId);
        Check(melded.ReadMainValues().BondFragments == prepared.ReadMainValues().BondFragments - rule.BondFragments,
            "Real melding failed to charge fragments.");
        Check(melded.ReadCharacterRingLinks().SequenceEqual(save.ReadCharacterRingLinks()), "Real melding changed owners.");
        Unrelated(prepared, melded, "RING", "USER");
    }

    internal static EngageSave Fixture(BondRing[] rings, byte[]? original = null)
    {
        var baseSave = EngageSave.Parse(EmblemTests.Fixture());
        var bytes = baseSave.Serialize();
        var section = baseSave.Sections.Single(section => section.Name == "GDBD");
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]); writer.Write((uint)rings.Length);
        foreach (var ring in rings)
        { writer.Write(ring.InstanceId); writer.Write((ushort)0xefcd); writer.Write(ring.RingHash); writer.Write((byte)ring.StockCount); }
        return EngageSave.Parse(EmblemTests.Container(bytes.AsSpan(section.PayloadOffset, section.Length).ToArray(), output.ToArray(), original));
    }

    private static int Flag(EngageSave save, string key)
    {
        var bytes = save.Serialize();
        var section = save.Sections.Single(section => section.Name == "USER");
        int offset = section.PayloadOffset + 85;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)); offset += 4;
        for (uint index = 0; index < count; index++)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)); offset += 4;
            string name = Encoding.Unicode.GetString(bytes, offset, length); offset += length;
            byte kind = bytes[offset++];
            if (kind == 0)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)); offset += 4;
                if (name == key) return value;
            }
            else { int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)); offset += 4 + size; }
        }
        throw new InvalidOperationException("Missing acquisition flag.");
    }

    private static void Unrelated(EngageSave before, EngageSave after, params string[] changed)
    {
        var source = before.Serialize(); var target = after.Serialize();
        Check(source.AsSpan(0, 132).SequenceEqual(target.AsSpan(0, 132)), "Ring management changed the save summary.");
        foreach (var section in before.Sections.Where(section => !changed.Contains(section.Name)))
        {
            var other = after.Sections.Single(candidate => candidate.Name == section.Name);
            Check(source.AsSpan(section.Offset, section.Length + 8).SequenceEqual(target.AsSpan(other.Offset, other.Length + 8)),
                "Ring management changed an unrelated section.");
        }
        EngageSave.Parse(target);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("An unsafe ring operation succeeded.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
