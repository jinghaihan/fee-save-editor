using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Buffers.Binary;
using FeeEditor.Core;
using FeeEditor.Gui;
using FeeEditor.Gui.Localization;

AppBuilder.Configure<App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var window = new MainWindow();
window.Show();
Dispatcher.UIThread.RunJobs();
Check(UiLanguage.Current == "en", "The default UI language is not English.");
Check(UiLanguage.Read("en").Keys.Order().SequenceEqual(UiLanguage.Read("zh-Hans").Keys.Order()),
    "English and Chinese resource keys differ.");
Check(!window.FindControl<MenuItem>("SaveCopyMenu")!.IsEnabled, "Copy is enabled without a save.");

string temporary = Path.Combine(Path.GetTempPath(), $"fee-gui-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporary);
try
{
    byte[] original = Fixture();
    string source = Path.Combine(temporary, "Manual0");
    File.WriteAllBytes(source, original);
    Check(window.LoadSave(source), "The GUI could not open a valid save.");
    Check(window.Save!.Serialize().AsSpan().SequenceEqual(original), "Opening a save changed its bytes.");
    var clone = window.Save.Serialize();
    clone[0] = 0xff;
    Check(window.Save.FormatVersion == 9, "Serialized data shares the save buffer.");
    Check(window.FindControl<ListBox>("SectionList")!.ItemCount == 1, "The section list is empty.");
    var search = window.FindControl<TextBox>("SectionSearch")!;
    search.Text = "no-match";
    Dispatcher.UIThread.RunJobs();
    Check(window.FindControl<ListBox>("SectionList")!.ItemCount == 0, "Search did not filter sections.");
    search.Text = "user";
    Dispatcher.UIThread.RunJobs();
    Check(window.FindControl<ListBox>("SectionList")!.ItemCount == 1, "Search is not case-insensitive.");

    for (int pass = 0; pass < 3; pass++)
    {
        window.SetLanguage("zh-Hans");
        Dispatcher.UIThread.RunJobs();
        Check(search.PlaceholderText == "搜索…", "Chinese search text did not update.");
        Check(window.FindControl<TextBlock>("KindValue")!.Text == "游戏存档", "Chinese save type did not update.");
        Check(window.GetLogicalDescendants().OfType<MenuItem>().Any(item => Equals(item.Header, "文件")),
            "The File menu did not translate.");
        window.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Check(search.PlaceholderText == "Search...", "English search text did not recover.");
        Check(window.FindControl<TextBlock>("KindValue")!.Text == "Game save", "English save type did not recover.");
        Check(!window.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text == "总览"),
            "Chinese text remained after switching back to English.");
    }

    string copy = Path.Combine(temporary, "copy");
    Check(window.SaveCopy(copy), "The GUI could not create a save copy.");
    Check(File.ReadAllBytes(copy).AsSpan().SequenceEqual(original), "The GUI copy was not byte-identical.");
    Check(!window.SaveCopy(source), "The GUI overwrote the source file.");
    Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "The source file was modified.");
    string invalid = Path.Combine(temporary, "invalid");
    File.WriteAllBytes(invalid, [0, 1, 2]);
    Check(!window.LoadSave(invalid), "The GUI accepted a truncated save.");
    Check(window.Save.Serialize().AsSpan().SequenceEqual(original), "An invalid load replaced the current save.");
    Check(!Directory.EnumerateFiles(temporary, ".fee-*.tmp").Any(), "Temporary save files were left behind.");

    if (args is ["--save-directory", var directory])
        foreach (string name in new[] { "Auto", "Manual0", "Global" })
        {
            string path = Path.Combine(directory, name);
            byte[] before = File.ReadAllBytes(path);
            Check(window.LoadSave(path), $"The GUI could not open {name}.");
            string destination = Path.Combine(temporary, name + "-copy");
            Check(window.SaveCopy(destination), $"The GUI could not copy {name}.");
            Check(File.ReadAllBytes(destination).AsSpan().SequenceEqual(before), $"{name} copy differs.");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), $"{name} was modified.");
            Console.WriteLine($"{name}: GUI read and lossless copy passed.");
        }
}
finally
{
    foreach (string file in Directory.EnumerateFiles(temporary))
        File.Delete(file);
    Directory.Delete(temporary);
}

Dispatcher.UIThread.RunJobs();
using var frame = window.CaptureRenderedFrame();
if (frame is null || frame.PixelSize.Width == 0)
    throw new InvalidOperationException("GUI did not render.");
window.Close();
Console.WriteLine("GUI smoke test passed.");

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static byte[] Fixture()
{
    var bytes = new byte[284];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, 9);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x130);
    "EDNI"u8.CopyTo(bytes.AsSpan(128));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(132), 260);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(136), 276);
    "RESU"u8.CopyTo(bytes.AsSpan(260));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(264), 12);
    "testdata"u8.CopyTo(bytes.AsSpan(268));
    "LVRC"u8.CopyTo(bytes.AsSpan(276));
    uint crc = uint.MaxValue;
    foreach (byte value in bytes.AsSpan(0, 280))
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
    }
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(280), ~crc);
    EngageSave.Parse(bytes);
    return bytes;
}
