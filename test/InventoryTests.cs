using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class InventoryTests
{
    private const string Recover = "IID_リカバー";
    private const string Fensalir = "IID_フェンサリル";
    private static readonly InventoryItem Staff = new(0x4e134981, 4, 0, 0xdeadbeef, null);
    private static readonly InventoryItem Weapon = new(0xe9d2a0e9, 255, 3, 0x55, 0x123abc);
    private static readonly InventoryItem Unknown = new(0x12345678, 17, 8, uint.MaxValue, 0xabcdef);

    public static void Run(MainWindow window, string temporary)
    {
        Check(ItemCatalog.Hash(Recover) == 0x4e134981 && ItemCatalog.Hash(Fensalir) == 0xe9d2a0e9
            && ItemCatalog.Hash("IID_ブーツ") == 0x27ec834e && ItemCatalog.Hash("IID_特効薬") == 0x4c74ae9a,
            "Item IDs do not match independently observed game hashes.");
        Check(ItemCatalog.Get(Recover).MaxUses == 10 && ItemCatalog.Get(Recover).MaxRefine == 0
            && ItemCatalog.Get(Fensalir).UnlimitedUses && ItemCatalog.Get(Fensalir).MaxRefine == 5,
            "The catalog has incorrect item-specific limits.");
        byte[] original = Fixture();
        var save = EngageSave.Parse(original);
        Check(save.ReadInventory().Select(slot => slot.Item).SequenceEqual(new[] { Staff, Weapon, Unknown, null }),
            "The independent convoy fixture did not parse correctly.");
        Check(ReferenceEquals(save, save.WithInventoryItem(0, Recover, 4, 0)), "A no-op changed the convoy.");
        for (int uses = 1; uses <= 10; uses++)
        {
            var edited = save.WithInventoryItem(0, Recover, uses, 0);
            Check(edited.ReadInventory()[0].Item == Staff with { Uses = uses }, "Staff edit lost flags or changed fields.");
            Check(edited.WithInventoryItem(0, Recover, 4, 0).Serialize().AsSpan().SequenceEqual(original),
                "Reversing remaining uses did not restore every byte.");
        }
        for (int level = 0; level <= 5; level++)
        {
            var edited = save.WithInventoryItem(1, Fensalir, 255, level);
            Check(edited.ReadInventory()[1].Item == Weapon with { RefineLevel = level }, "Refining lost an engraving or flag.");
            Check(edited.WithInventoryItem(1, Fensalir, 255, 3).Serialize().AsSpan().SequenceEqual(original),
                "Reversing refinement changed unrelated data.");
        }
        foreach ((int slot, string id, int uses, int refine) in new[]
        {
            (0, Recover, 0, 0), (0, Recover, 11, 0), (0, Recover, 1, 1), (0, Recover, 1, -1),
            (1, Fensalir, 254, 0), (1, Fensalir, 256, 0), (1, Fensalir, 255, 6),
            (-1, Recover, 1, 0), (4, Recover, 1, 0), (0, "IID_missing", 1, 0), (1, Recover, 1, 0)
        })
            Reject(() => save.WithInventoryItem(slot, id, uses, refine), "An invalid item edit was accepted.");
        Reject(() => save.DeleteInventoryItem(-1), "A negative deletion slot was accepted.");
        Reject(() => save.RestoreInventoryUses(2), "Unknown maximum uses were guessed.");
        Reject(() => save.RestoreInventoryUses(3), "An empty slot was restored.");
        Reject(() => save.RestoreInventoryUses(4), "An out-of-capacity slot was restored.");
        Check(save.RestoreInventoryUses().ReadInventory().Select(slot => slot.Item)
            .SequenceEqual(new[] { Staff with { Uses = 10 }, Weapon, Unknown, null }), "Restore changed non-use fields.");
        var added = save.AddInventoryItem(Recover);
        Check(added.ReadInventory()[3].Item == new InventoryItem(Staff.ItemHash, 10, 0, 0, null), "Add did not use the first empty slot.");
        Check(added.DeleteInventoryItem(3).Serialize().AsSpan().SequenceEqual(original), "Add/delete was not exactly reversible.");
        Check(save.AddInventoryItem(Fensalir, 255, 5).ReadInventory()[3].Item
            == new InventoryItem(Weapon.ItemHash, 255, 5, 0, null), "Adding an item ignored its requested refinement.");
        Reject(() => save.AddInventoryItem(Recover, 11), "Add bypassed remaining-use validation.");
        Reject(() => added.AddInventoryItem(Recover), "A full convoy grew past its saved capacity.");
        var removed = save.DeleteInventoryItem(1);
        Check(removed.Length == save.Length - 18 && removed.ReadInventory()[1].Item is null, "Engraved deletion has the wrong size.");
        AssertUnrelatedSections(save, removed);
        AssertUnrelatedSections(save, added);
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Editing mutated the original save.");

        foreach (Action<byte[]> corrupt in new Action<byte[]>[]
        {
            bytes => Write32(bytes, 0, 2), bytes => Write16(bytes, 32, 0), bytes => Write16(bytes, 32, 1000),
            bytes => Write32(bytes, 34, 2), bytes => Write32(bytes, 38, 4), bytes => bytes[42] = 2,
            bytes => Write16(bytes, 43, 0xccdb), bytes => Write16(bytes, 43, 0), bytes => Write16(bytes, 55, 0)
        })
            Reject(() => EngageSave.Parse(Fixture(mutate: corrupt)).ReadInventory(), "Malformed convoy fields were accepted.");
        Reject(() => EngageSave.Parse(Container(Payload()[..^1])).ReadInventory(), "Truncated convoy data was accepted.");
        Reject(() => EngageSave.Parse(Container([.. Payload(), 0])).ReadInventory(), "Trailing convoy data was accepted.");
        Reject(() => EngageSave.Parse(Container(Payload(), duplicate: true)).ReadInventory(), "Duplicate TRAN sections were accepted.");
        var full = EngageSave.Parse(Fixture(Enumerable.Repeat<InventoryItem?>(Staff, 999).ToArray()));
        Check(full.ReadInventory().Count == 999 && full.RestoreInventoryUses().ReadInventory().All(slot => slot.Item!.Uses == 10),
            "The actual 999-slot capacity or batch restore failed.");
        Reject(() => full.AddInventoryItem(Recover), "A full 999-slot convoy accepted another item.");
        RunGui(window, temporary, original);
        Console.WriteLine("Convoy core, capacity, preservation, malformed-input and GUI tests passed.");
    }

    private static void RunGui(MainWindow window, string temporary, byte[] original)
    {
        string source = Path.Combine(temporary, "inventory-fixture");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditInventory, "Convoy controls did not enable.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<Grid>("ItemsPanel")!.IsVisible, "The native tab strip did not open Items.");
        var list = window.FindControl<ListBox>("InventoryList")!;
        var search = window.FindControl<TextBox>("ItemSearch")!;
        var uses = window.FindControl<NumericUpDown>("ItemUsesInput")!;
        var refine = window.FindControl<NumericUpDown>("ItemRefineInput")!;
        var form = window.FindControl<StackPanel>("ItemEditorInputs")!;
        Check(form.Width == 340 && form.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left,
            "The item form must match FETH's left-aligned editor layout.");
        Check(list.ItemCount == 3 && uses.Value == 4 && uses.Maximum == 10 && refine.Maximum == 0,
            "The convoy form is empty or has generic instead of item-specific limits.");
        Check(((MainWindow.InventoryRow)list.Items[2]!).Label.Contains("0x12345678"), "An unknown item was displayed blank.");
        uses.Text = "7";
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans");
            Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.InventoryRow)list.Items[0]!).Label.StartsWith(ItemCatalog.Get(Recover).Chinese),
                "Chinese item names did not update.");
            window.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.InventoryRow)list.Items[0]!).Label.StartsWith("Recover"), "English names did not recover.");
            Check(uses.Text == "7", "Language switching discarded pending item edits.");
        }
        search.Text = "recover";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 1 && uses.Text == "7", "Searching the selected item discarded edits.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        Check(window.ApplyItemValues() && window.Save!.ReadInventory()[0].Item!.Uses == 7, "The GUI did not apply an item edit.");
        byte[] beforeInvalid = window.Save!.Serialize();
        foreach (string invalid in new[] { "0", "11", "-1", "1.5", "2147483648", "abc", "" })
        {
            uses.Text = invalid;
            Check(!window.ApplyItemValues(), "The GUI silently clamped or accepted invalid uses.");
            Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeInvalid), "A rejected item edit changed the save.");
        }
        uses.Text = "7";
        refine.Text = "1";
        Check(!window.ApplyItemValues(), "The GUI allowed refinement of a staff.");
        refine.Text = "0";
        string pendingCopy = Path.Combine(temporary, "pending-item-copy");
        uses.Text = "6";
        Check(window.SaveCopy(pendingCopy) && EngageSave.Load(pendingCopy).ReadInventory()[0].Item!.Uses == 6,
            "Save Copy did not apply pending item values.");
        uses.Text = "7";
        Check(window.ApplyItemValues(), "Could not restore the pending-input fixture.");
        Check(window.AddSelectedItem(), "The GUI could not add an item.");
        Check(((MainWindow.InventoryRow)list.SelectedItem!).Slot == 3, "The newly added item was not selected.");
        Check(window.Save.ReadInventory()[3].Item!.Uses == 7, "Adding an item ignored the form values.");
        Check(window.DeleteSelectedItem(), "The GUI could not delete an item.");
        Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeInvalid), "GUI add/delete changed other entries.");
        list.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Check(!window.FindControl<Button>("ApplyItemButton")!.IsEnabled, "An unknown item enabled lossy editing.");
        string copy = Path.Combine(temporary, "unknown-item-copy");
        Check(window.SaveCopy(copy) && File.ReadAllBytes(copy).AsSpan().SequenceEqual(beforeInvalid),
            "Unknown item inspection prevented lossless save copy.");
        Check(window.RestoreAllItemUses() && window.Save.ReadInventory()[0].Item!.Uses == 10, "GUI batch restore failed.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI inventory editing modified the source file.");
        Check(window.LoadSave(source), "Could not reset the inventory GUI fixture.");
        byte[] unusual = Fixture([Staff with { Uses = 200 }, null]);
        string unusualSource = Path.Combine(temporary, "inventory-above-limit");
        File.WriteAllBytes(unusualSource, unusual);
        Check(window.LoadSave(unusualSource), "An unusual existing inventory could not be read.");
        window.ShowItems();
        Check(!window.ApplyItemValues() && window.Save!.Serialize().AsSpan().SequenceEqual(unusual),
            "An over-limit existing item was silently clamped into a valid edit.");
        string unusualCopy = Path.Combine(temporary, "inventory-above-limit-copy");
        Check(window.SaveCopy(unusualCopy) && File.ReadAllBytes(unusualCopy).AsSpan().SequenceEqual(unusual),
            "An unusual item could not be copied without changing it.");
        Check(window.LoadSave(source), "Could not reset the unusual inventory fixture.");
    }

    public static byte[] Fixture(InventoryItem?[]? items = null, Action<byte[]>? mutate = null)
    {
        byte[] payload = Payload(items);
        mutate?.Invoke(payload);
        return Container(payload);
    }

    private static byte[] Payload(InventoryItem?[]? items = null)
    {
        items ??= [Staff, Weapon, Unknown, null];
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(1u);
        writer.Write(Enumerable.Range(1, 28).Select(value => (byte)value).ToArray());
        writer.Write((ushort)items.Length);
        foreach (var item in items)
        {
            writer.Write(1u); writer.Write(5u); writer.Write(item is not null);
            if (item is null) continue;
            writer.Write((ushort)0xefcd); writer.Write(item.ItemHash);
            writer.Write((byte)item.Uses); writer.Write((byte)item.RefineLevel); writer.Write(item.Flags);
            writer.Write((ushort)(item.EngravingHash.HasValue ? 0xefcd : 0xccdb));
            if (item.EngravingHash.HasValue) writer.Write(item.EngravingHash.Value);
        }
        return output.ToArray();
    }

    private static byte[] Container(byte[] payload, bool duplicate = false)
    {
        byte[] original = MainTests.Fixture();
        var save = EngageSave.Parse(original);
        var sections = new List<byte[]>();
        var user = save.Sections[0];
        sections.Add(original.AsSpan(user.Offset, user.Length + 8).ToArray());
        byte[] transport = new byte[payload.Length + 8];
        "NART"u8.CopyTo(transport);
        Write32(transport, 4, (uint)(payload.Length + 4));
        payload.CopyTo(transport, 8);
        sections.Add(transport);
        if (duplicate) sections.Add(transport);
        var opaque = save.Sections[1];
        sections.Add(original.AsSpan(opaque.Offset, opaque.Length + 8).ToArray());
        byte[] result = new byte[260 + sections.Sum(section => section.Length) + 8];
        original.AsSpan(0, 132).CopyTo(result);
        int offset = 260;
        for (int index = 0; index < sections.Count; index++)
        {
            Write32(result, 132 + index * 4, (uint)offset);
            sections[index].CopyTo(result, offset);
            offset += sections[index].Length;
        }
        Write32(result, 132 + sections.Count * 4, (uint)offset);
        "LVRC"u8.CopyTo(result.AsSpan(offset));
        Write32(result, offset + 4, Checksum(result.AsSpan(0, offset + 4)));
        return result;
    }

    public static void CheckReal(EngageSave save)
    {
        byte[] original = save.Serialize();
        var slots = save.ReadInventory();
        Check(slots.Count == 999 && slots.Count(slot => slot.Item is not null) == 118, "Unexpected real convoy shape.");
        Check(slots.Where(slot => slot.Item is not null).All(slot => ItemCatalog.Find(slot.Item!.ItemHash) is not null),
            "A real convoy reference was not recognized by the catalog.");
        var staff = slots.First(slot => ItemCatalog.Find(slot.Item?.ItemHash ?? 0) is { UnlimitedUses: false, MaxUses: > 1 });
        var definition = ItemCatalog.Find(staff.Item!.ItemHash)!;
        var edited = save.WithInventoryItem(staff.Slot, definition.Id, 1, staff.Item.RefineLevel);
        Check(edited.WithInventoryItem(staff.Slot, definition.Id, staff.Item.Uses, staff.Item.RefineLevel)
            .Serialize().AsSpan().SequenceEqual(original), "Real uses edit did not restore exactly.");
        int empty = slots.First(slot => slot.Item is null).Slot;
        var added = save.AddInventoryItem(Recover);
        AssertUnrelatedSections(save, added);
        Check(added.DeleteInventoryItem(empty).Serialize().AsSpan().SequenceEqual(original), "Real add/delete was not reversible.");
        Console.WriteLine("Real convoy: 118/999 entries recognized; edits, relocation and exact restoration passed.");
    }

    private static void AssertUnrelatedSections(EngageSave before, EngageSave after)
    {
        byte[] original = before.Serialize();
        byte[] edited = after.Serialize();
        Check(original.AsSpan(0, 132).SequenceEqual(edited.AsSpan(0, 132)), "Convoy edits changed the game summary.");
        foreach (var section in before.Sections.Where(section => section.Name != "TRAN"))
        {
            var target = after.Sections.Single(entry => entry.Name == section.Name);
            Check(original.AsSpan(section.Offset, section.Length + 8)
                .SequenceEqual(edited.AsSpan(target.Offset, target.Length + 8)), "An unrelated section changed during convoy resizing.");
        }
    }

    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Write16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        return ~crc;
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
