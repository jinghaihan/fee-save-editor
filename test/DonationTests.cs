using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class DonationTests
{
    public static void Run()
    {
        Check(DonationCatalog.Countries.Count == 4, "Donation catalog must contain four countries.");
        foreach (var country in DonationCatalog.Countries)
        {
            Check(country.Thresholds.SequenceEqual(new[] { 0, 5000, 15000, 40000, 90000 }), "Incorrect donation thresholds.");
            for (int level = 1; level <= 5; level++)
            {
                int amount = country.AmountForLevel(level);
                Check(country.LevelForAmount(amount) == level, "Donation boundary has the wrong level.");
                if (amount > 0) Check(country.LevelForAmount(amount - 1) == level - 1, "Donation level rounds upward.");
            }
            Check(country.LevelForAmount(9_999_999) == 5, "Over-max donations must retain level five.");
            Reject(() => country.AmountForLevel(0)); Reject(() => country.AmountForLevel(6));
        }
        var missing = EngageSave.Parse(MainTests.Fixture());
        Check(missing.ReadDonations().All(row => row.Amount == 0 && row.Level == 1), "Absent donation variables must default to zero.");
        Check(missing.WithDonationLevel(DonationCatalog.Countries[0].Id, 1).Serialize().AsSpan().SequenceEqual(missing.Serialize()),
            "Setting absent zero donation created a variable.");
        var inserted = missing.WithMaximumDonations();
        Check(inserted.ReadDonations().All(row => row.Level == 5 && row.Amount == 90000), "Missing variables were not inserted.");
        Unrelated(missing, inserted);
        Check(missing.ReadDonations().All(row => row.Amount == 0), "Insertion mutated the source.");

        byte[] original = Fixture();
        var save = EngageSave.Parse(original);
        Check(save.ReadDonations().Select(row => row.Amount).SequenceEqual(new[] { 5100, 15000, 90000, 1_000_000 }), "Saved amounts were clamped.");
        Check(save.WithDonations(save.ReadDonations().ToDictionary(row => row.Country.Id, row => row.Amount)).Serialize()
            .AsSpan().SequenceEqual(original), "No-op donations changed bytes.");
        var first = DonationCatalog.Countries[0];
        var edited = save.WithDonationLevel(first.Id, 4);
        Check(edited.ReadDonations()[0].Amount == 40000, "Level edit used incremental instead of cumulative cost.");
        Check(edited.WithDonations(new Dictionary<string, int> { [first.Id] = 5100 }).Serialize().AsSpan().SequenceEqual(original),
            "Donation edit was not exactly reversible.");
        ExactChanges(save, edited, first.Key);
        var maximum = save.WithMaximumDonations();
        Check(maximum.ReadDonations().All(row => row.Amount == 90000), "Max donation did not set exact level-five amounts.");
        ExactChanges(save, maximum, DonationCatalog.Countries.Select(row => row.Key).ToArray());
        foreach (int value in new[] { -1, 10_000_000 })
            Reject(() => save.WithDonations(new Dictionary<string, int> { [first.Id] = value }));
        Reject(() => save.WithDonationLevel("NID_unknown", 5));
        Reject(() => save.WithDonations(new Dictionary<string, int> { [first.Id] = 90000, [DonationCatalog.Countries[1].Id] = -1 }));
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Rejected donation edits mutated the source.");
        foreach (string malformed in new[] { "duplicate", "string", "negative", "overflow", "type", "version", "length", "count" })
            Reject(() => EngageSave.Parse(Fixture(malformed)).ReadDonations());
        Console.WriteLine("Donation thresholds, insertion, preservation and malformed-input tests passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var current = save.ReadDonations();
        foreach (var row in current)
        {
            var edited = save.WithDonationLevel(row.Country.Id, 2);
            Check(edited.ReadDonations().Single(value => value.Country.Id == row.Country.Id).Amount == 5000, "Real donation edit failed.");
            ExactChanges(save, edited, row.Country.Key);
            Check(edited.WithDonations(new Dictionary<string, int> { [row.Country.Id] = row.Amount }).Serialize()
                .AsSpan().SequenceEqual(save.Serialize()), "Real donation restoration changed unrelated bytes.");
        }
        Console.WriteLine("Real-save donation edits and exact restoration passed.");
    }

    public static byte[] Fixture(string? malformed = null)
    {
        byte[] original = MainTests.Fixture();
        int start = 268 + 49, length = (int)Read32(original, start), end = start + length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        int[] amounts = [5100, 15000, 90000, 1_000_000];
        for (int index = 0; index < 4; index++)
        {
            WriteKey(writer, DonationCatalog.Countries[index].Key);
            if (index == 0 && malformed == "string") { writer.Write((byte)1); WriteKey(writer, "wrong type"); }
            else
            {
                writer.Write((byte)(index == 0 && malformed == "type" ? 2 : 0));
                int amount = amounts[index];
                if (index == 0 && malformed == "negative") amount = -1;
                if (index == 0 && malformed == "overflow") amount = 10_000_000;
                writer.Write(amount);
            }
        }
        // Achievement progress and claimed rewards are independent from donation amounts.
        WriteKey(writer, "G_実績_投資合計"); writer.Write((byte)0); writer.Write(123456);
        WriteKey(writer, "G_実績_フィレネの投資段階が５になった"); writer.Write((byte)0); writer.Write(3);
        if (malformed == "duplicate") { WriteKey(writer, DonationCatalog.Countries[0].Key); writer.Write((byte)0); writer.Write(42); }
        byte[] extra = stream.ToArray();
        byte[] result = [.. original.AsSpan(0, end), .. extra, .. original.AsSpan(end)];
        Write32(result, start, (uint)(length + extra.Length));
        Write32(result, start + 36, Read32(original, start + 36) + (uint)(malformed == "duplicate" ? 7 : 6));
        Write32(result, 264, Read32(original, 264) + (uint)extra.Length);
        Write32(result, 136, Read32(original, 136) + (uint)extra.Length);
        Write32(result, 140, Read32(original, 140) + (uint)extra.Length);
        if (malformed == "version") Write32(result, start + 4, 1);
        if (malformed == "length") Write32(result, start, uint.MaxValue);
        if (malformed == "count") Write32(result, start + 36, uint.MaxValue);
        Write32(result, result.Length - 4, Checksum(result.AsSpan(0, result.Length - 4)));
        return result;
    }

    private static void ExactChanges(EngageSave before, EngageSave after, params string[] keys)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        Check(original.Length == edited.Length, "Updating existing donations resized the save.");
        var offsets = new HashSet<int>();
        foreach (string key in keys)
        {
            byte[] text = Encoding.Unicode.GetBytes(key);
            int offset = original.AsSpan().IndexOf(text) + text.Length + 1;
            Check(offset >= text.Length + 1, "Independent donation fixture key was not found.");
            for (int index = 0; index < 4; index++) offsets.Add(offset + index);
        }
        for (int index = 0; index < original.Length - 4; index++)
            Check(original[index] == edited[index] || offsets.Contains(index), "Donation edit changed rewards, progression or other data.");
        Unrelated(before, after);
    }

    private static void Unrelated(EngageSave before, EngageSave after)
    {
        Check(before.ReadMainValues() == after.ReadMainValues(), "Donation edit changed Main resources/settings.");
        byte[] original = before.Serialize(), edited = after.Serialize();
        Check(original.AsSpan(0, 128).SequenceEqual(edited.AsSpan(0, 128)), "Donation edit changed the summary.");
        foreach (var section in before.Sections.Where(section => section.Name != "USER"))
        {
            var target = after.Sections.Single(row => row.Name == section.Name);
            Check(original.AsSpan(section.Offset, section.Length + 8).SequenceEqual(edited.AsSpan(target.Offset, target.Length + 8)),
                "Donation edit changed an unrelated section.");
        }
    }
    private static void WriteKey(BinaryWriter writer, string key) { byte[] bytes = Encoding.Unicode.GetBytes(key); writer.Write(bytes.Length); writer.Write(bytes); }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid donation data was accepted.");
    }
}
