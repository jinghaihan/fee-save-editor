using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow : Window
{
    public EngageSave? Save { get; private set; }
    private string? _path;

    public MainWindow() => InitializeComponent();

    public bool LoadSave(string path)
    {
        try
        {
            // Validate first. An invalid file must not replace the loaded save.
            var save = EngageSave.Load(path);
            Save = save;
            _path = Path.GetFullPath(path);
            SectionSearch.Clear();
            RefreshOverview();
            RefreshSections();
            SaveCopyMenu.IsEnabled = true;
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("OpenFailed", error.Message);
            return false;
        }
    }

    public bool SaveCopy(string path)
    {
        if (Save is null)
            return false;
        try
        {
            Save.WriteCopy(path);
            ShowMessage("CopySaved", Path.GetFullPath(path));
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("SaveFailed", error.Message);
            return false;
        }
    }

    public void SetLanguage(string language)
    {
        UiLanguage.Apply(language);
        RefreshOverview();
        Message.IsVisible = false;
    }

    private void RefreshOverview()
    {
        if (Save is null)
            return;
        EmptyState.IsVisible = false;
        OverviewFields.IsVisible = true;
        FileNameValue.Text = Path.GetFileName(_path);
        KindValue.Text = UiLanguage.Get(Save.Kind == SaveKind.Game ? "GameSave" : "GlobalSave");
        FileSizeValue.Text = Save.Length.ToString("N0");
        FormatValue.Text = Save.FormatVersion?.ToString() ?? "—";
        GameVersionValue.Text = Save.GameVersion.HasValue ? $"0x{Save.GameVersion:X}" : "—";
        ChecksumValue.Text = $"{Save.Checksum:X8}";
        SectionCountValue.Text = Save.Sections.Count.ToString();
    }

    private void RefreshSections()
    {
        if (Save is null)
            return;
        string query = SectionSearch.Text?.Trim() ?? "";
        string? selected = SectionList.SelectedItem as string;
        string[] names = Save.Sections.Select(section => section.Name)
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        SectionList.ItemsSource = names;
        SectionList.SelectedItem = names.Contains(selected) ? selected : names.FirstOrDefault();
        RefreshSelectedSection();
    }

    private void RefreshSelectedSection()
    {
        var section = Save?.Sections.FirstOrDefault(section => section.Name == SectionList.SelectedItem as string);
        SectionFields.IsVisible = section is not null;
        if (section is null)
            return;
        SectionNameValue.Text = section.Name;
        OffsetValue.Text = $"0x{section.Offset:X}";
        PayloadSizeValue.Text = section.Length.ToString("N0");
    }

    private async void OpenSave_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiLanguage.Get("OpenSave"), AllowMultiple = false
            });
            if (files.Count == 0)
                return;
            string? path = files[0].TryGetLocalPath();
            if (path is null)
                ShowMessage("OpenFailed", UiLanguage.Get("LocalFilesOnly"));
            else
                LoadSave(path);
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("OpenFailed", error.Message);
        }
    }

    private async void SaveCopy_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiLanguage.Get("SaveCopy"), SuggestedFileName = $"{Path.GetFileName(_path)}-copy",
                ShowOverwritePrompt = true
            });
            if (file is null)
                return;
            string? path = file.TryGetLocalPath();
            if (path is null)
                ShowMessage("SaveFailed", UiLanguage.Get("LocalFilesOnly"));
            else
                SaveCopy(path);
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("SaveFailed", error.Message);
        }
    }

    private void ShowMessage(string key, string detail)
    {
        Message.Text = $"{UiLanguage.Get(key)} {detail}";
        Message.IsVisible = true;
    }

    private static bool IsFileError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException;

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private void English_Click(object? sender, RoutedEventArgs e) => SetLanguage("en");
    private void Chinese_Click(object? sender, RoutedEventArgs e) => SetLanguage("zh-Hans");
    private void SectionSearch_Changed(object? sender, TextChangedEventArgs e) => RefreshSections();
    private void SectionList_Changed(object? sender, SelectionChangedEventArgs e) => RefreshSelectedSection();
}
