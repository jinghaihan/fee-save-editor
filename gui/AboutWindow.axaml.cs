using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FeeEditor.Gui;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/jinghaihan/fee-save-editor";

    public AboutWindow()
    {
        InitializeComponent();
        VersionValue.Text = GetVersion();
    }

    public static string GetVersion() =>
        typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private async void OpenProject_Click(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(ProjectUrl));
}
