using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record EquipmentKindChoice(RosterEquipmentKind Kind, string Label);
    public sealed record EquipmentChoice(RosterEquipmentOption Option, string Label);
    public bool CanEditRosterEquipment { get; private set; }
    private RosterEquipmentSelection? _equipmentBaseline;
    private bool _refreshingEquipment;

    private RosterEquipmentSelection? EquipmentInput
    {
        get
        {
            if (RosterEquipmentType.SelectedItem is not EquipmentKindChoice type) return null;
            if (type.Kind == RosterEquipmentKind.None) return new(RosterEquipmentKind.None, 0);
            return (RosterEquipmentChoice.SelectedItem as EquipmentChoice)?.Option.Selection;
        }
    }

    private void RefreshRosterEquipment(bool preserveEdits)
    {
        if (RosterEquipmentForm is null) return;
        var pending = preserveEdits ? EquipmentInput : null;
        _refreshingEquipment = true;
        CanEditRosterEquipment = false;
        _equipmentBaseline = null;
        var character = SelectedCharacter;
        RosterEquipmentType.ItemsSource = Enum.GetValues<RosterEquipmentKind>().Select(kind =>
            new EquipmentKindChoice(kind, UiLanguage.Get(kind switch
            { RosterEquipmentKind.Emblem => "Emblem", RosterEquipmentKind.BondRing => "BondRings", _ => "None" }))).ToArray();
        try
        {
            if (Save is not null && character is not null)
            {
                _equipmentBaseline = Save.ReadRosterEquipment(character.Index);
                Save.ReadRosterEquipmentOptions(character.Index);
                CanEditRosterEquipment = character.Force is not UnitForce.Enemy and not UnitForce.Temporary
                    && RosterCatalog.Person(character.PersonHash) is not null
                    && Save.ReadCharacterRingLinks()[character.Index].PartnerEmblemInstance == 0;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            // An unsupported equipment section must not disable the other roster editors.
            CanEditRosterEquipment = false;
        }
        var selection = pending ?? _equipmentBaseline ?? new(RosterEquipmentKind.None, 0);
        RosterEquipmentType.SelectedItem = RosterEquipmentType.Items.Cast<EquipmentKindChoice>().First(choice => choice.Kind == selection.Kind);
        RosterEquipmentForm.IsEnabled = CanEditRosterEquipment;
        RefreshEquipmentChoices(selection);
        _refreshingEquipment = false;
    }

    private void RefreshEquipmentChoices(RosterEquipmentSelection? selected)
    {
        var type = (RosterEquipmentType.SelectedItem as EquipmentKindChoice)?.Kind ?? RosterEquipmentKind.None;
        RosterEquipmentChoiceField.IsVisible = type != RosterEquipmentKind.None;
        RosterEquipmentChoiceTitle.Text = UiLanguage.Get(type == RosterEquipmentKind.Emblem ? "Emblem" : "BondRings");
        IEnumerable<RosterEquipmentOption> options = CanEditRosterEquipment && Save is not null && SelectedCharacter is { } character
            ? Save.ReadRosterEquipmentOptions(character.Index).Where(option => option.Selection.Kind == type) : [];
        string query = RosterEquipmentSearch.Text?.Trim() ?? "";
        var choices = options.Select(option => new EquipmentChoice(option, EquipmentLabel(option)))
            .Where(choice => choice.Option.Selection == selected || choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        RosterEquipmentChoice.ItemsSource = choices;
        RosterEquipmentChoice.SelectedItem = choices.FirstOrDefault(choice => choice.Option.Selection == selected)
            ?? choices.FirstOrDefault();
        if (selected is not null && selected.Kind == type && selected.Kind != RosterEquipmentKind.None
            && choices.All(choice => choice.Option.Selection != selected))
            RosterEquipmentChoice.SelectedItem = null;
        UnequipRosterButton.IsEnabled = _equipmentBaseline is not null && _equipmentBaseline.Kind != RosterEquipmentKind.None;
    }

    private string EquipmentLabel(RosterEquipmentOption option)
    {
        if (option.Selection.Kind == RosterEquipmentKind.None) return UiLanguage.Get("None");
        string name = option.EmblemId is string gid ? EmblemCatalog.Emblem(gid)!.Name(UiLanguage.Current)
            : BondRingName(new(option.Selection.InstanceId, option.RingHash!.Value, option.StockCount, option.OwnerIndex));
        if (option.OwnerIndex is int owner)
            name += " · " + CharacterName(Save!.ReadRoster()[owner]);
        else if (option.Selection.Kind == RosterEquipmentKind.BondRing)
            name += $" · ×{option.StockCount}";
        return name;
    }

    private EngageSave PendingRosterEquipment(EngageSave save)
    {
        if (!CanEditRosterEquipment || _equipmentBaseline is null) return save;
        var selection = EquipmentInput ?? throw new ArgumentException("Select existing equipment.");
        if (selection == _equipmentBaseline) return save;
        // Apply pending ring-stock/bond edits before an equipment transfer changes their owner or instance.
        return PendingEmblemValues(save).WithRosterEquipment(_selectedCharacter!.Value, selection);
    }

    public bool ApplyRosterEquipment() => EditRoster(save => PendingCharacterValues(save), refresh: true);

    public bool UnequipRosterEquipment() => EditRoster(save => PendingEmblemValues(PendingCharacterValuesWithoutEquipment(save))
        .WithRosterEquipment(_selectedCharacter!.Value, new(RosterEquipmentKind.None, 0)), refresh: true);

    private void RosterEquipmentType_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEquipment) return;
        var baseline = _equipmentBaseline?.Kind == (RosterEquipmentType.SelectedItem as EquipmentKindChoice)?.Kind ? _equipmentBaseline : null;
        _refreshingEquipment = true;
        RosterEquipmentSearch.Clear();
        _refreshingEquipment = false;
        RefreshEquipmentChoices(baseline);
    }

    private void RosterEquipmentSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_refreshingEquipment) RefreshEquipmentChoices(EquipmentInput);
    }
    private void ApplyRosterEquipment_Click(object? sender, RoutedEventArgs e) => ApplyRosterEquipment();
    private void UnequipRoster_Click(object? sender, RoutedEventArgs e) => UnequipRosterEquipment();
}
