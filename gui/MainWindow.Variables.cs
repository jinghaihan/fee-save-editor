using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    private string? _selectedVariable;
    private int _variableBaseline;
    private bool _refreshingVariables;
    public bool CanEditVariables { get; private set; }

    private void LoadVariables()
    {
        CanEditVariables = VariableForm.IsEnabled = false;
        _selectedVariable = null;
        VariableKeyValue.Text = "";
        VariableValueInput.Value = null;
        VariableList.ItemsSource = Array.Empty<string>();
        if (Save is null || Save.Kind != SaveKind.Game) return;
        try { Save.ReadGameVariables(); CanEditVariables = true; RefreshVariables(); }
        catch (Exception error) when (IsFileError(error))
        {
            if (Save.Sections.Any(section => section.Name == "USER")) ShowMessage("VariablesUnavailable", error.Message);
        }
    }

    private void RefreshVariables()
    {
        if (!CanEditVariables || Save is null) return;
        string query = VariableSearch.Text?.Trim() ?? "";
        var keys = Save.ReadGameVariables().Select(row => row.Key)
            .Where(key => key.Contains(query, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        _refreshingVariables = true;
        VariableList.ItemsSource = keys;
        VariableList.SelectedItem = keys.FirstOrDefault(key => key == _selectedVariable) ?? keys.FirstOrDefault();
        _refreshingVariables = false;
        SelectVariable();
    }

    private void SelectVariable()
    {
        _selectedVariable = VariableList.SelectedItem as string;
        var variable = Save?.ReadGameVariables().FirstOrDefault(row => row.Key == _selectedVariable);
        VariableForm.IsEnabled = variable is not null;
        VariableKeyValue.Text = variable?.Key ?? "";
        _variableBaseline = variable?.Value ?? 0;
        VariableValueInput.Value = variable?.Value;
    }

    private bool HasPendingVariable() => _selectedVariable is not null
        && VariableValueInput.Text != _variableBaseline.ToString(VariableValueInput.NumberFormat);

    public bool ApplyVariableValue()
    {
        if (!CanEditVariables || Save is null || _selectedVariable is null) return false;
        var original = Save;
        try
        {
            int value = Amount(VariableValueInput);
            if (!HasPendingVariable()) return true;
            if (!ApplyPendingGameplayValues())
            {
                Save = original;
                return false;
            }
            Save = Save.WithGameVariable(_selectedVariable, value);
            _variableBaseline = value;
            RefreshGameplayValues();
            ShowPage(inspector: true);
            InspectorPages.SelectedIndex = 1;
            RefreshOverview();
            RefreshSections();
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            Save = original;
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void VariableList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingVariables || !CanEditVariables) return;
        if (HasPendingVariable() && !ApplyVariableValue())
        {
            _refreshingVariables = true;
            VariableList.SelectedItem = _selectedVariable;
            _refreshingVariables = false;
            return;
        }
        SelectVariable();
    }

    private void VariableSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_refreshingVariables || !CanEditVariables) return;
        if (HasPendingVariable() && !ApplyVariableValue()) return;
        RefreshVariables();
    }
    private void ApplyVariable_Click(object? sender, RoutedEventArgs e) => ApplyVariableValue();
    private void InspectorPages_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (InspectorOverviewPanel is null || VariablesPanel is null) return;
        InspectorOverviewPanel.IsVisible = InspectorPages.SelectedIndex == 0;
        VariablesPanel.IsVisible = InspectorPages.SelectedIndex == 1;
        if (VariablesPanel.IsVisible && CanEditVariables && !HasPendingVariable()) RefreshVariables();
    }
}
