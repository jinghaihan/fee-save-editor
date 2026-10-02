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
        foreach (string malformed in new[] { "duplicate", "string", "negative" })
        {
            try { EngageSave.Parse(Fixture(malformed)).ReadMinigameRecords(); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed minigame data was accepted.");
        }
        Console.WriteLine("Minigames: record keys, game translations, missing fields, lossless reads and malformed data passed.");
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
        }
        Check(save.Serialize().AsSpan().SequenceEqual(bytes), "Real-save minigame reading changed bytes.");
        Console.WriteLine("Real minigames: 15 high scores and 20 catch counts matched raw fields without source writes.");
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
        if (malformed == "duplicate") { WriteString(writer, definitions[0].Key); writer.Write((byte)0); writer.Write(10); }
        byte[] extra = stream.ToArray();
        byte[] result = [.. original.AsSpan(0, end), .. extra, .. original.AsSpan(end)];
        foreach (int offset in new[] { 136, 140, 264, start }) Write32(result, offset, Read32(original, offset) + (uint)extra.Length);
        Write32(result, start + 36, Read32(original, start + 36) + (uint)(35 + (malformed == "duplicate" ? 1 : 0)));
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
