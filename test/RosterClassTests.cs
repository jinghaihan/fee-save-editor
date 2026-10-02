using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterClassTests
{
    public static void Run(MainWindow window, string temporary)
    {
        var save = EngageSave.Parse(RosterTests.Fixture());
        foreach (var person in RosterCatalog.Persons)
        {
            var classes = RosterCatalog.ClassesFor(person.Hash, person.Gender);
            Check(classes.Any(job => job.Id == person.BirthClass), $"{person.Id} lost their native class.");
            Check(classes.Any(job => job.Id == "JID_エンチャント") && classes.Any(job => job.Id == "JID_マージカノン"),
                "DLC generic classes are missing.");
            Check(classes.All(job => (job.Flags & 4) == 0 || person.Gender == 2), "A male character can become a base flier.");
            foreach (var exclusive in RosterCatalog.Classes.Where(job => job.Flags == 1))
            {
                bool expected = exclusive.Id == person.BirthClass
                    || exclusive.Id == RosterCatalog.Class(ItemCatalog.Hash(person.BirthClass))?.Promotion;
                Check(classes.Contains(exclusive) == expected, "An exclusive class is available to the wrong character.");
            }
        }
        Check(RosterCatalog.ClassesFor(ItemCatalog.Hash("PID_リュール"), 2).Any(job => job.Id == "JID_ランスペガサス"),
            "Female Alear lost female-only classes.");
        Check(save.ReadRosterClasses(0).All(job => (job.Flags & 4) == 0), "Male Alear ignored customization gender.");
        var before = save.ReadRoster()[0];
        var changed = save.WithRosterClass(0, "JID_ソードマスター", 2);
        var character = changed.ReadRoster()[0];
        Check(character.ClassHash == ItemCatalog.Hash("JID_ソードマスター") && character.Values == before.Values with { Level = 1, Experience = 0 }
            && character.Progress.InternalLevel == 8 && character.Progress.ClassSkill is null,
            "Reclassing did not reset/recalculate the game's associated fields.");
        Check(character.Progress.Proficiencies == before.Progress.Proficiencies && character.Items.SequenceEqual(before.Items),
            "Reclassing changed carried items or existing proficiencies.");
        Check(ReferenceEquals(changed, changed.WithRosterClass(0, "JID_ソードマスター", 2)), "Same-class selection reset progress.");
        Reject(() => save.WithRosterClass(0, "JID_ダンサー", 256));
        Reject(() => save.WithRosterClass(0, "JID_ランスペガサス", 4));
        Reject(() => save.WithRosterClass(0, "JID_ブレイブヒーロー", 2));
        Reject(() => save.WithRosterClass(0, "JID_missing", 2));
        var hero = save.WithRosterClass(0, "JID_ブレイブヒーロー", 6);
        hero = hero.WithRosterValues(0, new(10, 33, 400));
        var variant = hero.WithRosterClass(0, "JID_ブレイブヒーロー", 10).ReadRoster()[0];
        Check(variant.Values.Level == 10 && variant.Values.Experience == 33 && variant.Progress.SelectedWeapons == 10,
            "Changing only a weapon variant reset the class level.");
        var thief = hero.WithRosterClass(0, "JID_シーフ", 32).ReadRoster()[0];
        Check(thief.Values.Level == 21 && thief.Values.Experience == 0 && thief.Progress.InternalLevel == 0,
            "Advanced-to-special reclassing did not start at level 21.");
        Reject(() => changed.WithRosterClassSkill(0, true));
        changed = changed.WithRosterValues(0, new(5, 0, 400));
        var learned = changed.WithRosterClassSkill(0, true);
        Check(learned.Length == changed.Length + 6 && learned.ReadRoster()[0].Progress.ClassSkill
            == ItemCatalog.Hash(RosterCatalog.Class(character.ClassHash)!.LearningSkill), "Class skill serialization failed.");
        Check(learned.WithRosterClassSkill(0, false).Serialize().AsSpan().SequenceEqual(changed.Serialize()),
            "Class skill reversal changed opaque state.");
        Check(learned.WithRosterClass(0, "JID_ブレイブヒーロー", 6).ReadRoster()[0].Progress.ClassSkill is null,
            "Reclassing retained the previous class skill.");
        Check(changed.ReadRoster()[1].Values == save.ReadRoster()[1].Values, "Reclassing modified another character.");
        string source = Path.Combine(temporary, "roster-class-fixture");
        File.WriteAllBytes(source, save.Serialize());
        Check(window.LoadSave(source), "Could not load class GUI fixture.");
        var select = window.FindControl<ComboBox>("RosterClass")!;
        select.SelectedItem = select.Items.OfType<MainWindow.ClassChoice>().Single(choice => choice.Definition.Id == "JID_ブレイブヒーロー");
        Dispatcher.UIThread.RunJobs();
        var weapons = window.FindControl<ComboBox>("RosterWeaponVariant")!;
        Check(weapons.ItemCount == 2, "The Hero weapon branches were not offered.");
        weapons.SelectedItem = weapons.Items.OfType<MainWindow.WeaponChoice>().Single(choice => choice.Mask == 10);
        window.SetLanguage("zh-Hans"); window.SetLanguage("en");
        Check(((MainWindow.WeaponChoice)weapons.SelectedItem!).Mask == 10, "Language switching lost the selected branch.");
        Check(window.ChangeRosterClass() && window.Save!.ReadRoster()[0].Progress.SelectedWeapons == 10,
            "The class dropdown did not edit the save.");
        Check(window.FindControl<NumericUpDown>("RosterLevel")!.Value == 1, "Reclassing left stale level inputs.");
        Console.WriteLine("Roster classes: all 41 owners, gender restrictions, DLC, weapon branches and associated state passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        foreach (var character in save.ReadRoster().Where(character => RosterCatalog.Person(character.PersonHash) is not null))
        {
            Check(save.ReadRosterClasses(character.Index).Any(job => job.Hash == character.ClassHash), "A real playable class was excluded.");
            var edited = save.WithRosterClass(character.Index, "JID_ソードマスター", 2);
            Check(edited.ReadRoster()[character.Index].ClassHash == ItemCatalog.Hash("JID_ソードマスター"), "Real reclassing failed.");
            foreach (var section in save.Sections.Where(section => section.Name != "UNIT"))
            {
                var copy = edited.Sections.Single(value => value.Name == section.Name);
                Check(save.Serialize().AsSpan(section.Offset, section.Length + 8).SequenceEqual(
                    edited.Serialize().AsSpan(copy.Offset, copy.Length + 8)), "Reclassing modified an unrelated section.");
            }
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("An illegal class edit was accepted.");
    }
}
