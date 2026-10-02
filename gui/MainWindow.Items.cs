using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record ItemChoice(ItemDefinition Definition, string Name);
    public sealed record InventoryRow(int Slot, string Label);
    public bool CanEditInventory { get; private set; }
    private int? _selectedSlot;
    private bool _refreshingItems;

    public void ShowItems()
    {
        MainPanel.IsVisible = false;
        InspectorPanel.IsVisible = false;
        ItemsPanel.IsVisible = true;
        RosterPanel.IsVisible = false;
        ApplyMainButton.IsVisible = false;
        MainNavigation.SelectedIndex = 1;
        RefreshPageTitle();
    }

    private void LoadInventory()
    {
        CanEditInventory = false;
        ItemEditorInputs.IsEnabled = false;
        RestoreAllUsesButton.IsEnabled = false;
        _selectedSlot = null;
        InventoryList.ItemsSource = Array.Empty<InventoryRow>();
        ConvoyCount.Text = "";
        ItemSearch.Clear();
        ItemTypeSearch.Clear();
        if (Save is null || Save.Kind != SaveKind.Game)
            return;
        try
        {
            Save.ReadInventory();
            CanEditInventory = true;
            ItemEditorInputs.IsEnabled = true;
            RestoreAllUsesButton.IsEnabled = true;
            RefreshInventory(selectEditor: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            // Keep the Main panel available when only the convoy layout is unsupported.
            ConvoyCount.Text = UiLanguage.Get("Unavailable");
        }
    }

    private void RefreshInventory(bool selectEditor)
    {
        if (!CanEditInventory || Save is null)
            return;
        var slots = Save.ReadInventory();
        ConvoyCount.Text = $"{slots.Count(entry => entry.Item is not null)} / {slots.Count}";
        string query = ItemSearch.Text?.Trim() ?? "";
        var rows = slots.Where(entry => entry.Item is not null).Select(entry =>
            new InventoryRow(entry.Slot, ItemLabel(entry.Item!)))
            .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _refreshingItems = true;
        InventoryList.ItemsSource = rows;
        InventoryList.SelectedItem = rows.FirstOrDefault(row => row.Slot == _selectedSlot) ?? rows.FirstOrDefault();
        _refreshingItems = false;
        _selectedSlot = (InventoryList.SelectedItem as InventoryRow)?.Slot;
        if (selectEditor)
            SelectInventoryItem();
    }

    private static string ItemLabel(InventoryItem item)
    {
        var definition = ItemCatalog.Find(item.ItemHash);
        string name = definition?.Name(UiLanguage.Current) ?? RosterCatalog.Item(item.ItemHash)?.Name(UiLanguage.Current)
            ?? $"{UiLanguage.Get("UnknownItem")} (0x{item.ItemHash:X8})";
        if (item.RefineLevel != 0)
            name += $" +{item.RefineLevel}";
        if (definition is { UnlimitedUses: false })
            name += $" · {item.Uses}/{definition.MaxUses}";
        return name;
    }

    private void RefreshItemChoices(uint? selectedHash = null, bool selectFirst = true)
    {
        selectedHash ??= (ItemTypeInput.SelectedItem as ItemChoice)?.Definition.Hash;
        string query = ItemTypeSearch.Text?.Trim() ?? "";
        var choices = ItemCatalog.Items.Select(item => new ItemChoice(item, item.Name(UiLanguage.Current)))
            .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Definition.Hash == selectedHash)
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _refreshingItems = true;
        ItemTypeInput.ItemsSource = choices;
        ItemTypeInput.SelectedItem = choices.FirstOrDefault(item => item.Definition.Hash == selectedHash);
        if (ItemTypeInput.SelectedItem is null && selectFirst)
            ItemTypeInput.SelectedItem = choices.FirstOrDefault();
        _refreshingItems = false;
    }

    private void SelectInventoryItem()
    {
        if (!CanEditInventory || Save is null)
            return;
        var item = _selectedSlot.HasValue ? Save.ReadInventory()[_selectedSlot.Value].Item : null;
        RefreshItemChoices(item?.ItemHash);
        var definition = item is null ? (ItemTypeInput.SelectedItem as ItemChoice)?.Definition : ItemCatalog.Find(item.ItemHash);
        if (item is not null && definition is null)
        {
            _refreshingItems = true;
            ItemTypeInput.SelectedIndex = -1;
            _refreshingItems = false;
        }
        SetItemRanges(definition);
        ItemUsesInput.Text = (item?.Uses ?? definition?.MaxUses ?? 1).ToString();
        ItemRefineInput.Text = (item?.RefineLevel ?? 0).ToString();
        ItemEngravingValue.Text = item?.EngravingHash is uint hash ? $"0x{hash:X8}" : UiLanguage.Get("None");
        ApplyItemButton.IsEnabled = item is not null && definition is not null;
        DeleteItemButton.IsEnabled = item is not null;
        AddItemButton.IsEnabled = definition is not null;
    }

    private void SetItemRanges(ItemDefinition? definition)
    {
        ItemUsesInput.Maximum = definition?.MaxUses ?? 255;
        ItemUsesInput.IsVisible = definition is not { UnlimitedUses: true };
        UnlimitedUsesLabel.IsVisible = definition is { UnlimitedUses: true };
        RestoreUsesButton.IsEnabled = definition is { UnlimitedUses: false };
        ItemRefineInput.Maximum = definition?.MaxRefine ?? 0;
        ItemRefineInput.IsEnabled = definition is { MaxRefine: > 0 };
    }

    private bool HasPendingItemValues()
    {
        if (!CanEditInventory || Save is null || !_selectedSlot.HasValue
            || ItemTypeInput.SelectedItem is not ItemChoice choice)
            return false;
        var item = Save.ReadInventory()[_selectedSlot.Value].Item;
        return item is not null && (item.ItemHash != choice.Definition.Hash
            || (!choice.Definition.UnlimitedUses && ItemUsesInput.Text != item.Uses.ToString())
            || ItemRefineInput.Text != item.RefineLevel.ToString());
    }

    public bool ApplyItemValues()
    {
        if (!CanEditInventory || Save is null || !_selectedSlot.HasValue)
            return false;
        return EditInventory(() =>
        {
            var choice = ItemTypeInput.SelectedItem as ItemChoice ?? throw new ArgumentException(UiLanguage.Get("SelectItem"));
            int uses = choice.Definition.UnlimitedUses ? 255 : Amount(ItemUsesInput);
            int refine = Amount(ItemRefineInput);
            return Save.WithInventoryItem(_selectedSlot.Value, choice.Definition.Id, uses, refine);
        });
    }

    public bool AddSelectedItem() => EditInventory(() =>
    {
        var choice = ItemTypeInput.SelectedItem as ItemChoice
            ?? throw new ArgumentException(UiLanguage.Get("SelectItem"));
        int uses = choice.Definition.UnlimitedUses ? 255 : Amount(ItemUsesInput);
        int refine = Amount(ItemRefineInput);
        int? empty = Save!.ReadInventory().FirstOrDefault(slot => slot.Item is null)?.Slot;
        var edited = Save.AddInventoryItem(choice.Definition.Id, uses, refine);
        ItemSearch.Clear();
        _selectedSlot = empty;
        return edited;
    });

    public bool DeleteSelectedItem() => EditInventory(() => _selectedSlot.HasValue
        ? Save!.DeleteInventoryItem(_selectedSlot.Value) : throw new ArgumentException(UiLanguage.Get("SelectItem")));

    public bool RestoreAllItemUses() => EditInventory(() => Save!.RestoreInventoryUses());

    private bool EditInventory(Func<EngageSave> edit)
    {
        if (!CanEditInventory || Save is null)
            return false;
        try
        {
            Save = edit();
            RefreshOverview();
            RefreshSections();
            RefreshInventory(selectEditor: true);
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void RefreshInventoryLanguage()
    {
        RefreshItemChoices(selectFirst: false);
        int? selected = _selectedSlot;
        RefreshInventory(selectEditor: false);
        if (selected != _selectedSlot)
            SelectInventoryItem();
        if (_selectedSlot.HasValue && Save?.ReadInventory()[_selectedSlot.Value].Item?.EngravingHash is null)
            ItemEngravingValue.Text = UiLanguage.Get("None");
    }

    private void ItemSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        int? selected = _selectedSlot;
        RefreshInventory(selectEditor: false);
        if (selected != _selectedSlot)
            SelectInventoryItem();
    }
    private void ItemTypeSearch_Changed(object? sender, TextChangedEventArgs e) => RefreshItemChoices(selectFirst: false);
    private void InventoryList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingItems)
            return;
        _selectedSlot = (InventoryList.SelectedItem as InventoryRow)?.Slot;
        SelectInventoryItem();
    }
    private void ItemTypeInput_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingItems)
            return;
        var definition = (ItemTypeInput.SelectedItem as ItemChoice)?.Definition;
        SetItemRanges(definition);
        ItemUsesInput.Text = (definition?.MaxUses ?? 1).ToString();
        ItemRefineInput.Text = "0";
        AddItemButton.IsEnabled = definition is not null;
        ApplyItemButton.IsEnabled = definition is not null && _selectedSlot.HasValue;
    }
    private void ApplyItem_Click(object? sender, RoutedEventArgs e) => ApplyItemValues();
    private void AddItem_Click(object? sender, RoutedEventArgs e) => AddSelectedItem();
    private void DeleteItem_Click(object? sender, RoutedEventArgs e) => DeleteSelectedItem();
    private void RestoreAllUses_Click(object? sender, RoutedEventArgs e) => RestoreAllItemUses();
    private void RestoreUses_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemTypeInput.SelectedItem is ItemChoice choice)
            ItemUsesInput.Text = choice.Definition.MaxUses.ToString();
    }
}
