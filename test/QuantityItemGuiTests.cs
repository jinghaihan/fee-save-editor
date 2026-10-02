using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class QuantityItemGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "quantities");
        byte[] original = QuantityItemTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditQuantityItems, "Quantity editing is unavailable for USER without TRAN.");
        window.ShowItems();
        var tabs = window.FindControl<TabStrip>("ItemPages")!;
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<Grid>("QuantityItemsPanel")!.IsVisible && !window.FindControl<Grid>("ConvoyPanel")!.IsVisible,
            "Quantity and convoy editors were merged.");
        Check(tabs.ItemCount == 6 && window.FindControl<ComboBox>("QuantityCategoryFilter") is null,
            "Item categories must be direct tabs, without a category dropdown.");
        var search = window.FindControl<TextBox>("QuantitySearch")!;
        var list = window.FindControl<ListBox>("QuantityItemList")!;
        var amount = window.FindControl<NumericUpDown>("QuantityAmountInput")!;
        Check(list.ItemCount == 4, "Reclass filter must include both DLC items.");
        list.SelectedItem = list.Items.OfType<MainWindow.QuantityItemRow>().Single(row => row.Item.Definition.Id == "IID_マスタープルフ");
        Check(amount.Value == 5 && amount.Maximum == 999, "Reclass editor loaded incorrect quantity or limits.");
        amount.Text = "998";
        foreach (string language in LanguageCatalog.Codes)
        {
            window.SetLanguage(language);
            Check(amount.Text == "998" && list.ItemCount == 4, "Switching language discarded a pending amount or category.");
            var selected = (MainWindow.QuantityItemRow)list.SelectedItem!;
            Check(window.FindControl<TextBlock>("QuantityItemName")!.Text == selected.Item.Definition.Name(language), "Quantity name did not translate.");
        }
        window.SetLanguage("en");
        Check(window.ApplyQuantityItemValues(), "GUI quantity apply failed.");
        Check(window.Save!.ReadQuantityItems().Single(row => row.Definition.Id == "IID_マスタープルフ").Amount == 998,
            "GUI quantity edit did not reach USER.");
        amount.Text = "1000";
        byte[] before = window.Save.Serialize();
        Check(!window.ApplyQuantityItemValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid GUI amount mutated the save.");
        amount.Text = "997";
        tabs.SelectedIndex = 0;
        string copy = Path.Combine(temporary, "quantity-copy");
        Check(window.SaveCopy(copy), "Save Copy lost a quantity edit hidden behind the Convoy tab.");
        Check(EngageSave.Load(copy).ReadQuantityItems().Single(row => row.Definition.Id == "IID_マスタープルフ").Amount == 997,
            "Save Copy did not apply pending quantity input.");
        tabs.SelectedIndex = 1;
        search.Text = "IID_エンチャント専用プルフ";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 1, "Quantity search cannot find a DLC ID.");
        search.Text = "nothing-matches";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 0 && !window.FindControl<StackPanel>("QuantityItemForm")!.IsEnabled, "Empty search left an editable stale selection.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        tabs.SelectedIndex = 2;
        list.SelectedItem = list.Items.OfType<MainWindow.QuantityItemRow>().Single(row => row.Item.Definition.Id == "IID_てつの晶石");
        var iron = window.FindControl<NumericUpDown>("IronIngotsInput")!;
        var money = window.FindControl<NumericUpDown>("MoneyInput")!;
        money.Text = "12345";
        amount.Text = "9999";
        Check(window.ApplyQuantityItemValues() && iron.Value == 9999 && money.Text == "12345", "Material sync discarded unrelated Main edits.");
        Check(window.ApplyMainValues() && window.Save!.ReadMainValues().IronIngots == 9999, "Main overwrote the applied material quantity.");
        iron.Text = "25";
        Check(window.ApplyMainValues() && amount.Value == 25, "Main material edit did not refresh the quantity view.");
        amount.Text = "26";
        iron.Text = "27";
        Check(window.ApplyMainValues() && amount.Text == "26", "Main discarded a pending quantity edit.");
        Check(window.ApplyQuantityItemValues() && iron.Value == 26, "Explicit quantity edit did not win the shared-field conflict.");
        search.Text = "Iron";
        Dispatcher.UIThread.RunJobs();
        money.Text = "12346";
        Check(window.FillQuantityCategory(), "Category fill failed.");
        var filled = window.Save!.ReadQuantityItems();
        Check(filled.Where(row => row.Definition.Category == QuantityItemCategory.Materials).All(row => row.Amount == row.Definition.Maximum)
            && filled.Single(row => row.Definition.Id == "IID_マスタープルフ").Amount == 997,
            "Fill applied only to search results or changed another category.");
        Check(iron.Value == 9999 && money.Text == "12346", "Category fill did not synchronize ingots or discarded unrelated edits.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        tabs.SelectedIndex = 5;
        byte[] beforeKeyFill = window.Save.Serialize();
        Check(!window.FindControl<Button>("FillQuantityCategoryButton")!.IsVisible && !window.FillQuantityCategory()
            && window.Save.Serialize().AsSpan().SequenceEqual(beforeKeyFill), "Key items allow unsafe bulk filling.");
        tabs.SelectedIndex = 1;
        amount.Text = "1000";
        tabs.SelectedIndex = 2;
        Check(tabs.SelectedIndex == 1 && amount.Text == "1000", "A category switch lost invalid pending input.");
        amount.Text = "997";
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI quantity tests changed the source save.");
        string overflowSource = Path.Combine(temporary, "quantity-overflow");
        byte[] overflow = QuantityItemTests.Fixture("overflow");
        File.WriteAllBytes(overflowSource, overflow);
        Check(window.LoadSave(overflowSource) && amount.Text == "1000", "Loading an existing cheat value clamped the UI draft.");
        string overflowCopy = Path.Combine(temporary, "quantity-overflow-copy");
        Check(window.SaveCopy(overflowCopy) && File.ReadAllBytes(overflowCopy).AsSpan().SequenceEqual(overflow),
            "Saving an unedited cheat quantity changed its bytes.");
        tabs.SelectedIndex = 0;
        Console.WriteLine("Quantity GUI: category tabs, safe bulk filling, search, nine languages, drafts, bounds and Main sync passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
