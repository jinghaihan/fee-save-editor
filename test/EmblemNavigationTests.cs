using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class EmblemNavigationTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "emblem-navigation-source");
        byte[] original = EmblemTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Cannot load the independent-panel fixture.");
        var navigation = window.FindControl<TabStrip>("MainNavigation")!;
        var pages = window.FindControl<TabStrip>("EmblemPages")!;
        var container = window.FindControl<Grid>("EmblemPagesPanel")!;
        Check(navigation.ItemCount == 6 && pages.ItemCount == 2 && pages.Parent == container,
            "Emblems must use page-level tabs above both cards.");
        var bonds = window.FindControl<Grid>("EmblemsPanel")!;
        var rings = window.FindControl<Grid>("BondRingsPanel")!;
        window.ShowEmblems();
        var experience = window.FindControl<NumericUpDown>("EmblemBondExpInput")!;
        experience.Text = "108";
        pages.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Check(rings.IsVisible && !bonds.IsVisible && window.Save!.ReadEmblems()[0].Bonds[0].Experience == 108,
            "Switching to Bond Rings discarded a pending bond edit or left Emblems visible.");
        var stock = window.FindControl<NumericUpDown>("BondRingStockInput")!;
        stock.Text = "8";
        pages.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Check(bonds.IsVisible && !rings.IsVisible && window.Save!.ReadBondRings()[0].StockCount == 8,
            "Switching to Emblems discarded pending stock or left Bond Rings visible.");
        var bondSearch = window.FindControl<TextBox>("EmblemSearch")!;
        var ringSearch = window.FindControl<TextBox>("BondRingSearch")!;
        bondSearch.Text = "Yunaka";
        window.ShowBondRings();
        ringSearch.Text = "Caeda";
        window.ShowEmblems();
        Check(bondSearch.Text == "Yunaka" && ringSearch.Text == "Caeda"
            && window.FindControl<ListBox>("EmblemRecordList")!.ItemCount == 1,
            "Independent searches were cleared or applied to the other page.");
        window.ShowBondRings();
        byte[] before = window.Save!.Serialize();
        stock.Text = "100";
        pages.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Check(rings.IsVisible && !bonds.IsVisible && navigation.SelectedIndex == 3 && pages.SelectedIndex == 1 && stock.Text == "100"
            && window.Save.Serialize().AsSpan().SequenceEqual(before),
            "An invalid ring edit did not retain the page, input and unchanged save.");
        stock.Text = "8";
        foreach (var show in new Action[] { window.ShowItems, window.ShowRoster, window.ShowSupports, window.ShowAchievements })
        {
            show();
            Check(!container.IsVisible && !rings.IsVisible && !bonds.IsVisible, "An Emblem-related panel overlaps another page.");
            window.ShowBondRings();
            Check(container.IsVisible && rings.IsVisible && navigation.SelectedIndex == 3 && pages.SelectedIndex == 1, "Cannot return to Bond Rings.");
        }
        string copy = Path.Combine(temporary, "emblem-navigation-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadBondRings()[0].StockCount == 8,
            "Global save omitted an independent page's changes.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Panel navigation overwrote its source save.");
        window.ShowEmblems();
        Console.WriteLine("Independent Emblem/Bond Ring pages: navigation, drafts, validation, searches and save copies passed.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
