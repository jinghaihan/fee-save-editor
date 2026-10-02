using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class EmblemCreationTests
{
    private const string Sigurd = "GID_シグルド";

    public static void Run(MainWindow window, string temporary)
    {
        var save = RosterEquipmentTests.Fixture(godInstances: [1, 3]);
        byte[] original = save.Serialize();
        Check(EmblemCreationCatalog.Emblems.Count == 19 && EmblemCreationCatalog.Emblems.Count(row => row.Dlc) == 7,
            "Creation must support every normal base/DLC Emblem, without Engage+.");
        Check(save.ReadMissingEmblems().Count == 18 && save.ReadMissingEmblems().Any(row => row.Id == EmblemTests.Tiki),
            "A saved bond holder must not be mistaken for an owned Emblem.");
        var restored = save.WithAddedEmblem(EmblemTests.Tiki);
        Check(Payload(save, "GDBD").AsSpan().SequenceEqual(Payload(restored, "GDBD")), "Restoring an Emblem reset its old bonds.");
        Check(restored.ReadRosterEquipmentOptions(1).Any(option => option.EmblemId == EmblemTests.Tiki),
            "Restored DLC equipment is unavailable.");
        Unrelated(save, restored, "GOD");
        Check(ReferenceEquals(restored, restored.WithAddedEmblem(EmblemTests.Tiki)), "An already-owned Emblem was duplicated.");
        var fresh = save.WithAddedEmblem(Sigurd);
        var holder = fresh.ReadEmblems().Single(row => row.EmblemId == Sigurd);
        Check(holder.InstanceId == 4 && holder.PactPartner is null && holder.Bonds.Count == 2
            && holder.Bonds.All(bond => bond.Level == 1 && bond.Experience == 0 && bond.InheritedSkills.Count == 0 && bond.TalkFlags == 0),
            "New Emblem bonds did not use the game's initial values.");
        var equipment = fresh.ReadRosterEquipmentOptions(1).Single(option => option.EmblemId == Sigurd);
        Check(equipment.Selection.InstanceId == 2 && equipment.OwnerIndex is null,
            "God and holder IDs were assumed identical, or the new Emblem was auto-equipped.");
        Check(fresh.WithRosterEquipment(1, equipment.Selection).ReadRosterEquipment(1) == equipment.Selection,
            "A newly created Emblem cannot be equipped.");
        Unrelated(save, fresh, "GOD", "GDBD");
        foreach (var definition in EmblemCreationCatalog.Emblems)
        {
            var added = save.WithAddedEmblem(definition.Id);
            Check(added.ReadMissingEmblems().All(row => row.Id != definition.Id), "A base/DLC Emblem is still missing after addition.");
            Check(EngageSave.Parse(added.Serialize()).ReadEmblems().Any(row => row.EmblemId == definition.Id), "New bonds did not round-trip.");
            Check(added.ReadRosterEquipmentOptions(1).Any(option => option.EmblemId == definition.Id), "A base/DLC addition cannot be equipped.");
        }
        var complete = save;
        foreach (var definition in EmblemCreationCatalog.Emblems) complete = complete.WithAddedEmblem(definition.Id);
        Check(complete.ReadMissingEmblems().Count == 0 && complete.ReadEmblems().Count == 20, "Sequential additions are incomplete or duplicated.");
        Unrelated(save, complete, "GOD", "GDBD");
        foreach (var definition in EmblemCreationCatalog.Emblems)
            Check(ReferenceEquals(complete, complete.WithAddedEmblem(definition.Id)), "Repeating additions changed the complete save.");
        foreach (string invalid in new[] { "", "GID_unknown", "GID_マルス_敵", EmblemCatalog.AlearEmblemId })
            Reject(() => save.WithAddedEmblem(invalid));
        Reject(() => RosterEquipmentTests.Fixture(missingGod: true).WithAddedEmblem(Sigurd));
        Reject(() => RosterEquipmentTests.Fixture(partner: 2, godInstances: [1, 3]).WithAddedEmblem(EmblemTests.Tiki));
        Reject(() => RosterEquipmentTests.Fixture(mutateGod: data => Write32(data, 36, 129)).WithAddedEmblem(Sigurd));
        Reject(() => RosterEquipmentTests.Fixture(mutateGod: data => Write32(data, 46, 99)).WithAddedEmblem(Sigurd));
        foreach (int flag in new[] { 0, 1, 2 })
        {
            var unavailable = RosterEquipmentTests.Fixture(godFlag: flag);
            Check(ReferenceEquals(unavailable, unavailable.WithAddedEmblem(EmblemTests.Tiki))
                && unavailable.ReadMissingEmblems().All(row => row.Id != EmblemTests.Tiki),
                "Adding reactivated or duplicated a dark/reserved/escaping Emblem.");
        }
        var full = FullHolderPool(save);
        Reject(() => full.WithAddedEmblem(Sigurd));
        Check(full.WithAddedEmblem(EmblemTests.Tiki).ReadMissingEmblems().All(row => row.Id != EmblemTests.Tiki),
            "A full holder pool prevented reusing an existing holder.");
        foreach (var force in Enum.GetValues<UnitForce>())
        {
            var added = RosterEquipmentTests.Fixture(force: force).WithAddedEmblem(Sigurd);
            int expected = force is UnitForce.Player or UnitForce.Absent or UnitForce.Dead or UnitForce.Lost ? 2 : 0;
            Check(added.ReadEmblems().Single(row => row.EmblemId == Sigurd).Bonds.Count == expected,
                "New bonds did not follow the native playable-unit forces.");
        }
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Emblem creation mutated the source buffer.");

        string source = Path.Combine(temporary, "emblem-add-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Could not load the missing-Emblem GUI fixture.");
        window.ShowEmblems();
        window.ShowEmblems();
        var button = window.FindControl<Button>("AddEmblemButton")!;
        var missing = window.FindControl<ComboBox>("MissingEmblemInput")!;
        Check(button.IsEnabled && missing.ItemCount == 18, "The add control did not show missing Emblems.");
        Check(window.FindControl<SukiUI.Controls.GlassCard>("AddEmblemCard")!.IsOpaque,
            "The add popover must use the framework's opaque card, without showing underlying buttons.");
        missing.SelectedItem = missing.Items.Cast<MainWindow.MissingEmblemChoice>().Single(row => row.Id == EmblemTests.Tiki);
        for (int pass = 0; pass < 2; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(Equals(button.Content, "添加纹章士") && missing.Items.Cast<MainWindow.MissingEmblemChoice>().Any(row => row.Label == "琪姬"),
                "The add control did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check((missing.SelectedItem as MainWindow.MissingEmblemChoice)?.Id == EmblemTests.Tiki
                && missing.Items.Cast<MainWindow.MissingEmblemChoice>().All(row => !row.Label.Contains("琪姬")),
                "Language switching lost the selected missing Emblem or left stale names.");
        }
        var experience = window.FindControl<NumericUpDown>("EmblemBondExpInput")!;
        experience.Text = "108";
        button.Flyout!.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        window.FindControl<Button>("AddEmblemConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!button.Flyout.IsOpen && window.Save!.ReadMissingEmblems().All(row => row.Id != EmblemTests.Tiki)
            && window.Save.ReadEmblems()[0].Bonds[0].Experience == 108,
            "Adding discarded pending bond edits.");
        Check(window.Save!.ReadEmblems()[1].Bonds[0].Level == 1, "Adding maximized existing DLC bonds.");
        experience.Text = "abc";
        byte[] before = window.Save.Serialize();
        Check(!window.AddEmblem(Sigurd) && window.Save.Serialize().AsSpan().SequenceEqual(before),
            "Invalid pending values partially added an Emblem.");
        experience.Text = "0";
        Check(window.AddEmblem(Sigurd) && (window.FindControl<ComboBox>("EmblemChoiceInput")!.SelectedItem as MainWindow.EmblemChoice)?.Instance == 4,
            "The add operation did not select its new bond holder.");
        window.ShowBondRings();
        Check(!window.FindControl<Grid>("EmblemsPanel")!.IsVisible && !window.AddEmblem("GID_セリカ"), "The add operation appeared in the Bond Ring tab.");
        window.ShowEmblems();
        string output = Path.Combine(temporary, "emblem-add-copy");
        Check(window.SaveCopy(output) && EngageSave.Load(output).ReadMissingEmblems().Count == 16, "Global save omitted added Emblems.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Emblem GUI tests overwrote the source save.");
        string completeSource = Path.Combine(temporary, "emblem-complete-source");
        File.WriteAllBytes(completeSource, complete.Serialize());
        Check(window.LoadSave(completeSource) && !button.IsEnabled && missing.ItemCount == 0, "The complete collection still enabled addition.");
        Console.WriteLine("Emblem creation: all 19 normal/DLC rings, existing/fresh bonds, equipment, bounds, translation and pending saves passed.");
    }

    private static EngageSave FullHolderPool(EngageSave save)
    {
        using var pool = new MemoryStream();
        pool.Write(Payload(save, "GDBD"));
        using (var writer = new BinaryWriter(pool, Encoding.UTF8, leaveOpen: true))
            for (uint id = 4; id <= 64; id++)
            {
                writer.Write(id);
                byte[] gid = Encoding.Unicode.GetBytes($"GID_unknown_{id}");
                writer.Write((uint)gid.Length); writer.Write(gid); writer.Write((byte)0); writer.Write((ushort)0);
            }
        byte[] payload = pool.ToArray();
        Write32(payload, 32, 64);
        return ReplacePool(save, "GDBD", payload);
    }

    private static EngageSave ReplacePool(EngageSave save, string name, byte[] replacement)
    {
        byte[] original = save.Serialize();
        using var result = new MemoryStream();
        result.Write(original.AsSpan(0, 260));
        var offsets = new List<uint>();
        using var writer = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true);
        foreach (var section in save.Sections)
        {
            offsets.Add((uint)result.Position);
            writer.Write(original.AsSpan(section.Offset, 4));
            byte[] payload = section.Name == name ? replacement : Payload(save, section.Name);
            writer.Write((uint)payload.Length + 4); writer.Write(payload);
        }
        offsets.Add((uint)result.Position);
        writer.Write("LVRC"u8); writer.Write(0u);
        byte[] bytes = result.ToArray();
        for (int index = 0; index < offsets.Count; index++) Write32(bytes, 132 + index * 4, offsets[index]);
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        Write32(bytes, bytes.Length - 4, ~crc);
        return EngageSave.Parse(bytes);
    }

    private static byte[] Payload(EngageSave save, string name)
    {
        var section = save.Sections.Single(section => section.Name == name);
        return save.Serialize().AsSpan(section.PayloadOffset, section.Length).ToArray();
    }
    private static void Unrelated(EngageSave before, EngageSave after, params string[] allowed)
    {
        Check(before.Serialize().AsSpan(0, 132).SequenceEqual(after.Serialize().AsSpan(0, 132)), "Adding changed save-summary data.");
        foreach (var section in before.Sections.Where(section => !allowed.Contains(section.Name)))
            Check(Payload(before, section.Name).AsSpan().SequenceEqual(Payload(after, section.Name)), $"Adding changed unrelated {section.Name} data.");
    }
    private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid Emblem creation was accepted.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
