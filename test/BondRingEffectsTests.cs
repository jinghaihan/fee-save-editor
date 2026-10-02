using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;
using FeeEditor.Gui.Localization;

internal static class BondRingEffectsTests
{
    public static void Run(MainWindow window, string temporary)
    {
        var olwen = EmblemCatalog.Rings.Single(row => row.Id == "RNID_トラキア_オルエン_S");
        Check(olwen.StatBonuses[(int)RosterStat.Speed] == 2 && olwen.StatBonuses[(int)RosterStat.Magic] == 1
            && olwen.StatBonuses[(int)RosterStat.Luck] == 1 && olwen.StatBonuses.Sum() == 4,
            "Olwen's native stat bonuses were not imported correctly.");
        Check(olwen.Skills.Single().Name("en") == "Dire Thunder"
            && olwen.Skills.Single().Description("en").Contains("Excludes Elthunder"),
            "Ring skill names or effects were misread.");
        Check(EmblemCatalog.Rings.Count(row => row.Skills.Count > 0) == 28
            && EmblemCatalog.Rings.Where(row => row.Rank != 3).All(row => row.Skills.Count == 0),
            "Native S-rank skills were omitted or applied to lower ranks.");
        foreach (var ring in EmblemCatalog.Rings)
        {
            Check(ring.StatBonuses.Count == 11 && ring.StatBonuses.All(value => value >= 0), "Invalid ring stat layout.");
            foreach (var skill in ring.Skills)
                foreach (string language in LanguageCatalog.Codes)
                    Check(!string.IsNullOrWhiteSpace(skill.Names.GetValueOrDefault(language))
                        && !string.IsNullOrWhiteSpace(skill.Descriptions.GetValueOrDefault(language))
                        && !skill.Description(language).Contains(@"\n"), "Ring effects have missing translations or escaped newlines.");
        }

        var save = BondRingTests.Fixture([new(10, olwen.Hash, 3, null), new(11, ItemCatalog.Hash(EmblemTests.Caeda), 2, null),
            new(12, 0x12345678, 1, null)]);
        string source = Path.Combine(temporary, "ring-effects-source");
        save.WriteCopy(source);
        window.LoadSave(source); window.ShowBondRings();
        var list = window.FindControl<ListBox>("BondRingList")!;
        var stats = window.FindControl<ItemsControl>("BondRingStatBonuses")!;
        var skills = window.FindControl<ItemsControl>("BondRingSkills")!;
        var preview = window.FindControl<StackPanel>("BondRingEffects")!;
        var skillPanel = window.FindControl<StackPanel>("BondRingSkillEffects")!;
        var stock = window.FindControl<NumericUpDown>("BondRingStockInput")!;
        Check(preview.IsVisible && skillPanel.IsVisible && stats.Items.Count == 3 && skills.Items.Count == 1,
            "The selected ring's stat and skill previews are missing.");
        stock.Text = "4";
        foreach (string language in LanguageCatalog.Codes)
        {
            window.SetLanguage(language); Dispatcher.UIThread.RunJobs();
            var skill = (MainWindow.BondRingSkillEffect)skills.Items[0]!;
            Check(skill.Name == olwen.Skills[0].Name(language) && skill.Description == olwen.Skills[0].Description(language)
                && stats.Items.Cast<MainWindow.EngravingEffect>().Any(row => row.Label == UiLanguage.Get("Speed") && row.Value == "+2")
                && stock.Text == "4", "Ring preview switching lost drafts or left stale game text.");
        }
        stock.Text = "3";
        window.SetLanguage("en"); list.SelectedIndex = 1;
        Check(preview.IsVisible && !skillPanel.IsVisible && skills.Items.Count == 0, "A lower-rank ring retained the previous skill.");
        list.SelectedIndex = 2;
        Check(!preview.IsVisible && stats.Items.Count == 0 && skills.Items.Count == 0, "An unknown ring showed invented bonuses.");
        Check(window.Save!.Serialize().AsSpan().SequenceEqual(save.Serialize())
            && File.ReadAllBytes(source).AsSpan().SequenceEqual(save.Serialize()), "Effect previews edited the save.");
        window.ShowEmblems();
        Console.WriteLine("Bond ring effects: all 483 definitions, 28 S-rank skills, nine languages, cleared previews and draft preservation passed.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
