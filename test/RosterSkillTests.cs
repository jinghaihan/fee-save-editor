using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterSkillTests
{
    private const string Canter = "SID_再移動";
    private const string CanterPlus = "SID_再移動＋";
    private const string Starsphere = "SID_星玉の加護";

    public static void Run(MainWindow window, string temporary)
    {
        Check(RosterCatalog.Skills.Count > 500 && RosterCatalog.Skills.Count(skill => skill.Inheritable) == 277,
            "The full base/DLC inheritable skill catalog is incomplete.");
        foreach (var skill in RosterCatalog.Skills)
            Check(skill.Name("en").Length > 0 && skill.Name("zh-Hans").Length > 0, "A skill lacks a translation.");
        Check(RosterCatalog.Skill(ItemCatalog.Hash("SID_裏邪竜ノ娘_兵種スキル"))?.Name("en") == "Resist Emblems",
            "Nel's class skill did not resolve.");
        var save = EngageSave.Parse(RosterTests.Fixture());
        var unlocked = save.WithRosterSkill(0, Canter, true);
        Check(unlocked.Length == save.Length + 14 && unlocked.ReadRoster()[0].Progress.InheritedSkills.Count == 1,
            "Unlocking a skill did not serialize the pool entry.");
        Check(unlocked.WithRosterSkill(0, Canter, false).Serialize().AsSpan().SequenceEqual(save.Serialize()),
            "Skill add/remove was not byte-exact.");
        var equipped = unlocked.WithRosterEquippedSkills(0, ItemCatalog.Hash(Canter), null);
        Check(equipped.Length == unlocked.Length + 14 && equipped.ReadRoster()[0].Progress.EquippedSkills.Single().Category == 11,
            "An equipped skill did not use the inheritance category.");
        Check(equipped.WithRosterEquippedSkills(0, null, null).Serialize().AsSpan().SequenceEqual(unlocked.Serialize()),
            "Equipped skill reversal changed other state.");
        var upgraded = equipped.WithRosterSkill(0, CanterPlus, true);
        var progress = upgraded.ReadRoster()[0].Progress;
        Check(progress.InheritedSkills.Count == 1 && progress.InheritedSkills.Single().Hash == ItemCatalog.Hash(CanterPlus)
            && progress.EquippedSkills.Single().Hash == ItemCatalog.Hash(CanterPlus), "Upgrading a tier left duplicate or stale skills.");
        Check(ReferenceEquals(upgraded, upgraded.WithRosterSkill(0, Canter, true)), "Unlocking a lower tier downgraded a skill.");
        Check(upgraded.WithRosterSkill(0, CanterPlus, false).Serialize().AsSpan().SequenceEqual(save.Serialize()),
            "Removing an equipped skill did not clean both arrays.");
        var dlc = upgraded.WithRosterSkill(0, Starsphere, true).WithRosterEquippedSkills(0, ItemCatalog.Hash(CanterPlus), ItemCatalog.Hash(Starsphere));
        Check(dlc.ReadRoster()[0].Progress.EquippedSkills.Count == 2, "A DLC skill could not occupy the second slot.");
        Reject(() => save.WithRosterSkill(0, "SID_神竜気", true));
        Reject(() => save.WithRosterSkill(0, "SID_missing", true));
        Reject(() => save.WithRosterEquippedSkills(0, ItemCatalog.Hash(Canter), null));
        Reject(() => unlocked.WithRosterEquippedSkills(0, ItemCatalog.Hash(Canter), ItemCatalog.Hash(Canter)));
        var all = save.UnlockAllRosterSkills(0);
        Check(all.ReadRoster()[0].Progress.InheritedSkills.Count == 94, "Unlock All did not retain one maximum tier per family.");
        Check(ReferenceEquals(all, all.UnlockAllRosterSkills(0)), "Unlock All is not idempotent.");
        var proficiencies = save.WithRosterProficiencies(0, 2);
        Check(proficiencies.ReadRoster()[0].Progress.Proficiencies == 2, "Proficiency edit failed.");
        Check(proficiencies.WithRosterProficiencies(0, 510).Serialize().AsSpan().SequenceEqual(save.Serialize()),
            "Proficiency reversal changed other masks.");
        Reject(() => save.WithRosterProficiencies(0, 0));
        Reject(() => save.WithRosterProficiencies(0, 1022));
        var condition = save.WithRosterCondition(0, -100, 0);
        Check(condition.ReadRoster()[0].Progress is { InternalLevel: -100, CurrentHP: 0 }, "Signed internal level or HP did not round-trip.");
        Check(condition.WithRosterCondition(0, 4, 20).Serialize().AsSpan().SequenceEqual(save.Serialize()), "Condition reversal changed other fields.");
        Reject(() => save.WithRosterCondition(0, -101, 20));
        Reject(() => save.WithRosterCondition(0, 101, 20));
        Reject(() => save.WithRosterCondition(0, 4, save.RosterMaximumHP(0) + 1));
        Reject(() => save.WithRosterCondition(0, 4, -1));
        int start = 34;
        byte[] fixture = RosterTests.Fixture();
        int record = save.Sections.Single(section => section.Name == "UNIT").PayloadOffset + start;
        int progressStart = start + BinaryPrimitives.ReadInt32LittleEndian(fixture.AsSpan(record)) - 330;
        foreach (int offset in new[] { progressStart, progressStart + 16, progressStart + 24, progressStart + 32 })
        {
            var malformed = EngageSave.Parse(RosterTests.Fixture(mutate: bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 99)));
            RejectData(() => malformed.ReadRoster());
        }
        var malformedCount = EngageSave.Parse(RosterTests.Fixture(mutate: bytes =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(progressStart + 20), uint.MaxValue)));
        RejectData(() => malformedCount.ReadRoster());
        var special = EngageSave.Parse(RosterTests.Fixture(mutate: bytes =>
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(progressStart + 16 + 24 + 1 + 4), 1022)));
        Check(special.WithRosterProficiencies(0, 2).ReadRoster()[0].Progress.Proficiencies == 514,
            "Editing normal proficiencies removed the special weapon bit.");
        var enhanced = EngageSave.Parse(RosterTests.Fixture(mutate: bytes =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(progressStart + 16 + 24 + 1 + 12 + 4 + 156 + 4 + 8), 7)));
        Check(enhanced.RosterMaximumHP(0) == enhanced.ReadRoster()[0].Stats[0].Value + 7,
            "Maximum HP omitted the enhancement bonus.");
        enhanced = enhanced.WithRosterCondition(0, 4, enhanced.RosterMaximumHP(0)).WithRosterStat(0, RosterStat.HP, 20);
        Check(enhanced.ReadRoster()[0].Progress.CurrentHP == 27, "Editing base HP dropped its enhancement bonus.");
        Check(save.ReadRoster()[1].Progress.InheritedSkills.Count == 0 && all.ReadRoster()[1].Progress.InheritedSkills.Count == 0,
            "An edit changed another character's skills.");
        string source = Path.Combine(temporary, "roster-skills-fixture");
        File.WriteAllBytes(source, dlc.Serialize());
        Check(window.LoadSave(source), "Could not load the Skills GUI fixture.");
        var tabs = window.FindControl<TabStrip>("RosterTabs")!;
        tabs.SelectedIndex = 3;
        Check(window.FindControl<Grid>("RosterSkillsForm")!.IsVisible, "The Skills tab did not open.");
        var first = window.FindControl<ComboBox>("RosterEquippedSkill1")!;
        var second = window.FindControl<ComboBox>("RosterEquippedSkill2")!;
        Check(((MainWindow.SkillChoice)first.SelectedItem!).Label == "Canter+"
            && ((MainWindow.SkillChoice)second.SelectedItem!).Label == "Starsphere", "Equipped skills were blank or unlabeled.");
        second.SelectedIndex = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.SkillChoice)first.SelectedItem!).Label != "Canter+", "Chinese skills were not translated.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.SkillChoice)first.SelectedItem!).Label == "Canter+" && ((MainWindow.SkillChoice)second.SelectedItem!).Hash is null,
                "Language switching lost a pending equipment edit.");
        }
        string destination = Path.Combine(temporary, "roster-skills-pending");
        window.ShowItems();
        Check(window.SaveCopy(destination) && EngageSave.Load(destination).ReadRoster()[0].Progress.EquippedSkills.Count == 1,
            "Saving from another page lost pending equipped skills.");
        tabs.SelectedIndex = 4;
        Check(window.FindControl<StackPanel>("RosterProficienciesForm")!.IsVisible, "The Proficiencies tab did not open.");
        Console.WriteLine("Roster skills: 277 base/DLC skills, 94 maximum tiers, two slots, proficiencies, HP/internal limits and live UI passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        foreach (var character in save.ReadRoster().Where(character => RosterCatalog.Person(character.PersonHash) is not null))
        {
            Check(character.Progress.InheritedSkills.Count == 94
                && character.Progress.InheritedSkills.All(skill => RosterCatalog.Skill(skill.Hash) is { Inheritable: true }),
                "A real base/DLC inherited skill was not recognized.");
            var first = character.Progress.EquippedSkills.ElementAtOrDefault(0)?.Hash;
            var second = character.Progress.EquippedSkills.ElementAtOrDefault(1)?.Hash;
            Check(ReferenceEquals(save, save.WithRosterEquippedSkills(character.Index, first, second)), "Inspection rewrote equipped skill metadata.");
            var removed = save.WithRosterEquippedSkills(character.Index, null, null);
            Check(removed.ReadRoster()[character.Index].Progress.EquippedSkills.Count == 0, "Clearing real slots failed.");
            var edited = save.WithRosterCondition(character.Index, character.Progress.InternalLevel == 99 ? 98 : 99, character.Progress.CurrentHP);
            Check(edited.WithRosterCondition(character.Index, character.Progress.InternalLevel, character.Progress.CurrentHP)
                .Serialize().AsSpan().SequenceEqual(save.Serialize()), "Real internal-level edit did not restore exactly.");
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("An illegal skill/condition edit was accepted.");
    }
    private static void RejectData(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed progression data was accepted.");
    }
}
