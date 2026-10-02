using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
Check(window.Icon is not null, "The Sommie window icon is missing.");
Check(window.FindControl<Image>("AppLogo") is null,
    "The menu must not contain a separate avatar, matching the FETH template.");
Check(UiLanguage.Read("en").Keys.Order().SequenceEqual(UiLanguage.Read("zh-Hans").Keys.Order()),
    "English and Chinese resource keys differ.");
Check(!window.FindControl<MenuItem>("SaveCopyMenu")!.IsEnabled, "Copy is enabled without a save.");
Check(window.FindControl<Grid>("MainPanel")!.IsVisible && !window.FindControl<StackPanel>("SettingsInputs")!.IsEnabled,
    "Main controls must be visible but disabled before a save is loaded.");
window.ShowItems();
Check(window.FindControl<Grid>("ItemsPanel")!.IsVisible && !window.FindControl<StackPanel>("ItemEditorInputs")!.IsEnabled,
    "Item controls must be visible but disabled before a save is loaded.");
window.ShowEmblems();
Check(window.FindControl<Grid>("EmblemsPanel")!.IsVisible && !window.FindControl<StackPanel>("EmblemBondForm")!.IsEnabled,
    "Emblem controls must be visible but disabled before a save is loaded.");
window.ShowSupports();
Check(window.FindControl<Grid>("SupportsPanel")!.IsVisible && !window.FindControl<StackPanel>("SupportForm")!.IsEnabled,
    "Support controls must be visible but disabled before a save is loaded.");
window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 0;
Check(window.FindControl<Grid>("MainPanel")!.IsVisible, "The native tab strip did not switch pages.");

string temporary = Path.Combine(Path.GetTempPath(), $"fee-gui-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporary);
try
{
    byte[] original = Fixture();
    string source = Path.Combine(temporary, "Manual0");
    File.WriteAllBytes(source, original);
    Check(window.LoadSave(source), "The GUI could not open a valid save.");
    Check(!window.CanEditMain, "A container without the verified Main schema was editable.");
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

    MainTests.Run(window, temporary);
    InventoryTests.Run(window, temporary);
    RosterTests.Run(window, temporary);
    RosterClassTests.Run(window, temporary);
    RosterSkillTests.Run(window, temporary);
    RosterStatTests.Run();
    RosterStatGuiTests.Run(window, temporary);
    EmblemTests.Run();
    BondRingTests.Run();
    EmblemGuiTests.Run(window, temporary);
    BondRingGuiTests.Run(window, temporary);
    SupportTests.Run();
    SupportGuiTests.Run(window, temporary);
    EngravingTests.Run();
    EngravingGuiTests.Run(window, temporary);

    if (args is ["--save-directory", var directory, ..])
        foreach (string name in new[] { "Auto", "Manual0", "Global" })
        {
            string path = Path.Combine(directory, name);
            byte[] before = File.ReadAllBytes(path);
            Check(window.LoadSave(path), $"The GUI could not open {name}.");
            Check(window.CanEditMain == (name != "Global"), $"{name}: Main edit availability is incorrect.");
            string destination = Path.Combine(temporary, name + "-copy");
            Check(window.SaveCopy(destination), $"The GUI could not copy {name}.");
            Check(File.ReadAllBytes(destination).AsSpan().SequenceEqual(before), $"{name} copy differs.");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), $"{name} was modified.");
            Console.WriteLine($"{name}: GUI read and lossless copy passed.");
            if (name != "Global")
            {
                var loaded = window.Save!;
                InventoryTests.CheckReal(loaded);
                RosterTests.CheckReal(loaded);
                RosterClassTests.CheckReal(loaded);
                RosterSkillTests.CheckReal(loaded);
                RosterStatTests.CheckReal(loaded);
                EmblemTests.CheckReal(loaded);
                BondRingTests.CheckReal(loaded);
                SupportTests.CheckReal(loaded);
                EngravingTests.CheckReal(loaded);
                var current = loaded.ReadMainValues();
                var updated = current with { Money = 12345, BondFragments = 6789, IronIngots = 111,
                    SteelIngots = 222, SilverIngots = 333, Difficulty = Difficulty.Normal,
                    GameMode = GameMode.Casual, SommieName = "Sommie round-trip 索拉" };
                var edited = loaded.WithMainValues(updated);
                Check(edited.ReadMainValues() == updated, $"{name}: Main edit failed.");
                Check(edited.WithMainValues(current).Serialize().AsSpan().SequenceEqual(before), $"{name}: unknown bytes changed.");
                Console.WriteLine($"{name}: Main edit and exact restoration passed.");
            }
        }
    if (args is ["--save-directory", var screenshotDirectory, "--screenshot", var screenshot, .. var captureOptions])
    {
        Check(window.LoadSave(Path.Combine(screenshotDirectory, "Manual0")), "Could not load screenshot save.");
        Check(window.FindControl<Button>("ApplyMainButton")!.IsEnabled, "The screenshot's Main action is disabled.");
        window.SetLanguage("en");
        if (captureOptions is ["--page", "items"] or ["--page", "items-engraving"])
        {
            window.ShowItems();
            if (captureOptions[1] == "items-engraving")
            {
                var items = window.FindControl<ListBox>("InventoryList")!;
                items.SelectedItem = items.Items.Cast<MainWindow.InventoryRow>().First(row =>
                    window.Save!.ReadInventory()[row.Slot].Item is { } item && EngravingCatalog.CanEngrave(item.ItemHash));
            }
        }
        else if (captureOptions is ["--page", "support"])
            window.ShowSupports();
        else if (captureOptions is ["--page", "emblems"] or ["--page", "rings"] or ["--page", "ring-meld"])
        {
            window.ShowEmblems();
            window.FindControl<TabStrip>("EmblemTabs")!.SelectedIndex = captureOptions[1] == "emblems" ? 0 : 1;
            if (captureOptions[1] == "ring-meld")
            {
                var rings = window.Save!.ReadBondRings();
                var list = window.FindControl<ListBox>("EmblemRecordList")!;
                list.SelectedItem = list.Items.Cast<MainWindow.EmblemRow>().First(row =>
                    rings.Any(ring => ring.InstanceId.ToString() == row.Key
                        && !ring.OwnerIndex.HasValue && BondRingCatalog.Melding(ring.RingHash) is not null));
            }
        }
        else if (captureOptions is ["--page", var page] && page.StartsWith("roster", StringComparison.Ordinal))
        {
            window.ShowRoster();
            window.FindControl<TabStrip>("RosterTabs")!.SelectedIndex = page switch
            {
                "roster" => 0, "roster-stats" => 1, "roster-items" => 2, "roster-skills" => 3, "roster-proficiencies" => 4,
                _ => throw new ArgumentException("Unknown roster screenshot tab.")
            };
        }
        else
            Check(captureOptions.Length == 0, "Unknown screenshot page.");
        Dispatcher.UIThread.RunJobs();
        // Allow theme transitions to settle before capturing the final state.
        var rendering = System.Diagnostics.Stopwatch.StartNew();
        while (rendering.ElapsedMilliseconds < 500)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
        using var rendered = window.CaptureRenderedFrame();
        Check(rendered is not null, "Screenshot capture failed.");
        rendered!.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Console.WriteLine($"Screenshot: {Path.GetFullPath(screenshot)}");
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
