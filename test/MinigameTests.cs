using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class MinigameTests
{
    public static void Run()
    {
        var groups = MinigameCatalog.Groups;
        Check(groups.Count == 5 && groups.Sum(group => group.Records.Length) == 35, "Minigame records are incomplete.");
        Check(groups[0].Records.Select(row => row.Key).SequenceEqual(new[] { "G_MusclePushUpBestNormal",
            "G_MusclePushUpBestHard", "G_MusclePushUpBestMaster", "G_MusclePushUpBestEternal" }), "Training keys are incorrect.");
        Check(groups[3].Records[2].Key == "G_DragonRideExpertScore", "Wyvern Expert uses the wrong key.");
        Check(groups[4].Records[0].Name("en") == "Charwhal" && groups[4].Records[0].Name("zh-Hans") == "独角红点鲑",
            "Fish names are not game translations.");
        var missing = EngageSave.Parse(MainTests.Fixture());
        byte[] original = missing.Serialize();
        Check(missing.ReadMinigameRecords().All(row => row.Value == 0), "Absent minigame records did not default to zero.");
        Check(missing.Serialize().AsSpan().SequenceEqual(original), "Reading missing records inserted variables.");
        var save = EngageSave.Parse(Fixture());
        original = save.Serialize();
        Check(save.ReadMinigameRecords().Select(row => row.Value).SequenceEqual(Enumerable.Range(1, 35).Select(value => value * 100)),
            "Minigame record values were read incorrectly.");
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Reading minigames changed the save.");
        Check(save.WithMinigameRecords(save.ReadMinigameRecords().ToDictionary(row => row.Definition.Key, row => row.Values))
            .Serialize().AsSpan().SequenceEqual(original), "Unchanged minigames changed bytes or inserted zero fields.");
        var targets = groups.SelectMany(group => group.Records).ToDictionary(row => row.Key,
            row => new MinigameValues(100, row.RankKey is null ? null : row.MaximumRank, row.SizeKey is null ? null : 123));
        var edited = missing.WithMinigameRecords(targets);
        var reloaded = EngageSave.Parse(edited.Serialize()).ReadMinigameRecords();
        Check(reloaded.All(row => row.Values == targets[row.Definition.Key]), "Inserted minigame values did not round trip.");
        Check(missing.Serialize().AsSpan().SequenceEqual(MainTests.Fixture()), "Editing changed the input save.");
        var one = save.ReadMinigameRecords()[0];
        Check(save.WithMinigameRecords(new Dictionary<string, MinigameValues> { [one.Definition.Key] = new(int.MaxValue) })
            .ReadMinigameRecords()[0].Value == int.MaxValue, "The signed integer upper boundary was rejected.");
        foreach (var invalid in new[] {
            new KeyValuePair<string, MinigameValues>("G_unknown", new(1)),
            new(one.Definition.Key, new(-1)), new(one.Definition.Key, new(1, 1)), new(one.Definition.Key, new(1, BestSize: 1)),
            new(groups[3].Records[0].Key, new(1, 10)), new(groups[3].Records[0].Key, new(1, BestSize: 1)),
            new(groups[4].Records[0].Key, new(1, -1)), new(groups[4].Records[0].Key, new(1, 6)),
            new(groups[4].Records[0].Key, new(1, BestSize: -1)) })
        {
            try { save.WithMinigameRecords(new Dictionary<string, MinigameValues> { [invalid.Key] = invalid.Value }); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid minigame edits were accepted.");
        }
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Rejected edits changed the source.");
        foreach (string malformed in new[] { "duplicate", "string", "negative", "rank-duplicate", "rank-string", "rank-invalid", "size-negative" })
        {
            try { EngageSave.Parse(Fixture(malformed)).ReadMinigameRecords(); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed minigame data was accepted.");
        }
        Console.WriteLine("Minigames: 78 editable fields, ranks, missing-variable insertion, boundaries and atomic validation passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        byte[] bytes = save.Serialize();
        var records = save.ReadMinigameRecords();
        foreach (var row in records)
        {
            byte[] key = Encoding.Unicode.GetBytes(row.Definition.Key);
            int offset = bytes.AsSpan().IndexOf(key);
            Check(offset >= 0 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + key.Length + 1)) == row.Value,
                "A minigame value differs from the independently located raw field.");
            foreach (var (relatedKey, value) in new[] { (row.Definition.RankKey, row.Rank), (row.Definition.SizeKey, row.BestSize) })
            {
                if (relatedKey is null) continue;
                byte[] related = Encoding.Unicode.GetBytes(relatedKey);
                int position = bytes.AsSpan().IndexOf(related);
                Check(position >= 0 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + related.Length + 1)) == value,
                    "A stored rank or size differs from its raw field.");
            }
        }
        var targets = records.ToDictionary(row => row.Definition.Key,
            row => new MinigameValues(row.Value + 1, row.Rank is null ? null : (row.Rank + 1) % (row.Definition.MaximumRank + 1),
                row.BestSize is null ? null : row.BestSize + 1));
        byte[] edited = save.WithMinigameRecords(targets).Serialize();
        var allowed = new HashSet<int>(Enumerable.Range(bytes.Length - 4, 4));
        foreach (var definition in records.Select(row => row.Definition))
        {
            foreach (string key in new[] { definition.Key, definition.RankKey, definition.SizeKey }.OfType<string>())
            {
                byte[] encoded = Encoding.Unicode.GetBytes(key);
                int offset = bytes.AsSpan().IndexOf(encoded) + encoded.Length + 1;
                foreach (int position in Enumerable.Range(offset, 4)) allowed.Add(position);
            }
        }
        Check(edited.Length == bytes.Length && Enumerable.Range(0, bytes.Length).All(index => bytes[index] == edited[index] || allowed.Contains(index)),
            "Real minigame edits changed bytes outside the requested fields and CRC.");
        var restored = EngageSave.Parse(edited).WithMinigameRecords(records.ToDictionary(row => row.Definition.Key, row => row.Values));
        Check(restored.Serialize().AsSpan().SequenceEqual(bytes), "Restoring real minigame records did not restore every byte.");
        Check(save.Serialize().AsSpan().SequenceEqual(bytes), "Real-save minigame reading changed bytes.");
        Console.WriteLine("Real minigames: all 78 fields matched raw data; edits touched only target integers and CRC, then restored every byte.");
    }

    public static byte[] Fixture(string? malformed = null)
    {
        byte[] original = MainTests.Fixture();
        const int start = 317;
        int length = (int)Read32(original, start), end = start + length;
        var definitions = MinigameCatalog.Groups.SelectMany(group => group.Records).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        for (int index = 0; index < definitions.Length; index++)
        {
            WriteString(writer, definitions[index].Key);
            if (index == 0 && malformed == "string") { writer.Write((byte)1); WriteString(writer, "not a record"); }
            else { writer.Write((byte)0); writer.Write(index == 0 && malformed == "negative" ? -1 : (index + 1) * 100); }
        }
        int added = 35;
        if (malformed == "duplicate") { WriteString(writer, definitions[0].Key); writer.Write((byte)0); writer.Write(10); added++; }
        if (malformed?.StartsWith("rank-", StringComparison.Ordinal) == true)
        {
            WriteString(writer, definitions[12].RankKey!);
            if (malformed == "rank-string") { writer.Write((byte)1); WriteString(writer, "SSS"); }
            else { writer.Write((byte)0); writer.Write(malformed == "rank-invalid" ? 10 : 1); }
            added++;
            if (malformed == "rank-duplicate") { WriteString(writer, definitions[12].RankKey!); writer.Write((byte)0); writer.Write(2); added++; }
        }
        if (malformed == "size-negative") { WriteString(writer, definitions[15].SizeKey!); writer.Write((byte)0); writer.Write(-1); added++; }
        byte[] extra = stream.ToArray();
        byte[] result = [.. original.AsSpan(0, end), .. extra, .. original.AsSpan(end)];
        foreach (int offset in new[] { 136, 140, 264, start }) Write32(result, offset, Read32(original, offset) + (uint)extra.Length);
        Write32(result, start + 36, Read32(original, start + 36) + (uint)added);
        uint crc = uint.MaxValue;
        foreach (byte value in result.AsSpan(0, result.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        Write32(result, result.Length - 4, ~crc);
        return result;
    }

    private static void WriteString(BinaryWriter writer, string text) { byte[] bytes = Encoding.Unicode.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
