using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;
using FeeEditor.Gui.Localization;

internal static class LanguageTests
{
    public static void Run(MainWindow window, string temporary)
    {
        var menu = window.FindControl<MenuItem>("LanguageMenu")!;
        Check(menu.Items.Cast<MenuItem>().Select(item => item.Tag as string).SequenceEqual(LanguageCatalog.Codes),
            "The language menu must expose all nine languages once, without regional duplicates.");
        var english = UiLanguage.Read("en");
        var names = RosterCatalog.Persons.Select(row => row.Names)
            .Concat(RosterCatalog.Classes.Select(row => row.Names))
            .Concat(RosterCatalog.Skills.Select(row => row.Names))
            .Concat(ItemCatalog.Items.Select(row => row.Names))
            .Concat(QuantityItemCatalog.Items.Select(row => row.Names))
            .Concat(EmblemCatalog.Emblems.Select(row => row.Names))
            .Concat(EmblemCatalog.Rings.Select(row => row.Names))
            .Concat(DonationCatalog.Countries.Select(row => (IReadOnlyDictionary<string, string>)row.Names))
            .Concat(AchievementCatalog.Achievements.Select(row => row.Names))
            .Concat(MinigameCatalog.Groups.Select(row => row.Names))
            .Concat(MinigameCatalog.Groups.SelectMany(row => row.Records.Select(record => record.Names)))
            .ToArray();
        foreach (string language in LanguageCatalog.Codes)
        {
            var strings = UiLanguage.Read(language);
            Check(strings.Keys.Order().SequenceEqual(english.Keys.Order()), $"{language}: missing UI keys.");
            foreach (var (key, text) in strings)
            {
                Check(!string.IsNullOrWhiteSpace(text), $"{language}/{key}: empty text.");
                Check(Placeholders(text).SequenceEqual(Placeholders(english[key])), $"{language}/{key}: invalid placeholders.");
                _ = string.Format(CultureInfo.GetCultureInfo(language), text, 1, 2, 10000);
            }
            Check(names.All(row => row.TryGetValue(language, out string? name)
                && !string.IsNullOrWhiteSpace(name) && !name.Contains("\\x", StringComparison.Ordinal)),
                $"{language}: a game-data translation is missing or unresolved.");
        }
        Check(RosterCatalog.Persons.First(row => row.Id == "PID_ヴァンドレ").Name("ja") == "ヴァンドレ"
            && RosterCatalog.Persons.First(row => row.Id == "PID_ヴァンドレ").Name("ko") == "반드레",
            "Japanese and Korean names do not match the game text.");
        Check(AchievementCatalog.Achievements[0].Name("ko").Contains("반드레와", StringComparison.Ordinal),
            "Korean achievement particles were not resolved.");

        CheckPage(window, temporary, "language-main", MinigameTests.Fixture(), () =>
        {
            Check(Equals(window.FindControl<ComboBox>("DifficultyInput")!.SelectedItem, UiLanguage.Get("Hard")),
                "Difficulty did not translate.");
            var group = (MainWindow.MinigameChoice)window.FindControl<ComboBox>("MinigameInput")!.SelectedItem!;
            Check(group.Name == MinigameCatalog.Groups.Single(row => row.Id == group.Id).Name(UiLanguage.Current),
                "Minigame names did not translate.");
        });
        CheckPage(window, temporary, "language-roster", RosterTests.Fixture(), () =>
        {
            window.ShowRoster();
            var selected = (MainWindow.RosterRow)window.FindControl<ListBox>("RosterList")!.SelectedItem!;
            var person = window.Save!.ReadRoster()[selected.Index];
            Check(window.FindControl<TextBlock>("RosterName")!.Text == RosterCatalog.Person(person.PersonHash)!.Name(UiLanguage.Current),
                "Roster plain-text name did not translate.");
            Check(window.FindControl<TextBlock>("RosterStatusValue")!.Text == UiLanguage.Get("RosterAvailable"),
                "Availability did not translate.");
            Check(window.FindControl<ComboBox>("RosterClass")!.Items.Cast<MainWindow.ClassChoice>()
                .All(row => row.Label == row.Definition.Name(UiLanguage.Current)), "Class choices did not translate.");
        });
        CheckPage(window, temporary, "language-emblems", EmblemTests.Fixture(), () =>
        {
            window.ShowEmblems();
            var choices = window.FindControl<ComboBox>("EmblemChoiceInput")!.Items.Cast<MainWindow.EmblemChoice>();
            Check(choices.All(row => row.Label == EmblemCatalog.Emblem(window.Save!.ReadEmblems()
                .Single(emblem => emblem.InstanceId == row.Instance).EmblemId)!.Name(UiLanguage.Current)),
                "Emblem choices did not translate.");
            window.ShowBondRings();
            var rings = window.Save!.ReadBondRings();
            Check(window.FindControl<ListBox>("BondRingList")!.Items.Cast<MainWindow.EmblemRow>()
                .All(row => row.Label.StartsWith(EmblemCatalog.Ring(rings.Single(ring => ring.InstanceId.ToString() == row.Key).RingHash)!
                    .Name(UiLanguage.Current), StringComparison.Ordinal)), "Bond Rings did not translate.");
        });
        CheckPage(window, temporary, "language-achievements", AchievementTests.Fixture(), () =>
        {
            window.ShowAchievements();
            var filter = window.FindControl<ComboBox>("AchievementStatusFilter")!;
            filter.SelectedIndex = 3;
            Check(((MainWindow.AchievementStatusChoice)filter.SelectedItem!).Label == UiLanguage.Get("RewardClaimed")
                && window.FindControl<ListBox>("AchievementList")!.ItemCount == 1,
                "Achievement status filter did not translate or retained the wrong status.");
            Check(window.FindControl<TextBlock>("AchievementStatusValue")!.Text == UiLanguage.Get("RewardClaimed"),
                "Plain-text achievement status did not translate.");
        });
        window.SetLanguage("en");
        Console.WriteLine("Nine-language UI/catalog tests: keys, placeholders, game names, repeated switching, filters and byte-preserving previews passed.");
    }

