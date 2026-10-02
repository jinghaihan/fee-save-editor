using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class QuantityItemTests
{
    public static void Run()
    {
        var catalog = QuantityItemCatalog.Items;
        Check(catalog.Count == 105 && catalog.Count(row => row.Category == QuantityItemCategory.ReclassItems) == 4,
            "Base/DLC quantity items are incomplete.");
        Check(!ItemCatalog.Items.Any(item => catalog.Any(row => row.Id == item.Id)), "Quantity items are offered as convoy entries.");
        foreach (string id in new[] { "IID_スキルの書・守", "IID_スキルの書・破", "IID_スキルの書・離" })
            Check(ItemCatalog.Get(id).MaxUses == 1, "A DLC/Well skill book is missing from the convoy catalog.");
        var save = EngageSave.Parse(MainTests.Fixture());
        byte[] original = save.Serialize();
        foreach (var definition in catalog)
        {
            var edited = save.WithQuantityItem(definition.Id, definition.Maximum);
            Check(edited.ReadQuantityItems().Single(row => row.Definition.Id == definition.Id).Amount == definition.Maximum,
                $"{definition.Id}: maximum failed to round-trip.");
            Check(edited.WithQuantityItem(definition.Id, definition.Maximum).Serialize().AsSpan().SequenceEqual(edited.Serialize()), "Quantity update is not idempotent.");
            Check(edited.WithQuantityItem(definition.Id, 0).ReadQuantityItems().Single(row => row.Definition.Id == definition.Id).Amount == 0,
                "Zero quantity is rejected.");
            PreserveSections(save, edited);
            Reject(() => save.WithQuantityItem(definition.Id, -1));
            Reject(() => save.WithQuantityItem(definition.Id, definition.Maximum + 1));
        }
        Check(save.WithQuantityItem("IID_マスタープルフ", 0).Serialize().AsSpan().SequenceEqual(original), "Missing zero quantity inserted a variable.");
        Reject(() => save.WithQuantityItem("IID_ライブ", 1));
        Reject(() => save.WithQuantityItem("IID_unknown", 1));
        foreach (string malformed in new[] { "negative", "duplicate", "string" })
        {
            var invalid = EngageSave.Parse(Fixture(malformed));
            Reject(() => invalid.ReadQuantityItems());
            if (malformed != "negative")
            {
                Reject(() => invalid.WithQuantityItem("IID_マスタープルフ", 1));
                byte[] before = invalid.Serialize();
                Reject(() => invalid.FillQuantityItems(QuantityItemCategory.ReclassItems));
                Check(invalid.Serialize().AsSpan().SequenceEqual(before), "A failed bulk edit mutated the source.");
            }
        }
        var overflow = EngageSave.Parse(Fixture("overflow"));
        Check(overflow.ReadQuantityItems().Single(row => row.Definition.Id == "IID_マスタープルフ").Amount == 1000,
            "An existing cheat quantity was silently clamped.");
        Check(overflow.WithQuantityItem("IID_牛肉", 12).ReadQuantityItems().Single(row => row.Definition.Id == "IID_マスタープルフ").Amount == 1000,
            "Editing another quantity clamped an unrelated cheat value.");
        var existing = EngageSave.Parse(Fixture());
        var replaced = existing.WithQuantityItem("IID_マスタープルフ", 999);
        ExactChanges(existing, replaced, "G_所持_IID_マスタープルフ");
        Check(existing.Serialize().AsSpan().SequenceEqual(Fixture()), "Editing mutated the source object.");
        foreach (var category in Enum.GetValues<QuantityItemCategory>().Where(value => value != QuantityItemCategory.KeyItems))
        {
            var filled = existing.FillQuantityItems(category);
            var before = existing.ReadQuantityItems().ToDictionary(row => row.Definition.Id);
            Check(filled.ReadQuantityItems().All(row => row.Amount == (row.Definition.Category == category
                ? row.Definition.Maximum : before[row.Definition.Id].Amount)), "Bulk filling changed another category or missed an item.");
            Check(filled.FillQuantityItems(category).Serialize().AsSpan().SequenceEqual(filled.Serialize()), "Bulk fill is not idempotent.");
            PreserveSections(existing, filled);
        }
        Reject(() => existing.FillQuantityItems(QuantityItemCategory.KeyItems));
        Reject(() => existing.FillQuantityItems((QuantityItemCategory)99));
        Console.WriteLine("Quantity items: all 105 base/DLC definitions, nine languages, boundaries, insertion, preservation and malformed inputs passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var rows = save.ReadQuantityItems();
        foreach (var row in rows.Where(row => row.Definition.Category == QuantityItemCategory.ReclassItems))
            Check(row.Amount == 999, "The real reclass quantity does not match its saved 999 count.");
        var edited = save.WithQuantityItem("IID_エンチャント専用プルフ", 998);
        ExactChanges(save, edited, "G_所持_IID_エンチャント専用プルフ");
        Check(edited.ReadInventory().SequenceEqual(save.ReadInventory()), "A quantity edit changed the convoy.");
        Check(edited.ReadMainValues() == save.ReadMainValues(), "A reclass quantity edit changed Main resources.");
        Check(edited.WithQuantityItem("IID_エンチャント専用プルフ", 999).Serialize().AsSpan().SequenceEqual(save.Serialize()), "Real quantity reversal was not byte-identical.");
        Console.WriteLine($"Real quantity items: {rows.Count} definitions; four reclass items at 999; exact target-only edit/reversal passed.");
    }

    public static byte[] Fixture(string? malformed = null)
    {
        byte[] original = MainTests.Fixture();
        const int start = 317;
        int end = start + (int)Read32(original, start);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        string key = "G_所持_IID_マスタープルフ";
        int count = malformed == "duplicate" ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            WriteString(writer, key);
            writer.Write((byte)(malformed == "string" ? 1 : 0));
            if (malformed == "string") WriteString(writer, "preserve");
            else writer.Write(malformed switch { "negative" => -1, "overflow" => 1000, _ => 5 });
        }
        byte[] records = stream.ToArray();
        byte[] bytes = [.. original.AsSpan(0, end), .. records, .. original.AsSpan(end)];
        foreach (int offset in new[] { 136, 140, 264, start }) Write32(bytes, offset, Read32(original, offset) + (uint)records.Length);
        Write32(bytes, start + 36, Read32(original, start + 36) + (uint)count);
        Seal(bytes);
        return bytes;
    }

    private static void PreserveSections(EngageSave before, EngageSave after)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        foreach (var section in before.Sections.Where(row => row.Name != "USER"))
        {
            var actual = after.Sections.Single(row => row.Name == section.Name);
            Check(original.AsSpan(section.Offset, section.Length + 8).SequenceEqual(edited.AsSpan(actual.Offset, actual.Length + 8)), "An unrelated section changed.");
        }
        Check(original.AsSpan(0, 128).SequenceEqual(edited.AsSpan(0, 128)), "Quantity editing changed the save summary.");
    }

    private static void ExactChanges(EngageSave before, EngageSave after, string key)
    {
        byte[] original = before.Serialize(), edited = after.Serialize();
        byte[] text = Encoding.Unicode.GetBytes(key);
        int offset = original.AsSpan().IndexOf(text) + text.Length + 1;
        Check(offset > text.Length && original.Length == edited.Length, "Existing quantity update resized the save.");
        for (int index = 0; index < original.Length - 4; index++)
            Check(index >= offset && index < offset + 4 || original[index] == edited[index], "Quantity editing changed an unrelated byte.");
        EngageSave.Parse(edited);
    }

    private static void WriteString(BinaryWriter writer, string text) { byte[] value = Encoding.Unicode.GetBytes(text); writer.Write(value.Length); writer.Write(value); }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Seal(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        Write32(bytes, bytes.Length - 4, ~crc);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid quantity input was accepted.");
    }
}
