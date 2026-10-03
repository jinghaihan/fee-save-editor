using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record MissingCharacterChoice(string Id, string Label);
    public sealed record ForceChoice(UnitForce Force, string Label);

    private void RefreshRosterManagement()
    {
        string? selected = (MissingRosterCharacterInput.SelectedItem as MissingCharacterChoice)?.Id;
        UnitForce? selectedForce = (RosterForceInput.SelectedItem as ForceChoice)?.Force;
        var choices = Array.Empty<MissingCharacterChoice>();
        if (Save is not null && CanEditRoster)
        {
            try
            {
                choices = Save.ReadMissingRosterCharacters().Select(person => new MissingCharacterChoice(person.Id, person.Name(UiLanguage.Current)))
                    .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
            }
            catch (Exception error) when (IsFileError(error)) { }
        }
        MissingRosterCharacterInput.ItemsSource = choices;
        MissingRosterCharacterInput.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices.FirstOrDefault();
        AddRosterCharacterButton.IsEnabled = choices.Length > 0;
        var character = SelectedCharacter;
        var forces = new[] { UnitForce.Absent, UnitForce.Dead, UnitForce.Lost }
            .Select(force => new ForceChoice(force, UiLanguage.Get("Force" + force))).ToArray();
        RosterForceInput.ItemsSource = forces;
        RosterForceInput.SelectedItem = forces.FirstOrDefault(choice => choice.Force == (selectedForce ?? character?.Force));
        RosterForceInput.IsEnabled = MoveRosterCharacterButton.IsEnabled = character is not null && Save!.CanMoveRosterCharacter(character.Index);
        DeleteRosterCharacterButton.IsEnabled = character is not null && Save!.CanDeleteRosterCharacter(character.Index);
    }

    public bool AddRosterCharacter(string personId)
    {
        bool success = EditRoster(save =>
        {
            if (SelectedCharacter is not null) save = PendingCharacterValues(save);
            return PendingEmblemValues(save).WithAddedRosterCharacter(personId);
        }, refresh: true, requireCharacter: false, selectPerson: ItemCatalog.Hash(personId));
        if (success)
        {
            AddRosterCharacterButton.Flyout?.Hide();
            RefreshEmblemRecords(preserveEditor: false);
        }
        return success;
    }

    public bool MoveSelectedRosterCharacter(UnitForce force)
    {
        if (SelectedCharacter is not { } character) return false;
        return EditRoster(save => PendingEmblemValues(PendingCharacterValues(save)).WithRosterForce(character.Index, force),
            refresh: true, selectPerson: character.PersonHash);
    }

    public bool DeleteSelectedRosterCharacter()
    {
        if (SelectedCharacter is not { } character) return false;
        bool success = EditRoster(save => PendingEmblemValues(PendingCharacterValues(save)).WithoutRosterCharacter(character.Index), refresh: true);
        if (success) DeleteRosterCharacterButton.Flyout?.Hide();
        return success;
    }

    private void AddRosterCharacter_Click(object? sender, RoutedEventArgs e)
    {
        if (MissingRosterCharacterInput.SelectedItem is MissingCharacterChoice choice) AddRosterCharacter(choice.Id);
    }
    private void MoveRosterCharacter_Click(object? sender, RoutedEventArgs e)
    {
        if (RosterForceInput.SelectedItem is ForceChoice choice) MoveSelectedRosterCharacter(choice.Force);
    }
    private void DeleteRosterCharacter_Click(object? sender, RoutedEventArgs e) => DeleteSelectedRosterCharacter();
}
