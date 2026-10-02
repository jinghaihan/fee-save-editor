using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FeeEditor.Gui;

AppBuilder.Configure<App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var window = new MainWindow();
window.Show();
Dispatcher.UIThread.RunJobs();
using var frame = window.CaptureRenderedFrame();
if (frame is null || frame.PixelSize.Width == 0)
    throw new InvalidOperationException("GUI did not render.");
window.Close();
Console.WriteLine("GUI smoke test passed.");
