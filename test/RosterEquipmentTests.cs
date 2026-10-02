using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterEquipmentTests
{
    private static readonly RosterEquipmentSelection None = new(RosterEquipmentKind.None, 0);
    private static readonly RosterEquipmentSelection Marth = new(RosterEquipmentKind.Emblem, 1);
    private static readonly RosterEquipmentSelection Tiki = new(RosterEquipmentKind.Emblem, 2);
    private static readonly RosterEquipmentSelection Ring = new(RosterEquipmentKind.BondRing, 10);
    private static uint Caeda => ItemCatalog.Hash(EmblemTests.Caeda);

    public static void Run(MainWindow window, string temporary)
    {
        var save = Fixture();
        byte[] original = save.Serialize();
        Check(save.ReadRosterEquipment(0) == Marth && save.ReadRosterEquipment(1) == None, "Equipment fixture did not parse.");
        Check(save.ReadRosterEquipmentOptions(0).Count == 5, "Owned Emblem or ring choices were misread.");
        Check(ReferenceEquals(save, save.WithRosterEquipment(0, Marth)), "Unchanged equipment rewrote the save.");
        var transferred = save.WithRosterEquipment(1, Marth);
        Check(transferred.ReadRosterEquipment(0) == None && transferred.ReadRosterEquipment(1) == Marth,
            "An Emblem was not transferred to its new owner.");
        Check(transferred.WithRosterEquipment(0, Marth).ReadRosterEquipment(1) == None, "Returning an Emblem left a duplicate owner.");
        Unrelated(save, transferred);
        var dlc = save.WithRosterEquipment(0, Tiki);
        Check(dlc.ReadRosterEquipment(0) == Tiki, "A saved DLC Emblem was unavailable.");
        var worn = save.WithRosterEquipment(0, Ring);
        var links = worn.ReadCharacterRingLinks();
        Check(links[0].EmblemInstance == 0 && links[0].RingInstance is not 0 and not 10, "A stack was not split for equipment.");
        var wornRing = worn.ReadBondRings().Single(ring => ring.OwnerIndex == 0);
        Check(wornRing.StockCount == 1 && worn.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 6,
            "Equipping a copy changed the wrong quantity.");
        var passed = worn.WithRosterEquipment(1, new(RosterEquipmentKind.BondRing, wornRing.InstanceId));
        Check(passed.ReadRosterEquipment(0) == None && passed.ReadBondRings().Single(ring => ring.OwnerIndex == 1).InstanceId == wornRing.InstanceId,
            "A worn ring transfer duplicated or consumed a copy.");
        var returned = passed.WithRosterEquipment(1, None);
        Check(returned.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 7 && returned.ReadBondRings().Count == 2,
            "Unequipping did not merge into the stock stack.");
        Check(returned.ReadCharacterRingLinks().All(link => link.EmblemInstance == 0 && link.RingInstance == 0), "Unequip left ownership links.");
        var lastCopy = Fixture(stock: 1).WithRosterEquipment(0, Ring);
        Check(lastCopy.ReadRosterEquipment(0).InstanceId == 10 && lastCopy.ReadBondRings().Count == 2,
            "The last copy created an unnecessary pool entry.");
        Check(lastCopy.WithRosterEquipment(0, None).ReadBondRings().Single(ring => ring.InstanceId == 10).OwnerIndex is null,
            "The only copy was lost on unequip.");
        var replaced = worn.WithRosterEquipment(0, new(RosterEquipmentKind.BondRing, 11));
        Check(replaced.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 7
            && replaced.ReadRosterEquipment(0).InstanceId == 11, "Ring replacement lost the former copy.");
        var sameStack = worn.WithRosterEquipment(0, Ring);
        Check(Totals(sameStack) == Totals(worn) && sameStack.ReadBondRings().Single(ring => ring.OwnerIndex == 0).StockCount == 1,
            "Re-equipping the same family duplicated its worn copy.");
        var engaged = Fixture(engaged: true);
        var disengaged = engaged.WithRosterEquipment(0, Tiki);
        Unrelated(engaged, disengaged);
        var unitSection = disengaged.Sections.Single(section => section.Name == "UNIT");
        ulong status = BinaryPrimitives.ReadUInt64LittleEndian(disengaged.Serialize().AsSpan(unitSection.PayloadOffset + 70));
        Check((status & 0x07800080UL) == 0, "Equipment retained stale Engage or Dual Guard state.");
        foreach (var changed in new[] { worn, passed, returned, lastCopy, replaced, dlc })
        {
            Unrelated(save, changed, allowRings: true);
            Check(changed.ReadBondRings().Where(ring => ring.RingHash == Caeda).Sum(ring => ring.StockCount)
                == (ReferenceEquals(changed, lastCopy) ? 1 : 7), "Equipment failed stock conservation.");
        }
        foreach (var invalid in new[] { new RosterEquipmentSelection((RosterEquipmentKind)99, 1), new(RosterEquipmentKind.None, 1),
            new(RosterEquipmentKind.Emblem, 0), new(RosterEquipmentKind.Emblem, 3), new(RosterEquipmentKind.Emblem, 999),
            new(RosterEquipmentKind.BondRing, 999) })
            Reject(() => save.WithRosterEquipment(0, invalid));
        Reject(() => save.WithRosterEquipment(-1, Ring));
        Reject(() => save.WithRosterEquipment(2, Ring));
        Reject(() => Fixture(stock: 0).WithRosterEquipment(0, Ring));
        Reject(() => Fixture(stock: 100).WithRosterEquipment(0, Ring));
        Reject(() => Fixture(force: UnitForce.Enemy).WithRosterEquipment(0, Ring));
        Reject(() => Fixture(force: UnitForce.Temporary).WithRosterEquipment(0, Ring));
        foreach (int flag in new[] { 0, 1, 2 })
        {
            var blocked = Fixture(godFlag: flag);
            Check(blocked.ReadRosterEquipmentOptions(0).All(option => option.Selection != Tiki), "An unavailable Emblem was selectable.");
            Reject(() => blocked.WithRosterEquipment(0, Tiki));
        }
        var linked = Fixture(partner: 3);
        Reject(() => linked.WithRosterEquipment(0, Ring));
        Check(linked.ReadCharacterRingLinks()[0].PartnerEmblemInstance == 3, "An Engage+ link was removed.");
        var missing = Fixture(missingGod: true);
        Reject(() => missing.ReadRosterEquipmentOptions(0));
        var duplicate = Fixture(secondEmblem: 1);
        Reject(() => duplicate.ReadRosterEquipmentOptions(0));
        foreach (var corrupt in new Action<byte[]>[]
        {
            payload => Write32(payload, 0, 9),
            payload => Write32(payload, 32, 101),
            payload => Write32(payload, 32, 4),
            payload => Write32(payload, 56, 1),
            payload => payload[50] = 2,
            payload => payload[54] = 255
        })
            Reject(() => Fixture(mutateGod: corrupt).ReadRosterEquipmentOptions(0));
        var aggregate = Fixture(extraRings: [new(12, Caeda, 99, null)]);
        Reject(() => aggregate.WithRosterEquipment(0, Ring));
        var full = Fixture(extraRings: Enumerable.Range(12, 748).Select(id => new BondRing((uint)(id > 750 ? id - 750 : id), (uint)id, 1, null)).ToArray());
        Reject(() => full.WithRosterEquipment(0, Ring));
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Rejected or successful immutable edits mutated the source.");

        string source = Path.Combine(temporary, "equipment-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditRosterEquipment, "The equipment GUI could not load.");
        window.ShowRoster();
        window.FindControl<TabStrip>("RosterTabs")!.SelectedIndex = 5;
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<StackPanel>("RosterEquipmentForm")!.IsVisible, "Equipment tab is missing.");
        var type = window.FindControl<ComboBox>("RosterEquipmentType")!;
        var choice = window.FindControl<ComboBox>("RosterEquipmentChoice")!;
        type.SelectedItem = type.Items.Cast<MainWindow.EquipmentKindChoice>().Single(item => item.Kind == RosterEquipmentKind.BondRing);
        choice.SelectedItem = choice.Items.Cast<MainWindow.EquipmentChoice>().Single(item => item.Option.Selection == Ring);
        for (int pass = 0; pass < 2; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(window.FindControl<Button>("UnequipRosterButton")!.Content?.ToString() == "卸下", "Equipment button did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check((choice.SelectedItem as MainWindow.EquipmentChoice)?.Option.Selection == Ring, "Language switching discarded pending equipment.");
            Check(choice.Items.Cast<MainWindow.EquipmentChoice>().All(item => !item.Label.Contains("希达")), "Chinese ring names remained.");
        }
        window.FindControl<NumericUpDown>("RosterSkillPoints")!.Text = "888";
        Check(window.ApplyRosterEquipment() && window.Save!.ReadRoster()[0].Values.SkillPoints == 888,
            "Equipment Apply discarded pending character edits.");
        Check(window.Save!.ReadRosterEquipment(0).Kind == RosterEquipmentKind.BondRing, "Equipment Apply did not equip.");
        Check(window.UnequipRosterEquipment() && window.Save!.ReadRosterEquipment(0) == None, "GUI unequip failed.");
        type.SelectedItem = type.Items.Cast<MainWindow.EquipmentKindChoice>().Single(item => item.Kind == RosterEquipmentKind.Emblem);
        choice.SelectedItem = choice.Items.Cast<MainWindow.EquipmentChoice>().Single(item => item.Option.Selection == Tiki);
        window.ShowItems();
        string copy = Path.Combine(temporary, "equipment-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadRosterEquipment(0) == Tiki,
            "File Save Copy ignored hidden pending equipment.");

        Check(window.LoadSave(source), "Could not reload equipment for pending stock tests.");
        window.ShowEmblems();
        var emblemTabs = window.FindControl<TabStrip>("EmblemTabs")!;
        emblemTabs.SelectedIndex = 1;
        var stock = window.FindControl<NumericUpDown>("BondRingStockInput")!;
        stock.Text = "9";
        window.ShowRoster();
        type.SelectedItem = type.Items.Cast<MainWindow.EquipmentKindChoice>().Single(item => item.Kind == RosterEquipmentKind.BondRing);
        choice.SelectedItem = choice.Items.Cast<MainWindow.EquipmentChoice>().Single(item => item.Option.Selection == Ring);
        Check(window.ApplyRosterEquipment()
            && window.Save!.ReadBondRings().Where(ring => ring.RingHash == Caeda).Sum(ring => ring.StockCount) == 9,
            "Equipment discarded or double-applied pending ring stock.");
        Check(window.FindControl<TextBlock>("BondRingOwnerValue")!.Text == "None" && stock.Text == "8",
            "The stock editor kept a stale value after splitting an equipped copy.");
        window.ShowEmblems();
        Check(window.ApplyEmblemValues() && window.UnequipRosterEquipment()
            && window.Save!.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 9,
            "The refreshed stock editor changed the conserved ring quantity.");
        Check(window.LoadSave(source), "Could not reload for atomic equipment tests.");
        window.ShowEmblems();
        stock.Text = "100";
        window.ShowRoster();
        type.SelectedItem = type.Items.Cast<MainWindow.EquipmentKindChoice>().Single(item => item.Kind == RosterEquipmentKind.BondRing);
        choice.SelectedItem = choice.Items.Cast<MainWindow.EquipmentChoice>().Single(item => item.Option.Selection == Ring);
        Check(!window.ApplyRosterEquipment() && window.Save!.Serialize().AsSpan().SequenceEqual(original),
            "Invalid pending stock partially committed equipment or character values.");
        stock.Text = "7";
        window.ShowEmblems();
        Check(window.ManageBondRings(meld: true), "Could not meld a ring while testing available equipment refresh.");
        window.ShowRoster();
        Check(choice.Items.Cast<MainWindow.EquipmentChoice>().Any(item => item.Option.RingHash == BondRingCatalog.Melding(Caeda)!.Result.Hash),
            "Returning from the ring editor did not refresh equipment choices.");
        emblemTabs.SelectedIndex = 0;
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI equipment overwrote the source.");
        Console.WriteLine("Roster equipment: Emblem/DLC transfers, ring splitting/merging, stock conservation, invalid ownership and GUI save/language passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        byte[] original = save.Serialize();
        var totals = Totals(save);
        foreach (var character in save.ReadRoster().Where(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary
            && RosterCatalog.Person(character.PersonHash) is not null))
        {
            var options = save.ReadRosterEquipmentOptions(character.Index);
            foreach (var option in options.Where(option => option.Selection.Kind == RosterEquipmentKind.Emblem))
            {
                var edited = save.WithRosterEquipment(character.Index, option.Selection);
                Check(edited.ReadRosterEquipment(character.Index) == option.Selection, "Real base/DLC Emblem assignment failed.");
                Unrelated(save, edited, allowRings: true);
            }
            var ring = options.First(option => option.Selection.Kind == RosterEquipmentKind.BondRing && option.StockCount > 1);
            var worn = save.WithRosterEquipment(character.Index, ring.Selection);
            var returned = worn.WithRosterEquipment(character.Index, None);
            Check(Totals(worn) == totals && Totals(returned) == totals, "Real ring equipment lost copies.");
            Check(returned.ReadRosterEquipment(character.Index) == None, "Real unequip failed.");
            Unrelated(save, worn, allowRings: true);
        }
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Real equipment tests modified the source.");
        Console.WriteLine("Real equipment: every playable character, all owned normal/DLC Emblems and ring stock conservation passed.");
    }

    private static string Totals(EngageSave save) => string.Join(";", save.ReadBondRings().GroupBy(ring => ring.RingHash)
        .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Sum(ring => ring.StockCount)}"));

    internal static EngageSave Fixture(int stock = 7, UnitForce force = UnitForce.Absent, int godFlag = -1,
        uint partner = 0, uint secondEmblem = 0, bool missingGod = false, BondRing[]? extraRings = null,
        bool engaged = false, Action<byte[]>? mutateGod = null)
    {
        byte[] roster = RosterTests.Fixture(force: force, validEquipment: true, mutate: payload =>
        {
            ReadOnlySpan<byte> marker = [0, 0, 4, 2, 1, 0, 0, 0, 20, 0, 0, 0];
            int position = 0, index = 0;
            while (position < payload.Length && payload.AsSpan(position).IndexOf(marker) is int found && found >= 0)
            {
                position += found;
                Write32(payload, position + 35, index == 0 ? 1u : secondEmblem);
                Write32(payload, position + 39, index == 0 ? partner : 0);
                Write32(payload, position + 43, 0);
                if (engaged && index == 0)
                {
                    payload[position] = 3;
                    payload[position + 1] = 4;
                    ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(70));
                    BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(70), flags | 0x07800080UL);
                }
                position += marker.Length;
                index++;
            }
            Check(index == 2, "The independent equipment fixture has incorrect unit trailers.");
        });
        var save = BondRingTests.Fixture([new(10, Caeda, stock, null), new(11, ItemCatalog.Hash("RNID_紋章_シーダ_S"), 1, null), .. extraRings ?? []], roster);
        using var pool = new MemoryStream();
        using (var writer = new BinaryWriter(pool, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(8u); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]); writer.Write(missingGod ? 2u : 3u);
            foreach (uint instance in missingGod ? new uint[] { 2, 3 } : new uint[] { 1, 2, 3 })
            {
                string gid = instance switch { 1 => EmblemTests.Marth, 2 => EmblemTests.Tiki, _ => EmblemCatalog.AlearEmblemId };
                writer.Write(instance); writer.Write((ushort)0xefcd); writer.Write(ItemCatalog.Hash(gid)); writer.Write(instance);
                for (int flag = 0; flag < 4; flag++) writer.Write((byte)(instance == 2 && flag == godFlag ? 1 : 0));
                writer.Write((byte)0); writer.Write((byte)0);
            }
        }
        byte[] gods = pool.ToArray();
        mutateGod?.Invoke(gods);
        return AppendSection(save, "GOD", gods);
    }

    private static EngageSave AppendSection(EngageSave save, string name, byte[] payload)
    {
        byte[] original = save.Serialize();
        byte[] bytes = new byte[original.Length + payload.Length + 8];
        original.AsSpan(0, original.Length - 8).CopyTo(bytes);
        int offset = original.Length - 8;
        Encoding.ASCII.GetBytes(new string(name.PadRight(4).Reverse().ToArray())).CopyTo(bytes, offset);
        Write32(bytes, offset + 4, (uint)payload.Length + 4); payload.CopyTo(bytes, offset + 8);
        Write32(bytes, 132 + save.Sections.Count * 4, (uint)offset);
        Write32(bytes, 136 + save.Sections.Count * 4, (uint)(bytes.Length - 8));
        "LVRC"u8.CopyTo(bytes.AsSpan(bytes.Length - 8));
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        Write32(bytes, bytes.Length - 4, ~crc);
        return EngageSave.Parse(bytes);
    }

    private static void Unrelated(EngageSave before, EngageSave after, bool allowRings = false)
    {
        byte[] source = before.Serialize(), target = after.Serialize();
        Check(source.AsSpan(0, 132).SequenceEqual(target.AsSpan(0, 132)), "Equipment changed the summary.");
        var beforeUnit = before.Sections.Single(section => section.Name == "UNIT");
        var afterUnit = after.Sections.Single(section => section.Name == "UNIT");
        byte[] originalUnit = source.AsSpan(beforeUnit.PayloadOffset, beforeUnit.Length).ToArray();
        byte[] editedUnit = target.AsSpan(afterUnit.PayloadOffset, afterUnit.Length).ToArray();
        Check(originalUnit.Length == editedUnit.Length, "Equipment resized the unit pool.");
        int position = 32;
        while (originalUnit[position] != 255)
        {
            int count = originalUnit[position + 1];
            position += 2;
            for (int index = 0; index < count; index++)
            {
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(originalUnit.AsSpan(position));
                ulong oldFlags = BinaryPrimitives.ReadUInt64LittleEndian(originalUnit.AsSpan(position + 36));
                ulong newFlags = BinaryPrimitives.ReadUInt64LittleEndian(editedUnit.AsSpan(position + 36));
                Check((oldFlags & ~0x07800080UL) == (newFlags & ~0x07800080UL), "Equipment changed unrelated status flags.");
                position += length;
            }
        }
        foreach (var section in before.Sections.Where(section => section.Name != "UNIT" && (!allowRings || section.Name != "RING")))
        {
            var result = after.Sections.Single(row => row.Name == section.Name);
            Check(source.AsSpan(section.PayloadOffset, section.Length).SequenceEqual(target.AsSpan(result.PayloadOffset, result.Length)),
                $"Equipment changed unrelated {section.Name} data.");
        }
    }
    private static void Reject(Func<object> action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid equipment input was accepted.");
    }
    private static void Write32(byte[] bytes, int position, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), value);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
