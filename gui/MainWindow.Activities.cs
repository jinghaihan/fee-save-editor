using Avalonia.Interactivity;
using FeeEditor.Core;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public bool CanEditActivities { get; private set; }

    private SomnielActivities ReadActivityInputs() => new(Amount(TrainingRemainingInput), Amount(ArenaRemainingInput));

    private void LoadActivities()
    {
        CanEditActivities = ActivityInputs.IsEnabled = RestoreActivitiesButton.IsEnabled = false;
        TrainingRemainingInput.Value = ArenaRemainingInput.Value = null;
        if (Save is null || !CanEditMain) return;
        try
        {
            var values = Save.ReadSomnielActivities();
            TrainingRemainingInput.Value = values.TrainingRemaining;
            ArenaRemainingInput.Value = values.ArenaRemaining;
            CanEditActivities = ActivityInputs.IsEnabled = RestoreActivitiesButton.IsEnabled = true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("ActivitiesUnavailable", error.Message);
        }
    }

    private void RestoreActivities_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditActivities) return;
        TrainingRemainingInput.Value = SomnielActivities.MaxTraining;
        ArenaRemainingInput.Value = SomnielActivities.MaxArena;
    }
}
