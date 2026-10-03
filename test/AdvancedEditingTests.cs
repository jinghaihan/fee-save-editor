using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;
using FeeEditor.Gui.Localization;

internal static class AdvancedEditingTests
{
    public static void Run(MainWindow window, string temporary)
    {
        Variables(window, temporary);
        var save = SafeFixture();
        byte[] original = save.Serialize();
        var records = Records(save);
        Check(save.ReadMissingRosterCharacters().Count == 39, "Missing playable characters were not identified.");
        foreach (var person in save.ReadMissingRosterCharacters())
        {
            var added = save.WithAddedRosterCharacter(person.Id);
            var character = added.ReadRoster().Single(row => row.PersonHash == person.Hash);
            Check(character.Force == UnitForce.Absent && character.Values.Level == 1 && character.Values.Experience == 0
                && character.Items.All(slot => slot.Item is null) && character.Progress.CurrentHP == added.RosterMaximumHP(character.Index),
                $"{person.Id}: fresh character defaults are invalid.");
            Check(RosterCatalog.Class(character.ClassHash)?.Id == person.BirthClass, "The new character has the wrong starting class.");
            Check(added.ReadEmblems().Where(holder => holder.EmblemId == "GID_マルス").All(holder =>
                holder.Bonds.Any(bond => bond.PersonId == person.Id && bond.Level == 1 && bond.Experience == 0)),
                "A new character is missing initial bonds with owned Emblems.");
            foreach (var record in records)
                Check(Records(added).Single(row => row.Person == record.Person).Bytes.AsSpan().SequenceEqual(record.Bytes),
                    "Creating a character modified another unit.");
            if (person.Id == "PID_ヴァンドレ")
            {
                Check(!added.CanDeleteRosterCharacter(character.Index), "A saved target association did not protect Vander.");
                Reject(() => added.WithoutRosterCharacter(character.Index));
                continue;
            }
            Check(added.CanDeleteRosterCharacter(character.Index), $"{person.Id}: a new unlinked character cannot be deleted.");
            var deleted = added.WithoutRosterCharacter(character.Index);
            Check(Payload(save, "UNIT").AsSpan().SequenceEqual(Payload(deleted, "UNIT")), "Creation/deletion changed other units.");
            Check(deleted.ReadEmblems().Any(holder => holder.Bonds.Any(bond => bond.PersonId == person.Id)), "Deletion erased historical bonds.");
            Reject(() => added.WithAddedRosterCharacter(person.Id));
        }
        Reject(() => save.WithAddedRosterCharacter("PID_リュール"));
        Reject(() => save.WithAddedRosterCharacter("PID_unknown"));
        var complete = save;
        foreach (var person in save.ReadMissingRosterCharacters()) complete = complete.WithAddedRosterCharacter(person.Id);
        Check(complete.ReadRoster().Count == 41 && complete.ReadMissingRosterCharacters().Count == 0,
            "Sequential character creation lost records or left missing characters.");
        var framed = save.WithAddedRosterCharacter("PID_フラン");
        int framme = framed.ReadRoster().Single(row => row.PersonHash == ItemCatalog.Hash("PID_フラン")).Index;
        var ringChoice = framed.ReadRosterEquipmentOptions(framme).First(option => option.Selection.Kind == RosterEquipmentKind.BondRing && option.StockCount > 0);
        var equipped = framed.WithRosterEquipment(framme, ringChoice.Selection);
        var unequipped = equipped.WithoutRosterCharacter(framme);
        Check(Payload(framed, "RING").AsSpan().SequenceEqual(Payload(unequipped, "RING")), "Deleting an equipped character lost a Bond Ring.");
        var emblemChoice = framed.ReadRosterEquipmentOptions(framme).First(option => option.Selection.Kind == RosterEquipmentKind.Emblem);
        var removedOwner = framed.WithRosterEquipment(framme, emblemChoice.Selection).WithoutRosterCharacter(framme);
        Check(removedOwner.ReadCharacterRingLinks().All(row => row.EmblemInstance != emblemChoice.Selection.InstanceId),
            "Deleting a character left its Emblem linked to another unit.");
        var carrying = framed.WithRosterItem(framme, 0, "IID_鉄の剣", 255, 0);
        Check(!carrying.CanDeleteRosterCharacter(framme), "A character with ordinary carried items can be deleted.");
        foreach (Difficulty difficulty in Enum.GetValues<Difficulty>())
        {
            var variant = save.WithMainValues(save.ReadMainValues() with { Difficulty = difficulty });
            foreach (var person in variant.ReadMissingRosterCharacters())
            {
                var added = variant.WithAddedRosterCharacter(person.Id);
                var character = added.ReadRoster().Single(row => row.PersonHash == person.Hash);
                Check(character.Progress.CurrentHP == added.RosterMaximumHP(character.Index), "Difficulty-specific creation offsets produced invalid HP.");
            }
        }
        using var fullPool = new MemoryStream();
        fullPool.Write(Payload(save, "UNIT").AsSpan(0, 32));
        fullPool.WriteByte(3); fullPool.WriteByte(249);
        foreach (var record in records) fullPool.Write(record.Bytes);
        for (uint index = 0; index < 247; index++)
        {
            byte[] extra = records[1].Bytes.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(46), 0xdead0000u + index);
            fullPool.Write(extra);
        }
        fullPool.WriteByte(255);
        var full = ReplacePool(save, "UNIT", fullPool.ToArray()).WithAddedRosterCharacter("PID_フラン");
        Check(full.ReadRoster().Count == 250 && full.ReadMissingRosterCharacters().Count == 0,
            "Roster creation exceeded the native 250-unit pool limit.");
        Reject(() => full.WithAddedRosterCharacter("PID_クラン"));
        Check(!save.CanDeleteRosterCharacter(0) && !save.CanDeleteRosterCharacter(1), "The protagonist or a character carrying items can be deleted.");
        Reject(() => save.WithoutRosterCharacter(0));
        Reject(() => save.WithoutRosterCharacter(1));
        foreach (var force in new[] { UnitForce.Dead, UnitForce.Lost, UnitForce.Absent })
        {
            var moved = save.WithRosterForce(1, force);
            Check(moved.ReadRoster().Single(row => row.PersonHash == records[1].Person).Force == force, "Moving chose the wrong force.");
            foreach (var record in records)
                Check(Records(moved).Single(row => row.Person == record.Person).Bytes.AsSpan().SequenceEqual(record.Bytes),
                    "Moving changed a unit's raw record.");
            Unrelated(save, moved, "UNIT");
        }
        foreach (var force in new[] { UnitForce.Player, UnitForce.Enemy, UnitForce.Ally, UnitForce.Temporary })
            Reject(() => save.WithRosterForce(1, force));
        foreach (byte sequence in new byte[] { 0, 2, 3, 5, 7, 8 })
        {
            var battle = SafeFixture(sequence);
            Reject(() => battle.WithAddedRosterCharacter("PID_フラン"));
            Reject(() => battle.WithRosterForce(1, UnitForce.Lost));
            Reject(() => battle.WithoutEmblem(1));
        }
        foreach (ulong flag in new ulong[] { 8, 0x200000, 0x400000, 0x8000000000, 0x200000000000 })
            Check(!RosterRecoveryTests.Fixture(UnitForce.Absent, flags: flag).CanMoveRosterCharacter(1), "A protected character can be moved.");
        Repair(window, temporary);
        EmblemRemoval(window, temporary);
        RosterGui(window, temporary, save);
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Advanced editing mutated its input.");
        Console.WriteLine("Advanced editing: numeric boundaries, 39 base/DLC creations, inactive movement, guarded deletion, class repair, Emblem removal and GUI saves passed.");
    }

    private static void Variables(MainWindow window, string temporary)
    {
        var save = EngageSave.Parse(MainTests.Fixture());
        const string key = "G_所持_IID_てつの晶石_extra";
        Check(save.ReadGameVariables().Count == 4, "String variables appeared in numeric editing.");
        foreach (int value in new[] { int.MinValue, -1, 0, int.MaxValue })
        {
            var edited = save.WithGameVariable(key, value);
            Check(edited.ReadGameVariables().Single(row => row.Key == key).Value == value, "Signed numeric variables did not round-trip.");
            Check(edited.WithGameVariable(key, 333).Serialize().AsSpan().SequenceEqual(save.Serialize()), "Variable editing changed other data.");
        }
        foreach (string invalid in new[] { "", "missing", "unrelated string variable" }) Reject(() => save.WithGameVariable(invalid, 0));
        Reject(() => EngageSave.Parse(MainTests.Fixture(duplicateMaterial: true)).ReadGameVariables());
        string source = Path.Combine(temporary, "variables-source");
        File.WriteAllBytes(source, save.Serialize());
        Check(window.LoadSave(source) && window.CanEditVariables, "The variables GUI could not load.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 7;
        window.FindControl<TabStrip>("InspectorPages")!.SelectedIndex = 1;
        window.FindControl<ListBox>("VariableList")!.SelectedItem = key;
        window.FindControl<NumericUpDown>("VariableValueInput")!.Text = "-2147483648";
        Check(window.ApplyVariableValue() && window.Save!.ReadGameVariables().Single(row => row.Key == key).Value == int.MinValue,
            "The GUI rejected the signed minimum.");
        window.FindControl<NumericUpDown>("VariableValueInput")!.Text = "not-a-number";
        byte[] before = window.Save!.Serialize();
        Check(!window.ApplyVariableValue() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid variable text partially changed the save.");
        window.FindControl<NumericUpDown>("VariableValueInput")!.Text = "123";
        Check(window.SaveCopy(Path.Combine(temporary, "variables-copy")), "Saving omitted pending variable edits.");
        Check(EngageSave.Load(Path.Combine(temporary, "variables-copy")).ReadGameVariables().Single(row => row.Key == key).Value == 123,
            "The saved variable did not match its input.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(save.Serialize()), "Variable editing overwrote its source.");
        window.FindControl<ListBox>("VariableList")!.SelectedItem = "G_所持_IID_てつの晶石";
        window.FindControl<NumericUpDown>("VariableValueInput")!.Text = "7";
        Check(window.ApplyVariableValue() && window.FindControl<NumericUpDown>("IronIngotsInput")!.Value == 7,
            "Raw edits left the dedicated resource editor stale.");
        window.FindControl<NumericUpDown>("IronIngotsInput")!.Text = "9";
        Check(window.ApplyMainValues(), "The resource editor did not apply.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 0;
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 7;
        Check(window.FindControl<NumericUpDown>("VariableValueInput")!.Value == 9, "Returning to Variables displayed stale resource values.");
        File.WriteAllBytes(source, SafeFixture().Serialize());
        Check(window.LoadSave(source), "The transactional variable fixture did not load.");
        window.FindControl<ListBox>("VariableList")!.SelectedIndex = 0;
        window.FindControl<NumericUpDown>("VariableValueInput")!.Text = "5";
        window.FindControl<NumericUpDown>("MoneyInput")!.Text = "123";
        window.FindControl<NumericUpDown>("RosterSkillPoints")!.Text = "invalid";
        before = window.Save!.Serialize();
        Check(!window.ApplyVariableValue() && window.Save.Serialize().AsSpan().SequenceEqual(before),
            "A failed variable transaction partially applied another panel's edits.");
    }

    private static void Repair(MainWindow window, string temporary)
    {
        var save = RosterRecoveryTests.Fixture(UnitForce.Absent, flags: 0, unknownClass: true);
        Check(save.CanRepairRosterClass(1) && !save.CanRepairRosterClass(0), "Class repair eligibility is wrong.");
        var repaired = save.RepairRosterClass(1);
        Check(RosterCatalog.Class(repaired.ReadRoster()[1].ClassHash)?.Id == RosterCatalog.Person(save.ReadRoster()[1].PersonHash)?.BirthClass,
            "Class repair did not restore the starting class.");
        Check(ReferenceEquals(repaired, repaired.RepairAllRosterClasses()), "Valid classes were changed during repair.");
        Check(Records(save)[0].Bytes.AsSpan().SequenceEqual(Records(repaired)[0].Bytes), "Repair modified another character.");
        Unrelated(save, repaired, "UNIT");
        byte[] excessive = Payload(save, "UNIT");
        int second = Records(save)[1].Offset - save.Sections.Single(section => section.Name == "UNIT").PayloadOffset;
        excessive[second + 109] = excessive[second + 110] = excessive[second + 111] = 255;
        var limited = ReplacePool(save, "UNIT", excessive).RepairRosterClass(1);
        Check(limited.ReadRoster()[1].Values.Level == RosterCatalog.Class(limited.ReadRoster()[1].ClassHash)!.MaxLevel
            && limited.ReadRoster()[1].Values.Experience == 0 && limited.ReadRoster()[1].Progress.CurrentHP == limited.RosterMaximumHP(1),
            "Class repair did not clamp level, EXP and HP to the repaired class.");
        var record = Records(save)[1];
        byte[] nullClass = record.Bytes[..50].Concat(new byte[] { 0xdb, 0xcc }).Concat(record.Bytes[56..]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(nullClass, (uint)nullClass.Length);
        var unit = Payload(save, "UNIT");
        int relative = record.Offset - save.Sections.Single(section => section.Name == "UNIT").PayloadOffset;
        var missing = ReplacePool(save, "UNIT", unit[..relative].Concat(nullClass).Concat(unit[(relative + record.Bytes.Length)..]).ToArray());
        Check(missing.ReadRoster()[1].ClassHash == 0 && missing.RepairRosterClass(1).ReadRoster()[1].ClassHash == repaired.ReadRoster()[1].ClassHash,
            "A two-byte null class was not repaired correctly.");
        string source = Path.Combine(temporary, "class-repair-source");
        File.WriteAllBytes(source, missing.Serialize());
        Check(window.LoadSave(source) && window.Save!.ReadRoster()[1].ClassHash == repaired.ReadRoster()[1].ClassHash,
            "The GUI did not automatically repair a missing class.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(missing.Serialize()), "Automatic repair overwrote the original save.");
        Check(!RosterRecoveryTests.Fixture(UnitForce.Absent, sequence: 3, unknownClass: true).CanRepairRosterClass(1), "Repair was allowed during battle.");
    }

    private static void EmblemRemoval(MainWindow window, string temporary)
    {
        var save = SafeFixture();
        Check(save.CanRemoveEmblem(1), "An owned normal Emblem cannot be removed.");
        var removed = save.WithoutEmblem(1);
        Check(removed.ReadCharacterRingLinks().All(row => row.EmblemInstance != 1), "Removed equipment remained linked.");
        Check(removed.ReadMissingEmblems().Any(row => row.Id == "GID_マルス"), "The removed Emblem was not missing.");
        Check(Payload(save, "GDBD").AsSpan().SequenceEqual(Payload(removed, "GDBD")), "Removal reset historical bonds.");
        Check(removed.WithAddedEmblem("GID_マルス").ReadRosterEquipmentOptions(1).Any(row => row.EmblemId == "GID_マルス"), "A removed Emblem cannot be added again.");
        Unrelated(save, removed, "GOD", "UNIT");
        Reject(() => save.WithoutEmblem(999));
        foreach (int flag in new[] { 0, 1, 2 }) Check(!RosterEquipmentTests.Fixture(godFlag: flag).CanRemoveEmblem(1), "A reserved Emblem can be removed.");
        string source = Path.Combine(temporary, "emblem-remove-source");
        File.WriteAllBytes(source, save.Serialize());
        Check(window.LoadSave(source), "The removal GUI did not load.");
        window.ShowEmblems();
        window.FindControl<NumericUpDown>("EmblemBondExpInput")!.Text = "invalid";
        byte[] before = window.Save!.Serialize();
        Check(!window.RemoveSelectedEmblem() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid pending values partially removed an Emblem.");
        window.FindControl<NumericUpDown>("EmblemBondExpInput")!.Text = "0";
        Check(window.FindControl<Button>("RemoveEmblemButton")!.IsEnabled && window.RemoveSelectedEmblem(), "The GUI could not remove the selected Emblem.");
        Check(window.Save!.ReadCharacterRingLinks().All(row => row.EmblemInstance != 1), "The GUI left an equipped Emblem after removal.");
        Check(window.SaveCopy(Path.Combine(temporary, "emblem-remove-copy")), "Removed Emblems could not be saved.");
    }

    private static void RosterGui(MainWindow window, string temporary, EngageSave save)
    {
        string source = Path.Combine(temporary, "roster-manage-source");
        File.WriteAllBytes(source, save.Serialize());
        Check(window.LoadSave(source), "The roster-management GUI did not load.");
        window.ShowRoster();
        foreach (var language in LanguageCatalog.Languages)
        {
            window.SetLanguage(language.Code);
            Dispatcher.UIThread.RunJobs();
            Check(window.FindControl<ComboBox>("MissingRosterCharacterInput")!.ItemCount == 39, "Language switching lost missing characters.");
            Check(Equals(window.FindControl<Button>("DeleteRosterCharacterButton")!.Content, UiLanguage.Get("Delete")), "Delete did not translate.");
        }
        window.SetLanguage("en");
        Check(window.AddRosterCharacter("PID_フラン"), "The GUI could not add Framme.");
        Check(window.MoveSelectedRosterCharacter(UnitForce.Lost), "The GUI could not move a new character.");
        Check(window.Save!.ReadRoster().Single(row => row.PersonHash == ItemCatalog.Hash("PID_フラン")).Force == UnitForce.Lost,
            "Moving lost the newly selected character.");
        Check(window.DeleteSelectedRosterCharacter(), "The GUI could not delete a new unlinked character.");
        Check(window.Save!.ReadRoster().Count == 2 && window.SaveCopy(Path.Combine(temporary, "roster-manage-copy")), "Roster changes could not be saved.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(save.Serialize()), "Roster management overwrote its input file.");
    }

    internal static EngageSave SafeFixture(byte sequence = 4)
    {
        var save = RosterRecoveryTests.Fixture(UnitForce.Absent, flags: 0, sequence: sequence);
        byte[] unit = Payload(save, "UNIT");
        int start = save.Sections.Single(section => section.Name == "UNIT").PayloadOffset;
        foreach (var record in Records(save)) BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(record.Offset - start + record.Bytes.Length - 6), 0);
        return ReplacePool(save, "UNIT", unit);
    }

    internal sealed record UnitRecord(uint Person, int Offset, byte[] Bytes);
    internal static List<UnitRecord> Records(EngageSave save)
    {
        byte[] bytes = save.Serialize();
        int offset = save.Sections.Single(section => section.Name == "UNIT").PayloadOffset + 32;
        var result = new List<UnitRecord>();
        while (bytes[offset++] != 255)
        {
            int count = bytes[offset++];
            for (int index = 0; index < count; index++)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
                result.Add(new(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 46)), offset, bytes.AsSpan(offset, length).ToArray()));
                offset += length;
            }
        }
        return result;
    }

    internal static byte[] Payload(EngageSave save, string name)
    {
        var section = save.Sections.Single(row => row.Name == name);
        return save.Serialize().AsSpan(section.PayloadOffset, section.Length).ToArray();
    }

    internal static EngageSave ReplacePool(EngageSave save, string name, byte[] payload)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        output.Write(save.Serialize().AsSpan(0, 260));
        var offsets = new List<uint>();
        foreach (var section in save.Sections)
        {
            offsets.Add((uint)output.Position);
            writer.Write(save.Serialize().AsSpan(section.Offset, 4));
            byte[] data = section.Name == name ? payload : Payload(save, section.Name);
            writer.Write((uint)data.Length + 4); writer.Write(data);
        }
        offsets.Add((uint)output.Position);
        writer.Write("LVRC"u8); writer.Write(0u);
        byte[] bytes = output.ToArray();
        bytes.AsSpan(132, 128).Clear();
        for (int index = 0; index < offsets.Count; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(132 + index * 4), offsets[index]);
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), ~crc);
        return EngageSave.Parse(bytes);
    }

    private static void Unrelated(EngageSave before, EngageSave after, params string[] edited)
    {
        foreach (var section in before.Sections.Where(row => !edited.Contains(row.Name)))
            Check(Payload(before, section.Name).AsSpan().SequenceEqual(Payload(after, section.Name)), $"Unrelated {section.Name} changed.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Unsafe advanced editing was accepted.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
