using Avalonia.Interactivity;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public bool RemoveSelectedEmblem()
    {
        if (Save is null || _emblemPage != EmblemPage.Bonds || SelectedEmblemCondition is not { } selected) return false;
        try
        {
            var edited = PendingEmblemValues(Save);
            if (CanEditRoster && SelectedCharacter is not null) edited = PendingCharacterValues(edited);
            Save = edited.WithoutEmblem(selected.InstanceId);
            RefreshRoster(selectEditor: true);
            RefreshMissingEmblems();
            RefreshEmblemRecords(preserveEditor: false);
            RefreshOverview(); RefreshSections();
            RemoveEmblemButton.Flyout?.Hide();
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("EditFailed", error.Message); return false; }
    }

    private void RemoveEmblem_Click(object? sender, RoutedEventArgs e) => RemoveSelectedEmblem();
}
