using Avalonia.Controls;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record EngravingChoice(uint? Hash, string Label);

    private static void RefreshEngravingChoices(ComboBox input, uint? itemHash, uint? selected)
    {
        var choices = new List<EngravingChoice> { new(null, UiLanguage.Get("None")) };
        bool eligible = itemHash is uint weapon && EngravingCatalog.CanEngrave(weapon);
        foreach (var engraving in EngravingCatalog.Engravings)
        {
            bool current = selected is uint hash && EngravingCatalog.Find(hash)?.Id == engraving.Id;
            if (eligible || current)
                choices.Add(new(current ? selected : engraving.Hash, engraving.Name(UiLanguage.Current)));
        }
        if (selected is uint unknown && EngravingCatalog.Find(unknown) is null)
            choices.Add(new(unknown, $"0x{unknown:X8}"));
        input.ItemsSource = choices;
        input.SelectedItem = choices.First(row => row.Hash == selected);
        input.IsEnabled = eligible || selected.HasValue;
    }

    private static string? SelectedEngravingId(ComboBox input)
    {
        var selected = input.SelectedItem as EngravingChoice ?? throw new ArgumentException(UiLanguage.Get("SelectItem"));
        if (selected.Hash is not uint hash) return null;
        return EngravingCatalog.Find(hash)?.Id
            ?? throw new ArgumentException("An unknown engraving can only be preserved or cleared.");
    }

    private static bool EngravingChanged(ComboBox input, InventoryItem? baseline) =>
        input.SelectedItem is EngravingChoice choice && choice.Hash != baseline?.EngravingHash;
}
