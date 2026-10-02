using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterRecoveryTests
{
    private const ulong DeadMask = 0x10000a00UL;

    public static void Run(MainWindow window, string temporary)
    {
        foreach (var force in new[] { UnitForce.Dead, UnitForce.Lost })
        {
            var save = Fixture(force);
            byte[] original = save.Serialize();
            Check(save.CanRestoreRosterCharacter(1), "A dead/lost playable character was not restorable.");
            var before = Records(save)[1];
            var edited = save.RestoreRosterCharacter(1);
            var roster = edited.ReadRoster();
            Check(roster[0].PersonHash == save.ReadRoster()[1].PersonHash && roster[0].Force == UnitForce.Absent
                && roster[0].Availability == RosterAvailability.Available, "Recovery did not move the selected character to the available pool.");
            byte[] expected = before.Bytes.ToArray();
            BinaryPrimitives.WriteUInt64LittleEndian(expected.AsSpan(36), save.ReadRoster()[1].StatusFlags & ~DeadMask);
            expected[111] = (byte)save.RosterMaximumHP(1);
            Check(Records(edited)[0].Bytes.AsSpan().SequenceEqual(expected), "Recovery changed unrelated character fields.");
            Check(Records(edited)[1].Bytes.AsSpan().SequenceEqual(Records(save)[0].Bytes), "Recovery changed another character.");
            Unrelated(save, edited);
            Check(save.Serialize().AsSpan().SequenceEqual(original), "Recovery mutated its input.");
            Check(!edited.CanRestoreRosterCharacter(0), "An available character still offered recovery.");
            Reject(() => edited.RestoreRosterCharacter(0));
            Check(edited.ReadRosterEquipment(1).InstanceId == 1 && edited.ReadRosterEquipment(0).InstanceId == 0,
                "Recovery moved the Emblem to a different person.");
            var twice = edited.RestoreRosterCharacter(1);
            Check(twice.ReadRoster().All(character => character.Force == UnitForce.Absent)
                && Records(twice).Count == 2, "Recovery left an empty force group or lost a unit.");
            Unrelated(save, twice);
        }
        var absentDead = Fixture(UnitForce.Absent);
        Check(absentDead.RestoreRosterCharacter(0).ReadRoster()[0].PersonHash == absentDead.ReadRoster()[0].PersonHash,
            "Clearing a bench death flag reordered the roster.");
        Check(absentDead.RestoreRosterCharacter(1).ReadRoster().All(character => character.Force == UnitForce.Absent),
            "A dead flag in the bench pool could not be cleared.");
        Reject(() => Fixture(UnitForce.Dead, flags: DeadMask | 8).RestoreRosterCharacter(1));
        foreach (ulong protectedFlag in new[] { 0x200000UL, 0x400000UL, 0x8000000000UL, 0x200000000000UL })
            Reject(() => Fixture(UnitForce.Dead, flags: DeadMask | protectedFlag).RestoreRosterCharacter(1));
        foreach (byte sequence in new byte[] { 0, 2, 3, 5, 7, 8 })
            Reject(() => Fixture(UnitForce.Dead, sequence: sequence).RestoreRosterCharacter(1));
        foreach (byte sequence in new byte[] { 1, 4, 6 })
            Check(Fixture(UnitForce.Dead, sequence: sequence).CanRestoreRosterCharacter(1), "A safe non-battle sequence was rejected.");
        Reject(() => Fixture(UnitForce.Dead, userFlags: 3).RestoreRosterCharacter(1));
        var available = EngageSave.Parse(RosterTests.Fixture());
        Check(available.ReadRoster().All(character => character.Availability == RosterAvailability.Available),
            "Normal bench characters were labeled unavailable.");
        Reject(() => available.RestoreRosterCharacter(0));
        Reject(() => Fixture(UnitForce.Enemy).RestoreRosterCharacter(1));
        Reject(() => Fixture(UnitForce.Temporary).RestoreRosterCharacter(1));
        Reject(() => Fixture(UnitForce.Dead, unknown: true).RestoreRosterCharacter(1));
        Reject(() => Fixture(UnitForce.Dead, unknownClass: true).RestoreRosterCharacter(1));
        Reject(() => Fixture(UnitForce.Dead, duplicate: true).RestoreRosterCharacter(1));
        Reject(() => Fixture(UnitForce.Dead).RestoreRosterCharacter(-1));
        Reject(() => Fixture(UnitForce.Dead).RestoreRosterCharacter(2));

        string source = Path.Combine(temporary, "roster-recovery-source");
        byte[] bytes = Fixture(UnitForce.Dead).Serialize();
        File.WriteAllBytes(source, bytes);
        Check(window.LoadSave(source), "The recovery GUI could not load.");
        window.ShowRoster();
        var list = window.FindControl<ListBox>("RosterList")!;
        list.SelectedItem = list.Items.Cast<MainWindow.RosterRow>().Single(row => row.Index == 1);
        Dispatcher.UIThread.RunJobs();
        var button = window.FindControl<Button>("RestoreRosterCharacterButton")!;
        var status = window.FindControl<TextBox>("RosterStatusInput")!;
        Check(button.IsEnabled && status.Text == "Dead", "The recovery controls did not reflect the selected character.");
        window.FindControl<NumericUpDown>("RosterItemUses")!.Value = 9;
        window.ShowEmblems();
        window.FindControl<Avalonia.Controls.Primitives.TabStrip>("EmblemTabs")!.SelectedIndex = 1;
        window.FindControl<NumericUpDown>("BondRingStockInput")!.Value = 6;
        window.ShowRoster();
        window.FindControl<NumericUpDown>("RosterSkillPoints")!.Value = 789;
        window.SetLanguage("zh-Hans");
        Dispatcher.UIThread.RunJobs();
        Check(status.Text == "死亡" && Equals(button.Content, "恢复角色"), "Recovery controls did not translate.");
        window.SetLanguage("en");
        Check(window.RestoreSelectedRosterCharacter(), "The GUI could not restore a dead character.");
        Dispatcher.UIThread.RunJobs();
        Check(((MainWindow.RosterRow)list.SelectedItem!).Index == 0 && window.Save!.ReadRoster()[0].Values.SkillPoints == 789
            && status.Text == "Available" && !button.IsEnabled, "Recovery lost selection, pending values or the availability state.");
        Check(window.Save!.ReadRoster()[0].Items[0].Item!.Uses == 9
            && window.Save.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 6,
            "Recovery discarded pending carried-item or ring-stock edits.");
        string copy = Path.Combine(temporary, "roster-recovery-copy");
        Check(window.SaveCopy(copy), "Recovered characters could not be saved.");
        Check(EngageSave.Load(copy).ReadRoster()[0].Availability == RosterAvailability.Available, "Recovery did not survive a GUI save.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(bytes), "The recovery GUI overwrote its source.");
        File.WriteAllBytes(source, Fixture(UnitForce.Dead, sequence: 3).Serialize());
        Check(window.LoadSave(source) && !button.IsEnabled, "The GUI offered restoration during battle.");
        byte[] blocked = window.Save!.Serialize();
        Check(!window.RestoreSelectedRosterCharacter() && window.Save!.Serialize().AsSpan().SequenceEqual(blocked),
            "A blocked GUI recovery partially modified the save.");
        window.FindControl<Avalonia.Controls.Primitives.TabStrip>("EmblemTabs")!.SelectedIndex = 0;
        Console.WriteLine("Roster recovery: dead/lost pools, death flags, HP, exact preservation, protected units, contexts, selection, localization and GUI save passed.");
    }

    internal static EngageSave Fixture(UnitForce force, ulong flags = DeadMask, byte sequence = 4, uint userFlags = 1,
        bool unknown = false, bool duplicate = false, bool unknownClass = false)
    {
        var save = RosterEquipmentTests.Fixture(force: force);
        byte[] bytes = save.Serialize();
        int user = save.Sections.Single(section => section.Name == "USER").PayloadOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(user + 32), userFlags);
        bytes[user + 36] = sequence;
        foreach (var entry in Records(save))
        {
            int offset = entry.Offset;
            ulong old = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset + 36));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset + 36), old | flags);
            bytes[offset + 111] = 0;
        }
        int second = Records(save)[1].Offset;
        if (unknown || duplicate)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(second + 46), unknown ? 0xdeadbeefu : save.ReadRoster()[0].PersonHash);
        if (unknownClass)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(second + 52), 0xdeadbeefu);
        FixChecksum(bytes);
        return EngageSave.Parse(bytes);
    }

    public static void CheckReal(EngageSave save)
    {
        byte[] original = save.Serialize();
        Check(save.ReadRoster().Where(character => RosterCatalog.Person(character.PersonHash) is not null)
            .All(character => character.Availability == RosterAvailability.Available && !save.CanRestoreRosterCharacter(character.Index)),
            "Normal real-save characters were treated as missing.");
        var records = Records(save);
        var target = records.Last(entry => RosterCatalog.Person(save.ReadRoster()[entry.Index].PersonHash) is not null);
        var user = save.Sections.Single(section => section.Name == "USER");
        bool battle = (BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(user.PayloadOffset + 32)) & 2) != 0;
        using var unit = new MemoryStream();
        var section = save.Sections.Single(section => section.Name == "UNIT");
        unit.Write(original.AsSpan(section.PayloadOffset, 32));
        foreach (var group in records.Where(entry => entry.Index != target.Index).GroupBy(entry => entry.Force).OrderBy(group => group.Key))
        {
            unit.WriteByte((byte)group.Key); unit.WriteByte((byte)group.Count());
            foreach (var entry in group) unit.Write(entry.Bytes);
        }
        byte[] dead = target.Bytes.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(dead.AsSpan(36), BinaryPrimitives.ReadUInt64LittleEndian(dead.AsSpan(36)) | DeadMask);
        dead[111] = 0;
        unit.WriteByte(4); unit.WriteByte(1); unit.Write(dead); unit.WriteByte(255);
        var prepared = ReplaceUnit(save, unit.ToArray());
        int index = prepared.ReadRoster().Count - 1;
        if (battle) Reject(() => prepared.RestoreRosterCharacter(index));
        else
        {
            var recovered = prepared.RestoreRosterCharacter(index);
            var person = recovered.ReadRoster().Single(character => character.PersonHash == save.ReadRoster()[target.Index].PersonHash);
            Check(person.Availability == RosterAvailability.Available && person.Progress.CurrentHP == recovered.RosterMaximumHP(person.Index),
                "A real-record recovery did not restore HP/availability.");
            Unrelated(save, recovered);
            Check(Equipment(recovered).SequenceEqual(Equipment(save)),
                "Real recovery changed a person's equipment.");
        }
        Check(save.Serialize().AsSpan().SequenceEqual(original), "The private save was changed during recovery tests.");
        Console.WriteLine("Real recovery: normal bench state, derived dead record, battle rejection and unchanged source passed.");
    }

    private sealed record UnitRecord(int Index, UnitForce Force, int Offset, byte[] Bytes);

    private static IEnumerable<(uint Person, uint Emblem, uint Partner, uint Ring)> Equipment(EngageSave save)
    {
        var roster = save.ReadRoster();
        return save.ReadCharacterRingLinks().Select(link => (Person: roster[link.CharacterIndex].PersonHash,
            link.EmblemInstance, link.PartnerEmblemInstance, link.RingInstance)).OrderBy(link => link.Person);
    }

    private static List<UnitRecord> Records(EngageSave save)
    {
        byte[] bytes = save.Serialize();
        var result = new List<UnitRecord>();
        int offset = save.Sections.Single(section => section.Name == "UNIT").PayloadOffset + 32;
        while (bytes[offset] != 255)
        {
            var force = (UnitForce)bytes[offset++];
            int count = bytes[offset++];
            for (int index = 0; index < count; index++)
            {
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                result.Add(new(result.Count, force, offset, bytes.AsSpan(offset, length).ToArray()));
                offset += length;
            }
        }
        return result;
    }

    private static EngageSave ReplaceUnit(EngageSave save, byte[] payload)
    {
        byte[] original = save.Serialize();
        using var output = new MemoryStream();
        output.Write(original.AsSpan(0, 260));
        int index = 0;
        foreach (var section in save.Sections)
        {
            int offset = (int)output.Position;
            output.Position = 132 + index++ * 4;
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write((uint)offset);
            output.Position = offset;
            if (section.Name != "UNIT") output.Write(original.AsSpan(section.Offset, section.Length + 8));
            else
            {
                output.Write("TINU"u8);
                using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write((uint)payload.Length + 4);
                output.Write(payload);
            }
        }
        int end = (int)output.Position;
        output.Position = 132 + index * 4;
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write((uint)end);
        output.Position = end;
        output.Write("LVRC"u8); output.Write(new byte[4]);
        byte[] result = output.ToArray();
        FixChecksum(result);
        return EngageSave.Parse(result);
    }

    private static void Unrelated(EngageSave before, EngageSave after)
    {
        byte[] source = before.Serialize(), edited = after.Serialize();
        Check(source.AsSpan(0, 132).SequenceEqual(edited.AsSpan(0, 132)), "Recovery changed the save summary.");
        foreach (var section in before.Sections.Where(section => section.Name != "UNIT"))
        {
            var target = after.Sections.Single(entry => entry.Name == section.Name);
            Check(source.AsSpan(section.PayloadOffset, section.Length).SequenceEqual(edited.AsSpan(target.PayloadOffset, target.Length)),
                "Recovery changed a section other than UNIT.");
        }
    }

    private static void FixChecksum(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), ~crc);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException("Unsafe character restoration was accepted.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
