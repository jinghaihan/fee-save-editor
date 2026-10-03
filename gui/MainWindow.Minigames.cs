using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;
using SukiUI.Controls;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    private sealed record MinigameEditor(MinigameRecordDefinition Definition, TextBlock Label,
        NumericUpDown Value, NumericUpDown? Size, ComboBox? Rank);
    public bool CanEditMinigames { get; private set; }
    private readonly Dictionary<string, MinigameEditor> _minigameEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _minigameTitles = new(StringComparer.Ordinal);
    private readonly List<(TextBlock Label, string Key)> _minigameHeadings = [];

    public void ShowMinigames()
    {
        MainPanel.IsVisible = ItemsPanel.IsVisible = RosterPanel.IsVisible = InspectorPanel.IsVisible = false;
        EmblemsPanel.IsVisible = BondRingsPanel.IsVisible = EmblemPagesPanel.IsVisible = false;
        SupportsPanel.IsVisible = AchievementsPanel.IsVisible = false;
        MinigamesPanel.IsVisible = true;
        MainNavigation.SelectedIndex = 5;
        RefreshPageLayout();
    }

    private void BuildMinigameCards()
    {
        if (_minigameEditors.Count != 0) return;
        foreach (var group in MinigameCatalog.Groups)
        {
            var title = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            _minigameTitles.Add(group.Id, title);
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(title);
            bool fishing = group.Id == "Fishing", ranked = group.Records[0].RankKey is not null;
            string columns = "*,*";
            if (fishing) columns = "2*,*,*,*";
            else if (ranked) columns = "*,*,*";
            var rows = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(columns),
                ColumnSpacing = 24, RowSpacing = 16
            };
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddHeading(fishing ? "Caught" : "BestScore", 1);
            if (fishing) AddHeading("BestSize", 2);
            if (ranked) AddHeading(fishing ? "SizeRank" : "BestRank", fishing ? 3 : 2);
            foreach (var definition in group.Records)
            {
                int row = rows.RowDefinitions.Count;
                rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                var value = CreateMinigameNumber(definition.Key);
                NumericUpDown? size = fishing ? CreateMinigameNumber(definition.Key + "Size") : null;
                ComboBox? rank = ranked ? new ComboBox
                {
                    Tag = definition.Key + "Rank", Height = 42, Margin = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false
                } : null;
                Add(label, row, 0);
                Add(value, row, 1);
                if (size is not null) Add(size, row, 2);
                if (rank is not null) Add(rank, row, fishing ? 3 : 2);
                _minigameEditors.Add(definition.Key, new(definition, label, value, size, rank));
            }
            content.Children.Add(rows);
            var card = new GlassCard { Name = "Minigame" + group.Id + "Card", Padding = new Thickness(20), Content = content };
            var target = group.Id switch
            {
                "Fishing" => FishingMinigameCards,
                "SitUps" or "WyvernRide" => MinigameRightCards,
                _ => MinigameLeftCards
            };
            target.Children.Add(card);

            void AddHeading(string key, int column)
            {
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
                _minigameHeadings.Add((label, key));
                Add(label, 0, column);
            }
            void Add(Control control, int row, int column)
            {
                Grid.SetRow(control, row);
                Grid.SetColumn(control, column);
                rows.Children.Add(control);
            }
        }
    }

    private static NumericUpDown CreateMinigameNumber(string key) => new()
    {
        Tag = key, Minimum = 0, Maximum = int.MaxValue, Height = 42, Margin = new Thickness(0),
        FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false
    };

    private void LoadMinigames()
    {
        CanEditMinigames = false;
        foreach (var editor in _minigameEditors.Values)
        {
            editor.Value.IsEnabled = false;
            SetMinigameNumber(editor.Value, null);
            if (editor.Size is not null) { editor.Size.IsEnabled = false; SetMinigameNumber(editor.Size, null); }
            if (editor.Rank is not null) { editor.Rank.IsEnabled = false; editor.Rank.SelectedIndex = -1; }
        }
        if (Save is null || !CanEditMain) return;
        try
        {
            var records = Save.ReadMinigameRecords();
            foreach (var record in records)
            {
                var editor = _minigameEditors[record.Definition.Key];
                SetMinigameNumber(editor.Value, record.Value);
                editor.Value.IsEnabled = true;
                if (editor.Size is not null) { SetMinigameNumber(editor.Size, record.BestSize); editor.Size.IsEnabled = true; }
                if (editor.Rank is not null) { editor.Rank.SelectedIndex = record.Rank ?? -1; editor.Rank.IsEnabled = true; }
            }
            CanEditMinigames = true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("MinigamesUnavailable", error.Message); }
    }

    private void RefreshMinigameLanguage()
    {
        BuildMinigameCards();
        foreach (var group in MinigameCatalog.Groups) _minigameTitles[group.Id].Text = group.Name(UiLanguage.Current);
        foreach (var (label, key) in _minigameHeadings) label.Text = UiLanguage.Get(key);
        foreach (var editor in _minigameEditors.Values)
        {
            string name = editor.Definition.Name(UiLanguage.Current);
            editor.Label.Text = name;
            AutomationProperties.SetName(editor.Value, name + " — " + UiLanguage.Get(editor.Definition.IsFishing ? "Caught" : "BestScore"));
            if (editor.Size is not null) AutomationProperties.SetName(editor.Size, name + " — " + UiLanguage.Get("BestSize"));
            if (editor.Rank is not null)
            {
                int selected = editor.Rank.SelectedIndex;
                editor.Rank.ItemsSource = editor.Definition.Ranks.Select(label =>
                    editor.Definition.IsFishing || label == "None" ? UiLanguage.Get(label) : label).ToArray();
                editor.Rank.SelectedIndex = selected;
                AutomationProperties.SetName(editor.Rank, name + " — " + UiLanguage.Get(editor.Definition.IsFishing ? "SizeRank" : "BestRank"));
            }
        }
    }

    private static void SetMinigameNumber(NumericUpDown input, int? value)
    {
        input.Value = value;
        input.Text = value?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private Dictionary<string, MinigameValues> ReadMinigameInputs()
    {
        var values = new Dictionary<string, MinigameValues>(StringComparer.Ordinal);
        foreach (var (key, editor) in _minigameEditors)
        {
            if (editor.Rank is not null && editor.Rank.SelectedIndex < 0)
                throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
            values.Add(key, new(Amount(editor.Value), editor.Rank?.SelectedIndex,
                editor.Size is null ? null : Amount(editor.Size)));
        }
        return values;
    }
}