    private static void CheckPage(MainWindow window, string temporary, string name, byte[] original, Action inspect)
    {
        string source = Path.Combine(temporary, name);
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Cannot load a language fixture.");
        foreach (string language in LanguageCatalog.Codes.Prepend("zh-Hans").Append("en"))
        {
            window.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
            var navigation = window.FindControl<TabStrip>("MainNavigation")!;
            string[] keys = ["Main", "Items", "Roster", "Emblems", "Support", "Achievements"];
            Check(navigation.Items.Cast<TabStripItem>().Select(item => item.Content as string).SequenceEqual(keys.Select(UiLanguage.Get)),
                "Top-level tabs retain text from a previous language.");
            Check(window.FindControl<TabStrip>("ItemPages")!.Items.Cast<TabStripItem>().Select(item => item.Content as string)
                .SequenceEqual(new[] { "Convoy", "ReclassItems", "Materials", "Ingredients", "Gifts", "KeyItems" }.Select(UiLanguage.Get)),
                "Item category tabs retain text from a previous language.");
            Check(window.FindControl<TabStrip>("EmblemPages")!.Items.Cast<TabStripItem>().Select(item => item.Content as string)
                .SequenceEqual(new[] { "Bonds", "BondRings" }.Select(UiLanguage.Get)), "Emblem tabs did not translate.");
            inspect();
        }
        Check(window.Save!.Serialize().AsSpan().SequenceEqual(original), "Language switching edited save data.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Language switching overwrote the source.");
    }

    private static IEnumerable<string> Placeholders(string value) => Regex.Matches(value, @"\{[^{}]+\}")
        .Select(match => match.Value).Order();
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
