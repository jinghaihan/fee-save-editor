using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class ActivityConditionTests
{
    public static void Run(MainWindow window, string temporary)
    {
        var save = ActivityFixture(1, 3);
        Check(save.ReadSomnielActivities() == new SomnielActivities(0, 0), "Spent counters were displayed as remaining uses.");
        byte[] original = save.Serialize();
        foreach (int training in new[] { 0, 1 })
        foreach (int arena in new[] { 0, 1, 2, 3 })
        {
            var values = new SomnielActivities(training, arena);
            var edited = save.WithSomnielActivities(values);
            Check(edited.ReadSomnielActivities() == values, "Activity boundaries did not round-trip.");
            Check(edited.WithSomnielActivities(new(0, 0)).Serialize().AsSpan().SequenceEqual(original), "Restoring counters changed unrelated bytes.");
            Check(edited.ReadMainValues() == save.ReadMainValues(), "Activity editing changed Main values.");
        }
        Check(ReferenceEquals(save, save.WithSomnielActivities(new(0, 0))), "Unchanged activity counters rewrote the save.");
        Check(save.WithRestoredSomnielActivities().ReadSomnielActivities() == new SomnielActivities(1, 3), "Restore uses wrote the wrong counters.");
        foreach (var invalid in new[] { new SomnielActivities(-1, 0), new(2, 0), new(0, -1), new(0, 4), new(int.MaxValue, 0) })
            Reject(() => save.WithSomnielActivities(invalid));
        foreach (var (training, arena) in new[] { (-1, 0), (2, 0), (0, -1), (0, 4) })
            Reject(() => ActivityFixture(training, arena).ReadSomnielActivities());
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Editing mutated original activity data.");

        string source = Path.Combine(temporary, "activities-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditActivities, "Activity controls did not enable.");
        var trainingInput = window.FindControl<NumericUpDown>("TrainingRemainingInput")!;
        var arenaInput = window.FindControl<NumericUpDown>("ArenaRemainingInput")!;
        Check(trainingInput.Value == 0 && arenaInput.Value == 0 && trainingInput.Maximum == 1 && arenaInput.Maximum == 3,
            "Activity controls do not show actual remaining uses and limits.");
        window.FindControl<Button>("RestoreActivitiesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.SetLanguage("ja"); window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
        Check(trainingInput.Value == 1 && arenaInput.Value == 3, "Language changes discarded restored uses.");
        string copy = Path.Combine(temporary, "activities-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadSomnielActivities() == new SomnielActivities(1, 3), "File save omitted activity edits.");
        foreach (string invalid in new[] { "-1", "4", "1.5", "abc", "" })
        {
            byte[] before = window.Save!.Serialize();
            arenaInput.Text = invalid;
            Check(!window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid activity input partly committed Main.");
        }
        Check(window.LoadSave(source), "Could not reload activities.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Activity GUI overwrote its source.");

        var rings = RosterEquipmentTests.Fixture(mutateGod: bytes => { bytes[53] = 255; bytes[73] = 123; });
        byte[] ringOriginal = rings.Serialize();
        var conditions = rings.ReadEmblemConditions();
        Check(conditions.Count == 2 && conditions[0].Dirtiness == 255 && conditions[1].Dirtiness == 123,
            "Dirtiness confused ownership flags, DLC bracelets or Engage+.");
        foreach (int dirt in new[] { 0, 1, 127, 255 })
        {
            var edited = rings.WithEmblemDirtiness(1, dirt);
            Check(edited.ReadEmblemConditions()[0].Dirtiness == dirt && edited.ReadEmblemConditions()[1].Dirtiness == 123, "Dirtiness edited another ring.");
            Check(edited.WithEmblemDirtiness(1, 255).Serialize().AsSpan().SequenceEqual(ringOriginal), "Ring cleaning changed unrelated data.");
            var allowed = new HashSet<int>(Enumerable.Range(ringOriginal.Length - 4, 4))
                { rings.Sections.Single(section => section.Name == "GOD").PayloadOffset + 53 };
            byte[] editedBytes = edited.Serialize();
            Check(Enumerable.Range(0, ringOriginal.Length).All(index => ringOriginal[index] == editedBytes[index] || allowed.Contains(index)),
                "Cleaning modified more than the dirty byte and checksum.");
        }
        Check(ReferenceEquals(rings, rings.WithEmblemDirtiness(1, 255)), "A no-op ring edit changed bytes.");
        foreach (int dirt in new[] { -1, 256, int.MaxValue }) Reject(() => rings.WithEmblemDirtiness(1, dirt));
        foreach (uint id in new[] { 0u, 3u, 999u }) Reject(() => rings.WithEmblemDirtiness(id, 0));
        Reject(() => EngageSave.Parse(EmblemTests.Fixture()).ReadEmblemConditions());
        var added = RosterEquipmentTests.Fixture(godInstances: [1, 3]).WithAddedEmblem("GID_シグルド");
        var sigurd = added.ReadEmblemConditions().Single(row => row.EmblemId == "GID_シグルド");
        Check(sigurd.InstanceId == 2 && sigurd.BondHolderId == 4, "Fixture does not distinguish GOD and GDBD identities.");
        Check(added.WithEmblemDirtiness(sigurd.InstanceId, 250).ReadEmblemConditions().Single(row => row.InstanceId == 2).Dirtiness == 250,
            "Editing dirtiness assumed owned and bond-holder IDs were equal.");
        var all = added;
        foreach (var definition in EmblemCreationCatalog.Emblems) all = all.WithAddedEmblem(definition.Id);
        foreach (var condition in all.ReadEmblemConditions())
            Check(all.WithEmblemDirtiness(condition.InstanceId, 255).WithEmblemDirtiness(condition.InstanceId, condition.Dirtiness)
                .Serialize().AsSpan().SequenceEqual(all.Serialize()), "A base-game or DLC ring failed reversible cleaning.");

        var dirtyAll = all;
        foreach (var condition in all.ReadEmblemConditions()) dirtyAll = dirtyAll.WithEmblemDirtiness(condition.InstanceId, 255);
        var cleanAll = dirtyAll.WithCleanedEmblems();
        Check(cleanAll.ReadEmblemConditions().Count == 19 && cleanAll.ReadEmblemConditions().All(row => row.Dirtiness == 0),
            "Batch cleaning omitted a base-game or DLC ring.");
        Check(ReferenceEquals(cleanAll, cleanAll.WithCleanedEmblems()), "Already-clean rings were rewritten.");
        foreach (var condition in cleanAll.ReadEmblemConditions()) cleanAll = cleanAll.WithEmblemDirtiness(condition.InstanceId, 255);
        Check(cleanAll.Serialize().AsSpan().SequenceEqual(dirtyAll.Serialize()), "Batch cleaning changed equipment, bonds or other bytes.");
        var physicalOnly = RosterEquipmentTests.Fixture(mutateGod: bytes => { bytes[53] = 255; bytes[73] = 123; bytes[93] = 77; });
        byte[] cleanedPhysical = physicalOnly.WithCleanedEmblems().Serialize();
        int god = physicalOnly.Sections.Single(section => section.Name == "GOD").PayloadOffset;
        Check(cleanedPhysical[god + 53] == 0 && cleanedPhysical[god + 73] == 0 && cleanedPhysical[god + 93] == 77,
            "Batch cleaning changed Engage+ instead of only physical rings.");

        source = Path.Combine(temporary, "ring-care-source");
        File.WriteAllBytes(source, ringOriginal);
        Check(window.LoadSave(source) && window.CanEditEmblemConditions, "Ring care did not load.");
        window.ShowEmblems();
        var dirtiness = window.FindControl<NumericUpDown>("EmblemDirtinessInput")!;
        var choices = window.FindControl<ComboBox>("EmblemChoiceInput")!;
        Check(dirtiness.Value == 255 && dirtiness.Maximum == 255, "Ring care shows wrong values or limits.");
        dirtiness.Text = "42";
        window.SetLanguage("zh-Hans"); window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
        Check(dirtiness.Text == "42", "Language switching discarded dirty edits.");
        choices.SelectedIndex = 1;
        Check(window.Save!.ReadEmblemConditions()[0].Dirtiness == 42 && dirtiness.Value == 123, "Changing Emblems lost dirtiness edits.");
        window.FindControl<Button>("CleanEmblemButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.Save.ReadEmblemConditions()[1].Dirtiness == 0 && window.Save.ReadEmblemConditions()[0].Dirtiness == 42, "Clean targeted the wrong Emblem.");
        dirtiness.Text = "200";
        window.FindControl<TextBox>("EmblemSearch")!.Text = "no-match";
        Check(window.ApplyEmblemValues() && window.Save.ReadEmblemConditions()[1].Dirtiness == 200, "Cleaning wrongly requires a visible character bond.");
        choices.SelectedIndex = 2;
        Check(!window.FindControl<StackPanel>("EmblemConditionForm")!.IsEnabled && dirtiness.Value is null, "Engage+ was treated as a physical ring.");
        choices.SelectedIndex = 0;
        foreach (string invalid in new[] { "-1", "256", "1.5", "abc", "" })
        {
            byte[] before = window.Save.Serialize();
            dirtiness.Text = invalid;
            Check(!window.ApplyEmblemValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid dirtiness partly committed Emblem changes.");
        }
        dirtiness.Text = "7";
        window.ShowBondRings(); window.ShowEmblems();
        Check(window.Save.ReadEmblemConditions()[0].Dirtiness == 7, "Switching to Bond Rings lost dirty edits.");
        dirtiness.Text = "8";
        copy = Path.Combine(temporary, "ring-care-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadEmblemConditions()[0].Dirtiness == 8, "File save omitted dirtiness.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(ringOriginal), "Ring care overwrote its source.");
        Check(window.LoadSave(source), "Could not reload ring care.");
        var batch = window.FindControl<Button>("CleanAllEmblemsButton")!;
        Check(batch.Parent == window.FindControl<Button>("MaxAllEmblemBondsButton")!.Parent
            && batch.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch,
            "Clean All must be beside the other batch actions below the left list.");
        Check(batch.IsEnabled, "Batch cleaning is not available.");
        dirtiness.Text = "256";
        byte[] beforeBatch = window.Save!.Serialize();
        batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeBatch), "Invalid input partly committed batch cleaning.");
        dirtiness.Text = "8";
        batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.Save.ReadEmblemConditions().All(row => row.Dirtiness == 0) && dirtiness.Value == 0,
            "Batch cleaning only cleaned the selected ring or left stale inputs.");
        copy = Path.Combine(temporary, "ring-care-batch-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadEmblemConditions().All(row => row.Dirtiness == 0),
            "File Save omitted batch cleaning.");
        Console.WriteLine("Activities and ring care: limits, byte-exact reversals, all base/DLC rings, identity mapping, GUI navigation/language/save passed.");
    }

    private static EngageSave ActivityFixture(int training, int arena)
    {
        byte[] bytes = MainTests.Fixture();
        byte[] marker = [0x88, 0x13, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0];
        int money = bytes.AsSpan().IndexOf(marker);
        Check(money >= 0, "The independent fixture has no activity counters.");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(money + 8), training);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(money + 12), arena);
        uint crc = uint.MaxValue;
        foreach (byte value in bytes.AsSpan(0, bytes.Length - 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), ~crc);
        return EngageSave.Parse(bytes);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid activity/ring edit was accepted.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
