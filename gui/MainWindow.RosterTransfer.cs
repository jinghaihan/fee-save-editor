using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public bool ExportSelectedRosterCharacter(string path)
    {
        if (Save is null || SelectedCharacter is not { } character) return false;
        try
        {
            var edited = PendingCharacterValues(Save);
            if (HasPendingRosterItemValues()) edited = EditedCharacterItem(edited);
            RosterTransfer.WriteNew(path, edited.ExportRosterCharacter(character.Index));
            ShowMessage("CharacterExported", Path.GetFullPath(path));
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("ExportFailed", error.Message);
            return false;
        }
    }

    public bool ImportSelectedRosterCharacter(string path)
    {
        if (Save is null || SelectedCharacter is not { } character) return false;
        try
        {
            if (new FileInfo(path).Length > RosterTransfer.MaximumFileSize)
                throw new InvalidDataException("A character file must not exceed 1 MiB.");
            return EditRoster(save => save.ImportRosterCharacter(character.Index, File.ReadAllBytes(path)), refresh: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("ImportFailed", error.Message);
            return false;
        }
    }

    private static readonly FilePickerFileType CharacterFileType = new("FEE character JSON")
    {
        Patterns = ["*.fee-character.json"], MimeTypes = ["application/json"]
    };

    private async void ExportRosterCharacter_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedCharacter is not { } character) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiLanguage.Get("ExportCharacter"),
                SuggestedFileName = $"{RosterCatalog.Person(character.PersonHash)!.Name("en")}.fee-character.json",
                FileTypeChoices = [CharacterFileType], ShowOverwritePrompt = false
            });
            if (file is null) return;
            string path = file.TryGetLocalPath() ?? throw new ArgumentException(UiLanguage.Get("LocalFilesOnly"));
            ExportSelectedRosterCharacter(path);
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("ExportFailed", error.Message); }
    }

    private async void ImportRosterCharacter_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedCharacter is null) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiLanguage.Get("ImportCharacter"), AllowMultiple = false,
                FileTypeFilter = [CharacterFileType, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            string path = files[0].TryGetLocalPath() ?? throw new ArgumentException(UiLanguage.Get("LocalFilesOnly"));
            ImportSelectedRosterCharacter(path);
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("ImportFailed", error.Message); }
    }
}
