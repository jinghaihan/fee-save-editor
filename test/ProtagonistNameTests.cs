using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class ProtagonistNameTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = RosterTests.Fixture(protagonistName: "Alear");
        var save = EngageSave.Parse(original);
        Check(save.ReadProtagonistName() == "Alear" && ReferenceEquals(save, save.WithProtagonistName("Alear")), "Name load/no-op failed.");
        foreach (string name in new[] { "琉尔", "Renamed Alear", "アリア", "Aléar", "A😀" })
        {
            var edited = save.WithProtagonistName(name);
            Check(EngageSave.Parse(edited.Serialize()).ReadProtagonistName() == name && edited.ReadRoster()[0].Progress.Gender == 2,
                "Name encoding, resize or customization metadata changed.");
            Check(edited.WithProtagonistName("Alear").Serialize().AsSpan().SequenceEqual(original), "Name reversal was not byte exact.");
            foreach (var section in save.Sections.Where(section => section.Name != "UNIT"))
            {
                var changed = edited.Sections.Single(row => row.Name == section.Name);
                Check(original.AsSpan(section.PayloadOffset, section.Length).SequenceEqual(edited.Serialize().AsSpan(changed.PayloadOffset, changed.Length)),
                    "Renaming changed an unrelated section.");
            }
        }
        foreach (string name in new[] { "", " ", "a\n", "\ud800", new string('a', 2049) })
        {
            try { save.WithProtagonistName(name); throw new InvalidOperationException("Invalid name accepted."); }
            catch (ArgumentException) { }
            Check(save.Serialize().AsSpan().SequenceEqual(original), "Invalid name mutated the source.");
        }
        var uncustomized = EngageSave.Parse(RosterTests.Fixture());
        Check(uncustomized.ReadProtagonistName() is null, "An unsaved customization was invented.");
        try { uncustomized.WithProtagonistName("Alear"); throw new InvalidOperationException("Missing customization accepted."); }
        catch (InvalidDataException) { }
        var duplicate = EngageSave.Parse(RosterTests.Fixture(protagonistName: "Alear", mutate: payload =>
        {
            int second = 34 + (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(34, 4));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(second + 46, 4), ItemCatalog.Hash(EmblemCatalog.AlearPersonId));
        }));
        try { duplicate.WithProtagonistName("New"); throw new InvalidOperationException("Ambiguous protagonist accepted."); }
        catch (InvalidDataException) { }
        string source = Path.Combine(temporary, "protagonist-name-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Cannot load customized fixture.");
        var input = window.FindControl<TextBox>("PlayerNameInput")!;
        var settings = window.FindControl<StackPanel>("SettingsInputs")!;
        Check(input.IsEnabled && input.Text == "Alear" && input.GetLogicalAncestors().Contains(settings.Children[0]),
            "Name editing is not the first game setting.");
        input.Text = "改名琉尔";
        window.SetLanguage("ja"); window.SetLanguage("en");
        window.ShowRoster(); Dispatcher.UIThread.RunJobs();
        window.FindControl<TextBox>("RosterSearch")!.Text = "Alear";
        window.FindControl<NumericUpDown>("RosterLevel")!.Text = "7";
        Check(input.Text == "改名琉尔" && window.ApplyMainValues(), "Language/navigation lost the pending name.");
        Check(((MainWindow.RosterRow)window.FindControl<ListBox>("RosterList")!.Items[0]!).Label.StartsWith("改名琉尔")
            && window.FindControl<NumericUpDown>("RosterLevel")!.Text == "7",
            "Roster did not refresh the new name.");
        Check(window.FindControl<Control>("RosterName") is null, "Roster still repeats the character name.");
        string copy = Path.Combine(temporary, "protagonist-name-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadProtagonistName() == "改名琉尔"
            && EngageSave.Load(copy).ReadRoster()[0].Values.Level == 7, "Save Copy omitted the name or discarded pending roster values.");
        byte[] before = window.Save!.Serialize();
        input.Text = " ";
        window.FindControl<NumericUpDown>("MoneyInput")!.Value = 777;
        Check(!window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid name partly committed Main.");
        Check(window.LoadSave(source) && input.Text == "Alear", "Reload retained a stale name.");
        string plain = Path.Combine(temporary, "protagonist-name-absent");
        File.WriteAllBytes(plain, RosterTests.Fixture());
        Check(window.LoadSave(plain) && !input.IsEnabled && input.Text == "", "Missing customization left stale editable data.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Name tests modified the source.");
        Console.WriteLine("Protagonist name: Unicode, exact reversals, metadata preservation, validation and GUI Save Copy passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
