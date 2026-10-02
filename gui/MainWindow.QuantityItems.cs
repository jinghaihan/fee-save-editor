using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record QuantityItemRow(QuantityItem Item, string Label);
    public bool CanEditQuantityItems { get; private set; }
    private bool _refreshingQuantityItems;
    private QuantityItem? _quantityBaseline;
    private string? _selectedQuantityId;
    private QuantityItemCategory _quantityCategory;

    private void LoadQuantityItems()
    {
        CanEditQuantityItems = false;
        FillQuantityCategoryButton.IsEnabled = false;
        QuantityItemForm.IsEnabled = false;
        _quantityBaseline = null;
        _selectedQuantityId = null;
        QuantityItemList.ItemsSource = Array.Empty<QuantityItemRow>();
        QuantityItemName.Text = "";
        QuantityAmountInput.Value = null;
        QuantitySearch.Clear();
        if (Save is null || Save.Kind != SaveKind.Game) return;
        try
        {
            Save.ReadQuantityItems();
            CanEditQuantityItems = true;
            RefreshQuantityItems(selectEditor: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            QuantityItemName.Text = UiLanguage.Get("Unavailable");
        }
    }

    private void RefreshQuantityItems(bool selectEditor)
    {
        if (!CanEditQuantityItems || Save is null) return;
        string query = QuantitySearch.Text?.Trim() ?? "";
        var rows = Save.ReadQuantityItems().Where(row => row.Definition.Category == _quantityCategory)
            .Select(row => new QuantityItemRow(row, $"{row.Definition.Name(UiLanguage.Current)} · ×{row.Amount}"))
            .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Item.Definition.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _refreshingQuantityItems = true;
        QuantityItemList.ItemsSource = rows;
        QuantityItemList.SelectedItem = rows.FirstOrDefault(row => row.Item.Definition.Id == _selectedQuantityId) ?? rows.FirstOrDefault();
        _refreshingQuantityItems = false;
        string? selected = (QuantityItemList.SelectedItem as QuantityItemRow)?.Item.Definition.Id;
        bool changed = selected != _selectedQuantityId;
        _selectedQuantityId = selected;
        if (selectEditor || changed) SelectQuantityItem();
        FillQuantityCategoryButton.IsVisible = _quantityCategory != QuantityItemCategory.KeyItems;
        FillQuantityCategoryButton.IsEnabled = _quantityCategory != QuantityItemCategory.KeyItems;
    }

    private void SelectQuantityItem()
    {
        _quantityBaseline = (QuantityItemList.SelectedItem as QuantityItemRow)?.Item;
        QuantityItemForm.IsEnabled = _quantityBaseline is not null;
        QuantityItemName.Text = _quantityBaseline?.Definition.Name(UiLanguage.Current) ?? "";
        QuantityAmountInput.Maximum = _quantityBaseline?.Definition.Maximum ?? 999;
        QuantityAmountInput.Text = _quantityBaseline?.Amount.ToString() ?? "";
    }

    private bool HasPendingQuantityItemValues() => CanEditQuantityItems && _quantityBaseline is not null
        && QuantityAmountInput.Text != _quantityBaseline.Amount.ToString();

    public bool ApplyQuantityItemValues()
    {
        if (!CanEditQuantityItems || Save is null || _quantityBaseline is null) return false;
        try
        {
            int amount = Amount(QuantityAmountInput);
            Save = Save.WithQuantityItem(_quantityBaseline.Definition.Id, amount);
            // These fields are also editable on Main; keep both views of the same value in sync.
            var material = _quantityBaseline.Definition.Id switch
            {
                "IID_てつの晶石" => IronIngotsInput,
                "IID_はがねの晶石" => SteelIngotsInput,
                "IID_ぎんの晶石" => SilverIngotsInput,
                _ => null
            };
            if (material is not null && CanEditMain) material.Value = amount;
            RefreshQuantityItems(selectEditor: true);
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void RefreshQuantityItemLanguage()
    {
        QuantityListTitle.Text = UiLanguage.Get(_quantityCategory.ToString());
        RefreshQuantityItems(selectEditor: false);
        QuantityItemName.Text = _quantityBaseline?.Definition.Name(UiLanguage.Current) ?? "";
    }

    private void ItemPages_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (ConvoyPanel is null || QuantityItemsPanel is null) return;
        if (HasPendingQuantityItemValues() && !ApplyQuantityItemValues())
        {
            ItemPages.SelectedIndex = (int)_quantityCategory + 1;
            return;
        }
        ConvoyPanel.IsVisible = ItemPages.SelectedIndex == 0;
        QuantityItemsPanel.IsVisible = ItemPages.SelectedIndex > 0;
        if (ItemPages.SelectedIndex > 0)
        {
            _quantityCategory = (QuantityItemCategory)(ItemPages.SelectedIndex - 1);
            RefreshQuantityItemLanguage();
        }
    }

    public bool FillQuantityCategory()
    {
        if (!CanEditQuantityItems || Save is null || _quantityCategory == QuantityItemCategory.KeyItems) return false;
        try
        {
            Save = Save.FillQuantityItems(_quantityCategory);
            RefreshQuantityItems(selectEditor: true);
            if (_quantityCategory == QuantityItemCategory.Materials && CanEditMain)
            {
                var values = Save.ReadMainValues();
                IronIngotsInput.Value = values.IronIngots;
                SteelIngotsInput.Value = values.SteelIngots;
                SilverIngotsInput.Value = values.SilverIngots;
            }
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }
    private void FillQuantityCategory_Click(object? sender, RoutedEventArgs e) => FillQuantityCategory();
    private void QuantitySearch_Changed(object? sender, TextChangedEventArgs e) => RefreshQuantityItems(selectEditor: false);
    private void QuantityItemList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingQuantityItems) return;
        _selectedQuantityId = (QuantityItemList.SelectedItem as QuantityItemRow)?.Item.Definition.Id;
        SelectQuantityItem();
    }
    private void ApplyQuantityItem_Click(object? sender, RoutedEventArgs e) => ApplyQuantityItemValues();
}
