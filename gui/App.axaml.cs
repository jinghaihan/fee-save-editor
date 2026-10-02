using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        UiLanguage.Apply("en");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            Dispatcher.UIThread.Post(MacDockIcon.Apply);
            if (desktop.Args is [var path])
                window.LoadSave(path);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
