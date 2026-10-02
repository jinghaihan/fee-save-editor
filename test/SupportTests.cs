using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class SupportTests
{
    public static void Run()
    {
        Check(SupportCatalog.Pairs.Count == 231 && SupportCatalog.Pairs.Count(pair => pair.IncludesAlear) == 40,
            "Base/DLC support pairs are incomplete.");
        var save = EngageSave.Parse(Fixture());
        Check(save.ReadSupports().Count == 232, "Support records were lost.");
        foreach (var pair in SupportCatalog.Pairs)
        {
            Check(SupportCatalog.Pair(pair.SecondPersonId + pair.FirstPersonId) == pair, "Reversed pairs are not equivalent.");
            for (int rank = 0; rank <= 3; rank++)
            {
                int points = pair.PointsForRank((SupportRank)rank);
                Check(pair.RankForPoints(points) == (SupportRank)rank, "Rank threshold is incorrect.");
                var edited = save.WithSupport(pair.Key, (SupportRank)rank);
                var entry = edited.ReadSupports().Single(row => row.Key == pair.Key);
                Check(entry.Rank == (SupportRank)rank && entry.Points == points && entry.Score == 7,
                    "Rank editing lost points or the map score.");
                Unrelated(save, edited, "UREL", "GDBD");
                Reject(() => save.WithSupport(pair.Key, (SupportRank)rank, pair.MaximumPoints((SupportRank)rank) + 1));
            }
            Reject(() => save.WithSupport(pair.Key, SupportRank.None, -1));
            Reject(() => save.WithSupport(pair.Key, (SupportRank)5));
            if (!pair.IncludesAlear || pair.OtherPersonId != "PID_ユナカ")
                Reject(() => save.WithSupport(pair.Key, SupportRank.APlus));
        }
        var maximum = save.WithMaximumSupports();
        Check(maximum.ReadSupports().Where(row => SupportCatalog.Pair(row.Key) is not null)
            .All(row => row.Rank == maximum.MaximumSupportRank(row.Key)), "Batch did not maximize every legal saved pair.");
        Check(maximum.ReadSupports().Last() == save.ReadSupports().Last(), "Batch edited an unknown support.");
        Check(ReferenceEquals(maximum, maximum.WithMaximumSupports()), "Repeated batch maximum is not a no-op.");
        var pact = maximum.ReadSupports().Single(row => row.Key == EmblemTests.Alear + "PID_ユナカ");
        Check(pact.Rank == SupportRank.APlus && pact.Points == 99, "Pact support did not reach its existing maximum.");
        Check(maximum.ReadEmblems()[2].Bonds[1].Level == 21, "Pact bond was not synchronized.");
        Reject(() => maximum.WithSupport(pact.Key, SupportRank.A));
        var onePair = SupportCatalog.Pairs.First(pair => !pair.IncludesAlear);
        var single = save.WithMaximumSupports(onePair.Key);
        Check(single.ReadSupports().Count(row => row.Rank != SupportRank.None) == 2, "Single maximum changed other pairs.");
        Unrelated(save, single, "UREL");
        Check(save.WithSupport(onePair.Key, SupportRank.None, 1).ReadSupports()
            .Single(row => row.Key == onePair.Key).Points == 1, "Partial support points were lost.");
        var lowPoints = save.WithSupport(onePair.Key, SupportRank.A, 2);
        Check(ReferenceEquals(lowPoints, lowPoints.WithMaximumSupports(onePair.Key)), "Existing A rank with low points was rewritten.");
        Reject(() => save.WithSupport("PID_unknownPID_unknown", SupportRank.A));
        Reject(() => EngageSave.Parse(Fixture(duplicate: true)).ReadSupports());
        Reject(() => EngageSave.Parse(Fixture(version: 2)).ReadSupports());
        Reject(() => EngageSave.Parse(Fixture(trailing: true)).ReadSupports());
        Reject(() => EngageSave.Parse(Fixture(truncated: true)).ReadSupports());
        var onlyUnknown = EngageSave.Parse(Fixture(onlyUnknown: true));
        Reject(() => onlyUnknown.WithSupport(onePair.Key, SupportRank.A));
        Check(ReferenceEquals(onlyUnknown, onlyUnknown.WithMaximumSupports()), "Unknown-only batch changed bytes.");
        var malformedBonds = save.Serialize();
        var bondSection = save.Sections.Single(row => row.Name == "GDBD");
        malformedBonds[bondSection.PayloadOffset] = 99;
        var malformed = EngageSave.Parse(Checksum(malformedBonds));
        byte[] original = malformed.Serialize();
        Reject(() => malformed.WithMaximumSupports());
        Check(malformed.Serialize().AsSpan().SequenceEqual(original), "A failed batch partially mutated the source.");
        Console.WriteLine("Support core: all 231 pairs, DLC thresholds, Pact limits, synchronization and byte preservation passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var entries = save.ReadSupports();
        Check(entries.Count == 231 && entries.All(row => SupportCatalog.Pair(row.Key) is not null),
            "Real support pairs do not match the complete catalog.");
        Check(entries.All(row => row.Rank == SupportRank.A), "Unexpected real support ranks.");
        Check(ReferenceEquals(save, save.WithMaximumSupports()), "Already-maxed real supports were altered.");
        foreach (var pair in SupportCatalog.Pairs.Where(row => !row.IncludesAlear))
        {
            var old = entries.Single(row => SupportCatalog.Pair(row.Key) == pair);
            var restored = save.WithSupport(old.Key, SupportRank.C).WithSupport(old.Key, old.Rank, old.Points);
            Check(save.Serialize().AsSpan().SequenceEqual(restored.Serialize()), "Real support round-trip was not exact.");
        }
        Console.WriteLine("Real supports: 231 base/DLC pairs, existing A ranks and exact edit restoration passed.");
    }

    public static byte[] Fixture(bool duplicate = false, uint version = 1, bool trailing = false,
        bool truncated = false, bool onlyUnknown = false)
    {
        byte[] original = EmblemTests.Fixture();
        var save = EngageSave.Parse(original);
        var god = save.Sections.Single(row => row.Name == "GDBD");
        byte[] pactPattern = [.. Encoding.Unicode.GetBytes("PID_ユナカ"), 3, 0, 0, 0, 21, 209, 0];
        int pactOffset = original.AsSpan(god.PayloadOffset, god.Length).IndexOf(pactPattern) + god.PayloadOffset + pactPattern.Length - 3;
        original[pactOffset] = 1; original[pactOffset + 1] = original[pactOffset + 2] = 0;
        var parts = save.Sections.Where(row => row.Name != "UREL")
            .Select(row => original.AsSpan(row.Offset, row.Length + 8).ToArray()).ToList();
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]);
            writer.Write((uint)((onlyUnknown ? 0 : 231) + 1 + (duplicate ? 1 : 0)));
            var keys = onlyUnknown ? new List<string>() : SupportCatalog.Pairs.Select(pair => pair.Key).ToList();
            keys.Add("PID_unknownPID_other");
            if (duplicate) keys.Add(SupportCatalog.Pairs[0].SecondPersonId + SupportCatalog.Pairs[0].FirstPersonId);
            foreach (string key in keys)
            {
                byte[] text = Encoding.Unicode.GetBytes(key); writer.Write((uint)text.Length); writer.Write(text);
                writer.Write(version); writer.Write((byte)(key.StartsWith("PID_unknown") ? 1 : 0));
                writer.Write((byte)0); writer.Write((byte)7);
            }
        }
        byte[] payload = buffer.ToArray();
        if (trailing) payload = [.. payload, (byte)0];
        if (truncated) payload = payload[..^1];
        byte[] section = new byte[payload.Length + 8];
        "LERU"u8.CopyTo(section); BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), (uint)payload.Length + 4);
        payload.CopyTo(section, 8); parts.Add(section);
        byte[] result = new byte[260 + parts.Sum(part => part.Length) + 8];
        original.AsSpan(0, 132).CopyTo(result);
        int offset = 260;
        for (int index = 0; index < parts.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(132 + index * 4), (uint)offset);
            parts[index].CopyTo(result, offset); offset += parts[index].Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(132 + parts.Count * 4), (uint)offset);
        "LVRC"u8.CopyTo(result.AsSpan(offset));
        return Checksum(result);
    }

    private static byte[] Checksum(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), ~crc);
        return bytes;
    }
    private static void Unrelated(EngageSave before, EngageSave after, params string[] changed)
    {
        byte[] source = before.Serialize(), target = after.Serialize();
        foreach (var section in before.Sections.Where(row => !changed.Contains(row.Name)))
        {
            var edited = after.Sections.Single(row => row.Name == section.Name);
            Check(source.AsSpan(section.Offset, section.Length + 8).SequenceEqual(target.AsSpan(edited.Offset, edited.Length + 8)),
                "Support editing changed an unrelated section.");
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid support edit was accepted.");
    }
}
