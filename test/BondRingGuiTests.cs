using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class BondRingGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "bond-ring-management-source");
        byte[] original = EmblemTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Could not load ring management fixture.");
        window.ShowEmblems();
        var tabs = window.FindControl<TabStrip>("EmblemTabs")!;
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var list = window.FindControl<ListBox>("EmblemRecordList")!;
        var search = window.FindControl<TextBox>("EmblemSearch")!;
        var stock = window.FindControl<NumericUpDown>("BondRingStockInput")!;
        var fragments = window.FindControl<NumericUpDown>("BondFragmentsInput")!;
        var fill = window.FindControl<Button>("FillSBondRingsButton")!;
        var meld = window.FindControl<Button>("MeldBondRingButton")!;
        var cost = window.FindControl<TextBlock>("BondRingMeldCost")!;
        Check(fill.IsVisible && fill.IsEnabled && Equals(fill.Content, "Fill Missing S Rings")
            && fill.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch,
            "The full-width S-ring action is not present at the bottom of the list.");
        Check(Grid.GetRow((Control)fill.Parent!) == 4 && fill.Parent == window.FindControl<Button>("MaxAllEmblemBondsButton")!.Parent,
            "Ring fill was not aligned with the existing bottom batch action.");
        Check(meld.IsEnabled && Equals(meld.Content, "Meld to B") && cost.Text == "2 × C + 100 Bond Fragments",
            "Melding preview does not show the game costs.");
        Check(meld.Parent!.Parent == window.FindControl<StackPanel>("BondRingForm"), "Melding is in the wrong editor form.");
        stock.Text = "9";
        for (int pass = 0; pass < 2; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(Equals(fill.Content, "补齐 S 级戒指") && Equals(meld.Content, "合成为 B 级")
                && cost.Text == "消耗 2 个 C 级戒指和 100 羁绊碎片", "Chinese melding controls did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(Equals(meld.Content, "Meld to B") && stock.Text == "9", "Language switching lost pending stock or left Chinese labels.");
        }
        Check(window.ManageBondRings(meld: true), "GUI melding failed.");
        Check(window.Save!.ReadBondRings().Single(ring => ring.InstanceId == 10).StockCount == 7
            && window.Save.ReadMainValues().BondFragments == 1100 && fragments.Text == "1100",
            "Melding missed pending stock or failed to synchronize the Main fragment input.");
        Check(Equals(meld.Content, "Meld to A") && !meld.IsEnabled && stock.Value == 1,
            "The editor did not select the resulting B ring or block insufficient materials.");
        string copy = Path.Combine(temporary, "bond-ring-management-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadMainValues().BondFragments == 1100,
            "Saving restored the fragments spent on melding from stale Main controls.");

        window.LoadSave(source); window.ShowEmblems(); tabs.SelectedIndex = 1;
        stock.Text = "1";
        Check(!meld.IsEnabled, "Melding remained enabled with too few copies.");
        byte[] before = window.Save!.Serialize();
        Check(!window.ManageBondRings(meld: true) && window.Save.Serialize().AsSpan().SequenceEqual(before),
            "A failed GUI meld partially committed pending stock.");
        stock.Text = "2"; fragments.Text = "99";
        Check(!meld.IsEnabled && !window.ManageBondRings(meld: true)
            && window.Save.Serialize().AsSpan().SequenceEqual(before), "A failed meld spent resources or committed inputs.");
        fragments.Text = "1200"; stock.Text = "100";
        Check(!window.ManageBondRings(meld: false) && window.Save.Serialize().AsSpan().SequenceEqual(before),
            "Fill S silently clamped invalid pending stock.");
        stock.Text = "7";
        search.Text = "no-match"; Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 0 && window.ManageBondRings(meld: false), "Search incorrectly limited the S-ring fill scope.");
        Check(window.FindControl<TextBlock>("EmblemCompletion")!.Text == "S: 123/123" && !fill.IsEnabled,
            "S-ring completion did not refresh after fill.");
        search.Clear(); Dispatcher.UIThread.RunJobs();
        list.SelectedIndex = 1;
        Check(!window.FindControl<StackPanel>("BondRingMeldForm")!.IsVisible, "An S ring offered an invalid next rank.");
        tabs.SelectedIndex = 0;
        Check(!fill.IsVisible && !window.ManageBondRings(meld: false), "Ring actions appeared in character bonds.");

        var empty = BondRingTests.Fixture([]);
        string emptyPath = Path.Combine(temporary, "bond-ring-management-empty");
        empty.WriteCopy(emptyPath);
        window.LoadSave(emptyPath); window.ShowEmblems(); tabs.SelectedIndex = 1;
        Check(!window.FindControl<StackPanel>("BondRingForm")!.IsEnabled && fill.IsEnabled
            && window.ManageBondRings(meld: false) && window.Save!.ReadBondRings().Count == 123,
            "An empty ring pool required a selected ring before fill.");
        var caeda = ItemCatalog.Hash(EmblemTests.Caeda);
        var equipped = BondRingTests.Fixture([new(10, caeda, 1, null), new(11, caeda, 2, null)], RosterTests.Fixture(validEquipment: true));
        string equippedPath = Path.Combine(temporary, "bond-ring-management-equipped");
        equipped.WriteCopy(equippedPath);
        window.LoadSave(equippedPath); window.ShowEmblems(); tabs.SelectedIndex = 1;
        Check(!meld.IsEnabled && !window.ManageBondRings(meld: true), "GUI allowed a worn ring to be melded.");
        list.SelectedIndex = 1;
        Check(meld.IsEnabled && window.ManageBondRings(meld: true)
            && window.Save!.ReadCharacterRingLinks().SequenceEqual(equipped.ReadCharacterRingLinks()), "GUI melding changed ring equipment links.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI ring management overwrote its original save.");
        tabs.SelectedIndex = 0;
        Console.WriteLine("Bond ring GUI: batch scope, costs, equipped protection, pending inputs, save copies and live translations passed.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
