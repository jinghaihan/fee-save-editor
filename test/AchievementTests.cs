using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class AchievementTests
{
    public static void Run()
    {
        var catalog = AchievementCatalog.Achievements;
        Check(catalog.Count == 765 && catalog.GroupBy(row => row.Category).OrderBy(row => row.Key)
            .Select(group => group.Count()).SequenceEqual(new[] { 97, 167, 194, 143, 164 }), "Achievement categories are incomplete.");
        Check(catalog[0].Name("en") == "Complete a support conversation with Vander."
            && catalog[0].Name("zh-Hans").Contains("凡德雷"), "Person argument was not localized.");
        Check(catalog.All(row => !row.Name("en").Contains("\\x") && !row.Name("zh-Hans").Contains("\\x")),
            "Game message placeholders leaked into the catalog.");
        var missing = EngageSave.Parse(MainTests.Fixture());
        Check(missing.ReadAchievements().All(row => !row.Achieved), "Absent achievement flags must default to None.");
        var inserted = missing.WithUnlockedAchievements(catalog[0].Id);
        Check(inserted.ReadAchievements()[0].Status == AchievementStatus.Cleared, "Missing flag was not inserted as Cleared.");
        Check(inserted.ReadAchievements().Skip(1).All(row => !row.Achieved), "Single unlock affected other achievements.");
        PreserveSections(missing, inserted);
        var save = EngageSave.Parse(Fixture());
        Check(save.ReadAchievements().Take(4).Select(row => row.Status).SequenceEqual(new[] {
            AchievementStatus.None, AchievementStatus.Cleared, AchievementStatus.Showed, AchievementStatus.Completed }), "Saved states changed on read.");
        Check(save.ReadAchievements()[1].RewardAvailable && save.ReadAchievements()[2].RewardAvailable
            && save.ReadAchievements()[3].RewardClaimed && !save.ReadAchievements()[3].RewardAvailable, "Reward state is wrong.");
        foreach (var row in catalog.Skip(1).Take(3))
            Check(save.WithUnlockedAchievements(row.Id).Serialize().AsSpan().SequenceEqual(save.Serialize()), "Unlocking an achieved flag downgraded it.");
        var single = save.WithUnlockedAchievements(catalog[0].Id);
        ExactExistingChanges(save, single, catalog[0].Key);
        var all = save.WithUnlockedAchievements();
        Check(all.ReadAchievements().All(row => row.Achieved), "Batch unlock did not include all named achievements.");
        Check(all.ReadAchievements().Take(4).Select(row => row.Status).SequenceEqual(new[] {
            AchievementStatus.Cleared, AchievementStatus.Cleared, AchievementStatus.Showed, AchievementStatus.Completed }), "Batch reset reward flags.");
        Check(all.WithUnlockedAchievements().Serialize().AsSpan().SequenceEqual(all.Serialize()), "Batch unlock is not idempotent.");
        Check(all.ReadMainValues() == save.ReadMainValues() && all.ReadDonations().SequenceEqual(save.ReadDonations()), "Unlock changed resources or donations.");
        foreach (string key in new[] { "G_実績_Pクラスチェンジ", "G_実績_総投資額", "G_クリア_M001" })
            Check(ReadVariable(all.Serialize(), key) == ReadVariable(save.Serialize(), key), "Unlock altered a counter, statistic or chapter flag.");
        PreserveSections(save, all);
        foreach (string malformed in new[] { "duplicate", "string", "negative", "overflow" })
            Reject(() => EngageSave.Parse(Fixture(malformed)).ReadAchievements());
        Reject(() => save.WithUnlockedAchievements("AID_unknown"));
        Reject(() => save.WithUnlockedAchievements("AID_Pクラスチェンジ"));
        Check(save.ReadAchievements()[0].Status == AchievementStatus.None, "Unlock mutated the original save.");
        Console.WriteLine("Achievements: 765 localized entries, statuses, insertion, reward preservation and atomic validation passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var before = save.ReadAchievements();
        var changed = before.Where(row => !row.Achieved).ToArray();
        var edited = save.WithUnlockedAchievements();
        Check(edited.ReadAchievements().All(row => row.Achieved), "Real-save unlock failed.");
        ExactExistingChanges(save, edited, changed.Select(row => row.Definition.Key).ToArray());
        var actual = edited.ReadAchievements().ToDictionary(row => row.Definition.Id);
        Check(before.Where(row => row.Achieved).All(row => actual[row.Definition.Id].Status == row.Status), "Real-save rewards were reset.");
        Console.WriteLine($"Real achievements: {before.Count(row => row.Achieved)}/{before.Count} achieved; {changed.Length} pending flags verified without source writes.");
    }

    public static byte[] Fixture(string? malformed = null)
    {
        byte[] original = MainTests.Fixture();
        const int start = 317;
        int end = start + (int)Read32(original, start);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var rows = AchievementCatalog.Achievements.Take(4).Select((row, index) => (row.Key, Value: index)).ToList();
        rows.AddRange(new[] { ("G_実績_Pクラスチェンジ", 123), ("G_実績_総投資額", 45000), ("G_クリア_M001", 1) });
        if (malformed == "duplicate") rows.Add(rows[0]);
        for (int index = 0; index < rows.Count; index++)
        {
            WriteString(writer, rows[index].Key);
            if (index == 0 && malformed == "string") { writer.Write((byte)1); WriteString(writer, "preserve"); }
            else
            {
                writer.Write((byte)0);
                int value = rows[index].Value;
                if (index == 0 && malformed == "negative") value = -1;
                if (index == 0 && malformed == "overflow") value = 4;
                writer.Write(value);
            }
        }
        byte[] extra = stream.ToArray();
        byte[] bytes = [.. original.AsSpan(0, end), .. extra, .. original.AsSpan(end)];
        foreach (int offset in new[] { 136, 140, 264, start }) Write32(bytes, offset, Read32(original, offset) + (uint)extra.Length);
        Write32(bytes, start + 36, Read32(original, start + 36) + (uint)rows.Count);
        Write32(bytes, bytes.Length - 4, Crc(bytes.AsSpan(0, bytes.Length - 4)));
        return bytes;
    }

    private static int ReadVariable(byte[] bytes, string key) => (int)Read32(bytes, VariableOffset(bytes, key));
    private static int VariableOffset(byte[] bytes, string key)
    {
        byte[] text = Encoding.Unicode.GetBytes(key);
        int index = bytes.AsSpan().IndexOf(text);
        Check(index >= 0, "Independent variable key not found.");
        return index + text.Length + 1;
    }
    private static void ExactExistingChanges(EngageSave before, EngageSave after, params string[] keys)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        Check(original.Length == edited.Length, "Existing achievement update resized a save.");
        var offsets = keys.SelectMany(key => Enumerable.Range(VariableOffset(original, key), 4)).ToHashSet();
        for (int index = 0; index < original.Length - 4; index++)
            Check(original[index] == edited[index] || offsets.Contains(index), "Achievement edit altered unrelated bytes.");
        PreserveSections(before, after);
    }
    private static void PreserveSections(EngageSave before, EngageSave after)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        Check(original.AsSpan(0, 128).SequenceEqual(edited.AsSpan(0, 128)), "Unlock changed the save summary.");
        foreach (var section in before.Sections.Where(row => row.Name != "USER"))
        {
            var target = after.Sections.Single(row => row.Name == section.Name);
            Check(original.AsSpan(section.Offset, section.Length + 8).SequenceEqual(edited.AsSpan(target.Offset, target.Length + 8)),
                "Unlock altered an unrelated section.");
        }
        Check(before.ReadMainValues() == after.ReadMainValues(), "Unlock awarded fragments or changed settings.");
    }
    private static void WriteString(BinaryWriter writer, string text) { byte[] bytes = Encoding.Unicode.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid achievement input was accepted.");
    }
}
