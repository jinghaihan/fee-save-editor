using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterTests
{
    private const string Recover = "IID_リカバー";
    private static readonly InventoryItem Staff = new(0x4e134981, 4, 0, 0xdeadbeef, null);
    private static readonly InventoryItem Weapon = new(0xe9d2a0e9, 255, 3, 0x55, 0x123abc);
    private static readonly InventoryItem Unknown = new(0x12345678, 17, 8, uint.MaxValue, 0xabcdef);

    public static void Run(MainWindow window, string temporary)
    {
        Check(RosterCatalog.Persons.Count == 41 && RosterCatalog.Classes.Count >= 60, "The roster catalog is incomplete.");
        foreach (var person in RosterCatalog.Persons)
            Check(person.Name("en") != person.Name("zh-Hans") && person.Name("unavailable") == person.Name("en"),
                "A character translation or English fallback is missing.");
        foreach (string id in new[] { "PID_エル", "PID_ラファール", "PID_セレスティア", "PID_グレゴリー", "PID_マデリーン" })
            Check(RosterCatalog.Person(ItemCatalog.Hash(id)) is not null, "A DLC character is missing.");
        byte[] original = Fixture();
        var save = EngageSave.Parse(original);
        var characters = save.ReadRoster();
        Check(characters[0].Progress is { CurrentHP: 20, InternalLevel: 4, OriginalProficiencies: 2,
            Proficiencies: 510, SelectedWeapons: 2, ClassSkill: null }
            && characters[0].Progress.EquippedSkills.Count == 0 && characters[0].Progress.InheritedSkills.Count == 0,
            "The character progression blocks did not decode.");
        Check(characters.Count == 2 && characters[0].Force == UnitForce.Absent && characters[0].Values == new RosterValue(5, 12, 400),
            "The independent roster fixture did not decode.");
        Check(characters[0].Items.Select(slot => slot.Item).SequenceEqual(new[] { Staff, Weapon, Unknown, null, null, null, null, null }),
            "Character items were decoded incorrectly.");
        Check(characters[0].Stats[(int)RosterStat.Strength] == new CharacterStat(RosterStat.Strength, 9, 42),
            "Class bases or personal stat modifiers were applied incorrectly.");
        var values = new RosterValue(20, 0, 9999);
        var edited = save.WithRosterValues(0, values);
        Check(edited.ReadRoster()[0].Values == values, "Character values did not round-trip.");
        Exact(save, edited.WithRosterValues(0, characters[0].Values), "Reversing values changed opaque data.");
        Check(ReferenceEquals(save, save.WithRosterValues(0, characters[0].Values)), "A no-op rewrote a character.");
        foreach (var invalid in new[] { new RosterValue(0, 0, 0), new(21, 0, 0), new(20, 1, 0),
            new(5, -1, 0), new(5, 100, 0), new(5, 0, -1), new(5, 0, 10000) })
            Reject(() => save.WithRosterValues(0, invalid), "An invalid level/EXP/SP was accepted.");
        Check(save.WithRosterValues(1, new RosterValue(40, 0, 0)).ReadRoster()[1].Values.Level == 40,
            "A special class did not allow level 40.");
        Reject(() => save.WithRosterValues(1, new RosterValue(41, 0, 0)), "A special class allowed level 41.");
        foreach (var stat in characters[0].Stats.Where(stat => stat.Stat != RosterStat.Sight))
        {
            int maximum = stat.Maximum!.Value;
            var changed = save.WithRosterStat(0, stat.Stat, maximum);
            Check(changed.ReadRoster()[0].Stats[(int)stat.Stat].Value == maximum, "A stat maximum was not applied.");
            Exact(save, changed.WithRosterStat(0, stat.Stat, stat.Value), "Stat reversal changed unrelated bytes.");
            Reject(() => save.WithRosterStat(0, stat.Stat, maximum + 1), "A stat cap was not enforced.");
            Reject(() => save.WithRosterStat(0, stat.Stat, -1), "A negative stat was accepted.");
        }
        Reject(() => save.WithRosterStat(0, RosterStat.Sight, 3), "Sight was editable without verification.");
        Reject(() => save.WithRosterStat(0, (RosterStat)99, 3), "An unknown stat was accepted.");
        Reject(() => save.WithRosterValues(2, values), "An invalid character index was accepted.");
        var overflow = EngageSave.Parse(Fixture(baseStrength: 100));
        Check(ReferenceEquals(overflow, overflow.WithRosterStat(0, RosterStat.Strength, 42)), "Inspection flattened an existing capped stat.");
        var injured = save.WithRosterStat(0, RosterStat.HP, 5);
        int unitStart = injured.Sections.Single(section => section.Name == "UNIT").PayloadOffset + 34;
        Check(injured.Serialize()[unitStart + 111] == 5, "Reducing maximum HP left current HP above the maximum.");
        var uses = save.WithRosterItem(0, 0, Recover, 7, 0);
        Check(uses.ReadRoster()[0].Items[0].Item == Staff with { Uses = 7 }, "Character uses edit lost flags.");
        Exact(save, uses.WithRosterItem(0, 0, Recover, 4, 0), "Uses reversal changed a character.");
        var forged = save.WithRosterItem(0, 1, "IID_フェンサリル", 255, 5);
        Check(forged.ReadRoster()[0].Items[1].Item == Weapon with { RefineLevel = 5 }, "Refinement lost an engraving.");
        Exact(save, forged.WithRosterItem(0, 1, "IID_フェンサリル", 255, 3), "Refinement reversal was not exact.");
        var added = save.WithRosterItem(0, 3, Recover, 10, 0);
        var next = added.ReadRoster()[1];
        Check(added.Length == save.Length + 14 && next.Values == characters[1].Values
            && next.Items.SequenceEqual(characters[1].Items) && next.Stats.SequenceEqual(characters[1].Stats),
            "Adding an item changed the next character.");
        Exact(save, added.DeleteRosterItem(0, 3), "Character add/delete was not reversible.");
        Unrelated(save, added);
        var deleted = save.DeleteRosterItem(0, 1);
        Check(deleted.Length == save.Length - 18 && deleted.ReadRoster()[0].Items[1].Item is null,
            "Deleting an engraved character item has the wrong size.");
        Unrelated(save, deleted);
        Check(save.RestoreRosterUses(0).ReadRoster()[0].Items.Select(slot => slot.Item)
            .SequenceEqual(new[] { Staff with { Uses = 10 }, Weapon, Unknown, null, null, null, null, null }),
            "Restoring uses modified an unknown or unlimited item.");
        var reserved = EngageSave.Parse(Fixture(mutate: bytes => Write32(bytes, 34 + 137, ItemCatalog.Hash("IID_エンゲージ枠"))));
        Reject(() => reserved.DeleteRosterItem(0, 0), "An Engage placeholder was deleted as a normal item.");
        Reject(() => reserved.WithRosterItem(0, 0, Recover, 1, 0), "An Engage placeholder was replaced as a normal item.");
        foreach ((int slot, string id, int remaining, int refinement) in new[]
        {
            (0, Recover, 0, 0), (0, Recover, 11, 0), (0, Recover, 1, 1), (1, Recover, 1, 0),
            (1, "IID_フェンサリル", 254, 0), (1, "IID_フェンサリル", 255, 6),
            (-1, Recover, 1, 0), (8, Recover, 1, 0), (0, "IID_missing", 1, 0)
        })
            Reject(() => save.WithRosterItem(0, slot, id, remaining, refinement), "An invalid character item was accepted.");
        foreach (Action<byte[]> mutate in new Action<byte[]>[]
        {
            bytes => Write32(bytes, 0, 1), bytes => bytes[32] = 7, bytes => bytes[33] = 0,
            bytes => Write32(bytes, 34, uint.MaxValue), bytes => Write32(bytes, 38, 39),
            bytes => bytes[34 + 44] = 0, bytes => Write32(bytes, 34 + 56, 1),
            bytes => bytes[34 + 120] = 2, bytes => Write32(bytes, 34 + 125, 1),
            bytes => bytes[34 + 129] = 7, bytes => Write32(bytes, 34 + 130, 4),
            bytes => bytes[34 + 134] = 2, bytes => bytes[^1] = 0
        })
            Reject(() => EngageSave.Parse(Fixture(mutate: mutate)).ReadRoster(), "Malformed roster data was accepted.");
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Core edits mutated the original roster.");
        CheckGui(window, temporary, original);
        Console.WriteLine("Roster: limits, opaque-data preservation, relocation, DLC names and live translations passed.");
    }

    private static void CheckGui(MainWindow window, string temporary, byte[] original)
    {
        string source = Path.Combine(temporary, "roster-fixture");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditRoster, "The Roster GUI did not load.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        var list = window.FindControl<ListBox>("RosterList")!;
        var search = window.FindControl<TextBox>("RosterSearch")!;
        var level = window.FindControl<NumericUpDown>("RosterLevel")!;
        var exp = window.FindControl<NumericUpDown>("RosterExperience")!;
        var sp = window.FindControl<NumericUpDown>("RosterSkillPoints")!;
        var tabs = window.FindControl<TabStrip>("RosterTabs")!;
        Check(list.ItemCount == 2 && level.Value == 5 && exp.Value == 12 && sp.Value == 400 && level.Maximum == 20,
            "Roster values were empty or limits were generic.");
        var form = window.FindControl<StackPanel>("RosterGeneralForm")!;
        Check(form.Width == 340 && form.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left,
            "Roster form alignment differs from the existing editor layout.");
        level.Text = "7";
        var uses = window.FindControl<NumericUpDown>("RosterItemUses")!;
        uses.Text = "6";
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans");
            Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.RosterRow)list.Items[0]!).Label.StartsWith("琉尔"), "Chinese character names did not update.");
            Check(window.FindControl<TextBox>("RosterClass")!.Text == "神龙之子", "Chinese class name did not update.");
            Check(((MainWindow.InventoryRow)window.FindControl<ListBox>("RosterItemsList")!.Items[0]!).Label.StartsWith(ItemCatalog.Get(Recover).Chinese),
                "Chinese equipment names did not update.");
            window.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.RosterRow)list.Items[0]!).Label.StartsWith("Alear"), "English character names did not recover.");
            Check(window.FindControl<TextBox>("RosterClass")!.Text == "Dragon Child", "English class name did not recover.");
            Check(level.Text == "7" && uses.Text == "6", "Language switching discarded pending roster edits.");
        }
        search.Text = "alear";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 1 && level.Text == "7", "Roster search discarded the selected editor.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        tabs.SelectedIndex = 1;
        Check(window.FindControl<StackPanel>("RosterStatsForm")!.IsVisible, "The Stats tab did not switch.");
        tabs.SelectedIndex = 2;
        Check(window.FindControl<StackPanel>("RosterItemsForm")!.IsVisible, "The Items tab did not switch.");
        string copy = Path.Combine(temporary, "roster-pending-copy");
        window.ShowItems();
        Check(window.SaveCopy(copy), "Save Copy rejected valid pending values from another roster tab/page: "
            + window.FindControl<TextBlock>("Message")!.Text);
        var copied = EngageSave.Load(copy).ReadRoster()[0];
        Check(copied.Values.Level == 7 && copied.Items[0].Item!.Uses == 6, "Save Copy lost pending edits from hidden roster tabs.");
        window.ShowRoster();
        byte[] before = window.Save!.Serialize();
        foreach (string invalid in new[] { "0", "21", "-1", "1.5", "2147483648", "abc", "" })
        {
            level.Text = invalid;
            Check(!window.ApplyRosterValues() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                "The GUI clamped or accepted an invalid character level.");
        }
        level.Text = "7";
        exp.Text = "100";
        Check(!window.ApplyRosterValues(), "EXP 100 was accepted.");
        exp.Text = "12";
        sp.Text = "10000";
        Check(!window.ApplyRosterValues(), "SP above the maximum was accepted.");
        sp.Text = "400";
        uses.Text = "11";
        Check(!window.ApplyRosterItem(), "Remaining uses above the item's maximum were accepted.");
        uses.Text = "6";
        list.SelectedIndex = 1;
        Check(level.Maximum == 40 && level.Value == 5, "Selecting a special class did not update the level limit.");
        list.SelectedIndex = 0;
        level.Text = "20";
        Check(exp.Text == "0", "Reaching maximum level did not clear EXP.");
        Check(window.ApplyRosterValues(), "The GUI could not apply maximum level.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Roster GUI editing overwrote the input save.");
        Check(window.LoadSave(source), "Could not reset the roster fixture.");
    }

    public static void CheckReal(EngageSave save)
    {
        var roster = save.ReadRoster();
        var playable = roster.Where(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary).ToArray();
        Check(playable.Length == 41 && playable.All(character => RosterCatalog.Person(character.PersonHash) is not null
            && RosterCatalog.Class(character.ClassHash) is not null), "A real character or DLC class did not resolve.");
        var first = roster[0];
        var values = first.Values with { SkillPoints = first.Values.SkillPoints == 9999 ? 9998 : 9999 };
        Exact(save, save.WithRosterValues(0, values).WithRosterValues(0, first.Values), "Real SP reversal changed opaque bytes.");
        foreach (var character in playable)
        {
            int? empty = character.Items.FirstOrDefault(slot => slot.Item is null)?.Slot;
            if (!empty.HasValue) continue;
            var added = save.WithRosterItem(character.Index, empty.Value, Recover, 10, 0);
            Unrelated(save, added);
            Exact(save, added.DeleteRosterItem(character.Index, empty.Value), "Real equipment add/delete was not reversible.");
        }
        Console.WriteLine("Real roster: 41 characters and classes recognized; SP and equipment edits restored byte-for-byte.");
    }

    public static byte[] Fixture(int baseStrength = 3, Action<byte[]>? mutate = null)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]);
            writer.Write((byte)3); writer.Write((byte)2);
            writer.Write(Character("PID_リュール", "JID_神竜ノ子", baseStrength, optionalTarget: false));
            writer.Write(Character("PID_ユナカ", "JID_シーフ", 3, optionalTarget: true));
            writer.Write((byte)255);
        }
        byte[] payload = output.ToArray();
        mutate?.Invoke(payload);
        byte[] original = MainTests.Fixture();
        var save = EngageSave.Parse(original);
        var sections = save.Sections.Select(section => original.AsSpan(section.Offset, section.Length + 8).ToArray()).ToList();
        byte[] unit = new byte[payload.Length + 8];
        "TINU"u8.CopyTo(unit);
        Write32(unit, 4, (uint)payload.Length + 4);
        payload.CopyTo(unit, 8);
        sections.Insert(1, unit);
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
        Write32(result, offset + 4, Crc(result.AsSpan(0, offset + 4)));
        return result;
    }

    private static byte[] Character(string person, string job, int baseStrength, bool optionalTarget)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0u); writer.Write(40u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]); writer.Write(0x80000000000UL);
        Reference(writer, ItemCatalog.Hash(person)); Reference(writer, ItemCatalog.Hash(job));
        writer.Write(0u);
        writer.Write(new byte[] { 2, (byte)baseStrength, 3, 4, 5, 6, 7, 8, 1, 0, 0 });
        writer.Write(0u); writer.Write(new byte[11]); writer.Write(0u); writer.Write(new byte[11]);
        writer.Write(0x12345678u); writer.Write(0x87654321u);
        writer.Write((byte)5); writer.Write((byte)12); writer.Write((byte)20);
        writer.Write(0xffffffffu); writer.Write(0f);
        writer.Write(optionalTarget);
        if (optionalTarget) Reference(writer, ItemCatalog.Hash("PID_ヴァンドレ"));
        writer.Write(0u); writer.Write(2u); writer.Write((byte)8);
        foreach (var item in new[] { Staff, Weapon, Unknown, null, null, null, null, null })
        {
            writer.Write(5u); writer.Write(item is not null);
            if (item is null) continue;
            Reference(writer, item.ItemHash);
            writer.Write((byte)item.Uses); writer.Write((byte)item.RefineLevel); writer.Write(item.Flags);
            Reference(writer, item.EngravingHash);
        }
        writer.Write(0u);
        for (int index = 0; index < 4; index++) Reference(writer, null);
        writer.Write(0);
        for (int index = 0; index < 3; index++) { writer.Write(1u); writer.Write(0); }
        writer.Write(false);
        writer.Write(2u); writer.Write(510u); writer.Write(2u);
        writer.Write(6u);
        for (int index = 0; index < 4; index++)
        {
            if (index == 3) writer.Write(1u);
            writer.Write(1u); writer.Write(11u); writer.Write(new byte[44]);
        }
        writer.Write((sbyte)4);
        writer.Write(Enumerable.Range(1, 48).Select(value => (byte)value).ToArray());
        writer.Write((byte)1); writer.Write((byte)2); writer.Write((short)400); writer.Write(0x87654321u);
        writer.Write((sbyte)-1); writer.Write((sbyte)-1);
        byte[] result = output.ToArray();
        Write32(result, 0, (uint)result.Length);
        return result;
    }

    private static void Reference(BinaryWriter writer, uint? hash)
    {
        writer.Write((ushort)(hash.HasValue ? 0xefcd : 0xccdb));
        if (hash.HasValue) writer.Write(hash.Value);
    }
    private static void Unrelated(EngageSave before, EngageSave after)
    {
        byte[] original = before.Serialize();
        byte[] edited = after.Serialize();
        Check(original.AsSpan(0, 132).SequenceEqual(edited.AsSpan(0, 132)), "Roster edit changed the summary.");
        foreach (var section in before.Sections.Where(section => section.Name != "UNIT"))
        {
            var target = after.Sections.Single(entry => entry.Name == section.Name);
            Check(original.AsSpan(section.Offset, section.Length + 8).SequenceEqual(edited.AsSpan(target.Offset, target.Length + 8)),
                "Roster resizing changed an unrelated section.");
        }
    }
    private static void Exact(EngageSave before, EngageSave after, string message) =>
        Check(before.Serialize().AsSpan().SequenceEqual(after.Serialize()), message);
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
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
