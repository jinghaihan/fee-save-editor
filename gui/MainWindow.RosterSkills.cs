using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record SkillChoice(uint? Hash, string Label);
    private readonly Dictionary<WeaponType, CheckBox> _rosterProficiencies = new();
    private uint? _selectedRosterSkill;

    private void ClearRosterSkills()
    {
        _selectedRosterSkill = null;
        RosterSkillList.ItemsSource = Array.Empty<SkillChoice>();
        RosterSkillChoice.ItemsSource = Array.Empty<SkillChoice>();
        RosterEquippedSkill1.ItemsSource = Array.Empty<SkillChoice>();
        RosterEquippedSkill2.ItemsSource = Array.Empty<SkillChoice>();
        RosterClassSkillName.Text = "";
        RosterClassSkillUnlocked.IsChecked = false;
        _rosterProficiencies.Clear();
        RosterProficiencyInputs.Children.Clear();
    }

    private static string SkillName(uint hash) => RosterCatalog.Skill(hash)?.Name(UiLanguage.Current)
        ?? $"{UiLanguage.Get("UnknownSkill")} (0x{hash:X8})";

    private void RefreshRosterSkills(RosterCharacter character, bool preserveEdits)
    {
        uint? first = preserveEdits ? (RosterEquippedSkill1.SelectedItem as SkillChoice)?.Hash
            : character.Progress.EquippedSkills.ElementAtOrDefault(0)?.Hash;
        uint? second = preserveEdits ? (RosterEquippedSkill2.SelectedItem as SkillChoice)?.Hash
            : character.Progress.EquippedSkills.ElementAtOrDefault(1)?.Hash;
        var choices = character.Progress.InheritedSkills.Concat(character.Progress.EquippedSkills).Select(skill => skill.Hash)
            .Distinct().Select(hash => new SkillChoice(hash, SkillName(hash)))
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        choices.Insert(0, new(null, UiLanguage.Get("None")));
        RosterEquippedSkill1.ItemsSource = choices;
        RosterEquippedSkill2.ItemsSource = choices;
        RosterEquippedSkill1.SelectedItem = choices.FirstOrDefault(choice => choice.Hash == first) ?? choices[0];
        RosterEquippedSkill2.SelectedItem = choices.FirstOrDefault(choice => choice.Hash == second) ?? choices[0];
        var job = RosterCatalog.Class(character.ClassHash);
        uint? classSkill = null;
        if (!string.IsNullOrEmpty(job?.LearningSkill)) classSkill = ItemCatalog.Hash(job.LearningSkill);
        RosterClassSkillName.Text = classSkill.HasValue ? SkillName(classSkill.Value) : UiLanguage.Get("None");
        if (!preserveEdits) RosterClassSkillUnlocked.IsChecked = character.Progress.ClassSkill.HasValue;
        RosterClassSkillUnlocked.IsEnabled = classSkill.HasValue && character.Values.Level >= (job!.MaxLevel >= 40 ? 25 : 5);
        RefreshInheritedSkills(character);
        var checks = preserveEdits ? _rosterProficiencies.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true) : null;
        _rosterProficiencies.Clear();
        RosterProficiencyInputs.Children.Clear();
        uint active = character.Progress.SelectedWeapons;
        if (active == 0 && job is not null && job.WeaponVariants().Count == 1) active = job.WeaponVariants()[0];
        foreach (var type in Enum.GetValues<WeaponType>().Where(type => type != WeaponType.Special))
        {
            uint bit = 1u << (int)type;
            bool enabled = ((character.Progress.OriginalProficiencies | active) & bit) == 0;
            bool unlocked = checks?.GetValueOrDefault(type) ?? (character.Progress.Proficiencies & bit) != 0;
            var input = new CheckBox { Content = UiLanguage.Get(type.ToString()), Width = 168, Height = 42,
                IsChecked = unlocked, IsEnabled = enabled };
            RosterProficiencyInputs.Children.Add(input);
            _rosterProficiencies.Add(type, input);
        }
    }

    private void RefreshInheritedSkills(RosterCharacter character)
    {
        string query = RosterSkillSearch.Text?.Trim() ?? "";
        var rows = character.Progress.InheritedSkills.Select(skill => new SkillChoice(skill.Hash, SkillName(skill.Hash)))
            .Where(choice => choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        bool refreshing = _refreshingRoster;
        _refreshingRoster = true;
        RosterSkillList.ItemsSource = rows;
        RosterSkillList.SelectedItem = rows.FirstOrDefault(choice => choice.Hash == _selectedRosterSkill) ?? rows.FirstOrDefault();
        _selectedRosterSkill = (RosterSkillList.SelectedItem as SkillChoice)?.Hash;
        uint? selected = (RosterSkillChoice.SelectedItem as SkillChoice)?.Hash;
        var choices = RosterCatalog.Skills.Where(skill => skill.Inheritable)
            .Select(skill => new SkillChoice(skill.Hash, skill.Name(UiLanguage.Current)))
            .Where(choice => choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || choice.Hash == selected)
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        RosterSkillChoice.ItemsSource = choices;
        RosterSkillChoice.SelectedItem = choices.FirstOrDefault(choice => choice.Hash == selected) ?? choices.FirstOrDefault();
        RemoveRosterSkillButton.IsEnabled = _selectedRosterSkill.HasValue
            && RosterCatalog.Skill(_selectedRosterSkill.Value) is { Inheritable: true };
        _refreshingRoster = refreshing;
    }

    private EngageSave PendingRosterSkills(EngageSave save, RosterCharacter old)
    {
        uint? first = (RosterEquippedSkill1.SelectedItem as SkillChoice)?.Hash;
        uint? second = (RosterEquippedSkill2.SelectedItem as SkillChoice)?.Hash;
        if (first != old.Progress.EquippedSkills.ElementAtOrDefault(0)?.Hash
            || second != old.Progress.EquippedSkills.ElementAtOrDefault(1)?.Hash)
            save = save.WithRosterEquippedSkills(old.Index, first, second);
        bool classSkill = RosterClassSkillUnlocked.IsChecked == true;
        if (classSkill != old.Progress.ClassSkill.HasValue)
            save = save.WithRosterClassSkill(old.Index, classSkill);
        uint mask = SelectedProficiencies();
        if (mask != (old.Progress.Proficiencies & 510))
            save = save.WithRosterProficiencies(old.Index, mask);
        return save;
    }

    private uint SelectedProficiencies()
    {
        uint mask = 0;
        foreach (var (type, input) in _rosterProficiencies)
            if (input.IsChecked == true) mask |= 1u << (int)type;
        return mask;
    }

    private EngageSave PendingRosterCondition(EngageSave save, RosterCharacter old)
    {
        var current = save.ReadRoster()[old.Index].Progress;
        int internalLevel = current.InternalLevel, hp = current.CurrentHP;
        if (RosterInternalLevel.Text != old.Progress.InternalLevel.ToString()) internalLevel = Amount(RosterInternalLevel);
        if (RosterCurrentHP.Text != old.Progress.CurrentHP.ToString()) hp = Amount(RosterCurrentHP);
        return save.WithRosterCondition(old.Index, internalLevel, hp);
    }

    public bool ApplyRosterSkills() => EditRoster(save => save.WithRosterEquippedSkills(_selectedCharacter!.Value,
        (RosterEquippedSkill1.SelectedItem as SkillChoice)?.Hash, (RosterEquippedSkill2.SelectedItem as SkillChoice)?.Hash), refresh: true);

    public bool ApplyRosterProficiencies() => EditRoster(save =>
        save.WithRosterProficiencies(_selectedCharacter!.Value, SelectedProficiencies()), refresh: true);

    private void ApplyRosterSkills_Click(object? sender, RoutedEventArgs e) => ApplyRosterSkills();
    private void ApplyRosterProficiencies_Click(object? sender, RoutedEventArgs e) => ApplyRosterProficiencies();
    private void ApplyRosterClassSkill_Click(object? sender, RoutedEventArgs e) => EditRoster(save =>
        PendingGeneralValues(save, SelectedCharacter!).WithRosterClassSkill(_selectedCharacter!.Value,
            RosterClassSkillUnlocked.IsChecked == true), refresh: true);
    private void UnlockRosterProficiencies_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => save.WithRosterProficiencies(_selectedCharacter!.Value, 510), refresh: true);
    private void RestoreRosterHP_Click(object? sender, RoutedEventArgs e) => EditRoster(save =>
        save.WithRosterCondition(_selectedCharacter!.Value, SelectedCharacter!.Progress.InternalLevel,
            save.RosterMaximumHP(_selectedCharacter.Value)), refresh: true);
    private void UnlockRosterSkill_Click(object? sender, RoutedEventArgs e) => EditRoster(save =>
        save.WithRosterSkill(_selectedCharacter!.Value,
            RosterCatalog.Skill((RosterSkillChoice.SelectedItem as SkillChoice)?.Hash ?? 0)?.Id
                ?? throw new ArgumentException(UiLanguage.Get("SelectSkill")), true), refresh: true);
    private void RemoveRosterSkill_Click(object? sender, RoutedEventArgs e) => EditRoster(save =>
        save.WithRosterSkill(_selectedCharacter!.Value, RosterCatalog.Skill(_selectedRosterSkill ?? 0)?.Id
            ?? throw new ArgumentException(UiLanguage.Get("SelectSkill")), false), refresh: true);
    private void UnlockAllRosterSkills_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => save.UnlockAllRosterSkills(_selectedCharacter!.Value), refresh: true);
    private void RosterSkillSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_refreshingRoster && SelectedCharacter is { } character) RefreshInheritedSkills(character);
    }
    private void RosterSkillList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingRoster) return;
        _selectedRosterSkill = (RosterSkillList.SelectedItem as SkillChoice)?.Hash;
        RemoveRosterSkillButton.IsEnabled = _selectedRosterSkill.HasValue
            && RosterCatalog.Skill(_selectedRosterSkill.Value) is { Inheritable: true };
    }
}
