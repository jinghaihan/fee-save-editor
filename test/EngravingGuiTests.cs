using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class EngravingGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = EngravingTests.Fixture();
        string source = Path.Combine(temporary, "engraving-fixture");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Could not load the engraving fixture.");
        window.ShowItems();
        var inventory = window.FindControl<ListBox>("InventoryList")!;
        var input = window.FindControl<ComboBox>("ItemEngravingInput")!;
        var effects = window.FindControl<ItemsControl>("ItemEngravingEffects")!;
        var refine = window.FindControl<NumericUpDown>("ItemRefineInput")!;
        SelectItem(inventory, 1);
        Check(input.IsEnabled && input.ItemCount == 22 && (input.SelectedItem as MainWindow.EngravingChoice)?.Label == "0x00123ABC",
            "Unknown engravings were hidden or replaced on load.");
        Check(!effects.IsVisible && effects.ItemCount == 0, "An unknown engraving displayed guessed effects.");
        CheckEffects(window, input, effects);
        SelectEngraving(input, EngravingTests.Marth);
        Check(effects.Items.Cast<MainWindow.EngravingEffect>().Select(row => row.Label)
            .SequenceEqual(new[] { "Might", "Weight", "Hit", "Critical", "Avoid", "Dodge" }), "English effect labels are incorrect.");
        refine.Text = "4";
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans");
            Dispatcher.UIThread.RunJobs();
            Check((input.SelectedItem as MainWindow.EngravingChoice)?.Label == EngravingCatalog.Get(EngravingTests.Marth).Name("zh-Hans"),
                "The pending engraving did not translate.");
            Check(effects.Items.Cast<MainWindow.EngravingEffect>().Select(row => row.Label)
                .SequenceEqual(new[] { "威力", "重量", "命中", "必杀", "回避", "必杀回避" }), "Effect labels did not translate.");
            window.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Check((input.SelectedItem as MainWindow.EngravingChoice)?.Label == "Marth" && refine.Text == "4",
                "Language switching lost the pending engraving or refinement.");
            Check(effects.Items.Cast<MainWindow.EngravingEffect>().Select(row => row.Label)
                .SequenceEqual(new[] { "Might", "Weight", "Hit", "Critical", "Avoid", "Dodge" }), "Chinese effect labels remained in English mode.");
        }
        Check(window.ApplyItemValues() && window.Save!.ReadInventory()[1].Item is { RefineLevel: 4 }
            && window.Save.ReadInventory()[2].Item!.EngravingHash is null, "The GUI did not transfer the engraving.");
        SelectEngraving(input, null);
        Check(!effects.IsVisible && effects.ItemCount == 0, "Clearing an engraving left stale effects visible.");
        Check(window.ApplyItemValues() && window.Save!.ReadInventory()[1].Item!.EngravingHash is null,
            "The GUI could not clear an engraving.");
        SelectItem(inventory, 0);
        Check(!input.IsEnabled && input.ItemCount == 1, "A staff offered engraving choices.");
        SelectItem(inventory, 3);
        window.FindControl<ComboBox>("ItemTypeInput")!.SelectedItem = window.FindControl<ComboBox>("ItemTypeInput")!.Items
            .Cast<MainWindow.ItemChoice>().First(row => row.Definition.Id == "IID_リカバー");
        Check(!window.ApplyItemValues(), "Replacing an engraved weapon silently discarded the engraving.");
        SelectEngraving(input, null);
        Check(window.ApplyItemValues() && window.Save!.ReadInventory()[3].Item is { Uses: 10, EngravingHash: null },
            "Clearing the engraving while replacing the item failed.");

        Check(window.LoadSave(source), "Could not reset engraving tests.");
        window.ShowRoster();
        window.FindControl<TabStrip>("RosterTabs")!.SelectedIndex = 2;
        var equipment = window.FindControl<ListBox>("RosterItemsList")!;
        var carriedInput = window.FindControl<ComboBox>("RosterItemEngraving")!;
        SelectItem(equipment, 1);
        CheckEffects(window, carriedInput, window.FindControl<ItemsControl>("RosterItemEngravingEffects")!);
        SelectEngraving(carriedInput, EngravingTests.Marth);
        Check(window.ApplyRosterItem() && window.Save!.ReadInventory()[2].Item!.EngravingHash is null,
            "The carried-item GUI did not clear the convoy's old owner.");
        Check((carriedInput.SelectedItem as MainWindow.EngravingChoice)?.Label == "Marth",
            "Applying a carried engraving did not refresh the editor baseline.");
        window.ShowItems();
        SelectItem(inventory, 4);
        SelectEngraving(input, EngravingTests.Marth);
        Check(window.ApplyItemValues() && window.Save!.ReadRoster()[0].Items[1].Item!.EngravingHash is null,
            "The convoy GUI did not clear the carried weapon's old owner.");
        Check((carriedInput.SelectedItem as MainWindow.EngravingChoice)?.Hash is null,
            "The hidden carried editor retained a transferred-away engraving.");
        SelectEngraving(input, "GID_チキ");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 0;
        string copy = Path.Combine(temporary, "pending-engraving-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadInventory()[4].Item!.EngravingHash == EngravingCatalog.Get("GID_チキ").Hash,
            "Saving from another page discarded a pending convoy engraving.");

        string carriedSource = Path.Combine(temporary, "carried-engraving-fixture");
        EngageSave.Parse(original).WithRosterEngraving(0, 1, EngravingTests.Marth).WriteCopy(carriedSource);
        Check(window.LoadSave(carriedSource), "Could not reset the pending carried test.");
        window.ShowRoster();
        SelectItem(equipment, 1);
        window.FindControl<NumericUpDown>("RosterItemRefine")!.Text = "4";
        window.ShowItems();
        SelectItem(inventory, 4);
        SelectEngraving(input, EngravingTests.Marth);
        Check(window.ApplyItemValues(), "Could not apply an engraving with pending carried edits.");
        string pending = Path.Combine(temporary, "pending-carried-engraving-copy");
        Check(window.SaveCopy(pending), "Saving rejected valid pending carried changes.");
        var saved = EngageSave.Load(pending);
        Check(saved.ReadRoster()[0].Items[1].Item is { RefineLevel: 4, EngravingHash: null }
            && saved.ReadInventory()[4].Item!.EngravingHash == EngravingCatalog.Get(EngravingTests.Marth).Hash,
            "An unrelated pending edit restored a stale transferred engraving.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI engraving edits overwrote the source save.");
        uint dlcHash = ItemCatalog.Hash("IID_トライゾン");
        string dlcSource = Path.Combine(temporary, "dlc-engraving-fixture");
        File.WriteAllBytes(dlcSource, EngravingTests.Fixture(items: new InventoryItem?[]
            { new(dlcHash, 7, 200, 0x55, null) }));
        Check(ItemCatalog.Find(dlcHash) is null && window.LoadSave(dlcSource), "Could not test engraving-only DLC editing.");
        window.ShowItems();
        Check(window.FindControl<ComboBox>("ItemTypeInput")!.PlaceholderText == "Représailles"
            && !window.FindControl<NumericUpDown>("ItemUsesInput")!.IsEnabled
            && input.IsEnabled, "A verified DLC weapon was blank or exposed unverified scalar editing.");
        SelectEngraving(input, EngravingTests.Marth);
        window.SetLanguage("zh-Hans");
        Check(window.FindControl<ComboBox>("ItemTypeInput")!.PlaceholderText == RosterCatalog.Item(dlcHash)!.Name("zh-Hans"),
            "The engraving-only weapon name did not translate.");
        window.SetLanguage("en");
        Check(window.ApplyItemValues() && window.Save!.ReadInventory()[0].Item is { Uses: 7, RefineLevel: 200, Flags: 0x55 }
            && window.Save.ReadInventory()[0].Item!.EngravingHash == EngravingCatalog.Get(EngravingTests.Marth).Hash,
            "The DLC engraving-only editor changed unverified scalar fields.");
        string dlcCarriedSource = Path.Combine(temporary, "dlc-carried-engraving-fixture");
        byte[] dlcRoster = RosterTests.Fixture(mutate: bytes =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34 + 156, 4), dlcHash));
        File.WriteAllBytes(dlcCarriedSource, EngravingTests.Fixture(dlcRoster));
        Check(window.LoadSave(dlcCarriedSource), "Could not test a carried DLC weapon.");
        window.ShowRoster();
        window.FindControl<TabStrip>("RosterTabs")!.SelectedIndex = 2;
        SelectItem(equipment, 1);
        Check(window.FindControl<ComboBox>("RosterItemType")!.PlaceholderText == "Représailles"
            && !window.FindControl<NumericUpDown>("RosterItemUses")!.IsEnabled && carriedInput.IsEnabled,
            "A carried DLC weapon lost engraving-only access or its visible name.");
        SelectEngraving(carriedInput, EngravingTests.Sigurd);
        Check(window.ApplyRosterItem() && window.Save!.ReadRoster()[0].Items[1].Item is { Uses: 255, RefineLevel: 3, Flags: 0x55 }
            && window.Save.ReadRoster()[0].Items[1].Item!.EngravingHash == EngravingCatalog.Get(EngravingTests.Sigurd).Hash
            && window.Save.ReadInventory()[3].Item!.EngravingHash is null,
            "A carried DLC engraving did not preserve fields or transfer ownership.");
        Console.WriteLine("Engraving GUI: translated selectors, transfers, clearing, pending inputs and hidden-page saves passed.");
    }

    private static void SelectItem(ListBox list, int slot)
    {
        list.SelectedItem = list.Items.Cast<MainWindow.InventoryRow>().First(row => row.Slot == slot);
        Dispatcher.UIThread.RunJobs();
    }
    private static void SelectEngraving(ComboBox input, string? id)
    {
        uint? hash = id is null ? null : EngravingCatalog.Get(id).Hash;
        input.SelectedItem = input.Items.Cast<MainWindow.EngravingChoice>().First(row => row.Hash == hash);
    }
    private static void CheckEffects(MainWindow window, ComboBox input, ItemsControl effects)
    {
        byte[] original = window.Save!.Serialize();
        foreach (var engraving in EngravingCatalog.Engravings)
        {
            SelectEngraving(input, engraving.Id);
            Dispatcher.UIThread.RunJobs();
            var values = effects.Items.Cast<MainWindow.EngravingEffect>().Select(row => row.Value).ToArray();
            int[] expected = { engraving.Power, engraving.Weight, engraving.Hit, engraving.Critical, engraving.Avoid, engraving.Secure };
            Check(effects.IsVisible && values.Length == 6, "A selected engraving did not show all six effects.");
            Check(values.Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).SequenceEqual(expected)
                && values.Zip(expected).All(pair => pair.Second <= 0 || pair.First.StartsWith('+')),
                "An engraving preview has incorrect modifiers or signs.");
        }
        Check(window.Save.Serialize().AsSpan().SequenceEqual(original), "Previewing effects changed save bytes.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
