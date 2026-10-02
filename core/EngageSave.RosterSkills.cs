namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public EngageSave WithRosterSkill(int index, string skillId, bool unlocked)
    {
        var definition = RosterCatalog.Skills.FirstOrDefault(skill => skill.Id == skillId && skill.Inheritable)
            ?? throw new ArgumentException("Select a verified inheritable skill.", nameof(skillId));
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var pool = entry.Character.Progress.InheritedSkills;
        if (unlocked && pool.Any(skill => RosterCatalog.Skill(skill.Hash) is { } known
            && known.Family == definition.Family && known.Tier > definition.Tier))
            return this;
        if (unlocked && pool.Any(skill => skill.Hash == definition.Hash)
            || !unlocked && pool.All(skill => skill.Hash != definition.Hash))
            return this;
        var replaced = pool.Where(skill => skill.Hash == definition.Hash || unlocked
            && RosterCatalog.Skill(skill.Hash)?.Family == definition.Family).Select(skill => skill.Hash).ToHashSet();
        var skills = pool.Where(skill => !replaced.Contains(skill.Hash)).ToList();
        if (unlocked) skills.Add(new(definition.Hash, 0, 11));
        if (skills.Count > 1280)
            throw new ArgumentException("The inherited skill pool is full.");
        var edited = ReplaceCharacterRange(layout, entry, entry.Progress.PoolStart, entry.Progress.PoolEnd, SkillBytes(skills));
        var equipped = new List<RosterSkill>();
        foreach (var skill in entry.Character.Progress.EquippedSkills)
        {
            if (!replaced.Contains(skill.Hash)) equipped.Add(skill);
            else if (unlocked) equipped.Add(skill with { Hash = definition.Hash });
        }
        return edited.ReplaceEquippedSkills(index, equipped);
    }

    public EngageSave UnlockAllRosterSkills(int index)
    {
        GetCharacter(RosterLayout.Read(this, _bytes), index);
        var edited = this;
        foreach (var family in RosterCatalog.Skills.Where(skill => skill.Inheritable).GroupBy(skill => skill.Family))
            edited = edited.WithRosterSkill(index, family.OrderByDescending(skill => skill.Tier).First().Id, true);
        return edited;
    }

    public EngageSave WithRosterEquippedSkills(int index, uint? first, uint? second)
    {
        var entry = GetCharacter(RosterLayout.Read(this, _bytes), index);
        var hashes = new[] { first, second }.Where(hash => hash.HasValue).Select(hash => hash!.Value).ToArray();
        if (entry.Character.Progress.EquippedSkills.Select(skill => skill.Hash).SequenceEqual(hashes))
            return this;
        if (hashes.Distinct().Count() != hashes.Length)
            throw new ArgumentException("A skill cannot occupy both inheritance slots.");
        var selected = new List<RosterSkill>();
        foreach (uint hash in hashes)
        {
            var definition = RosterCatalog.Skill(hash);
            if (definition is not { Inheritable: true } && entry.Character.Progress.EquippedSkills.All(skill => skill.Hash != hash))
                throw new ArgumentException("Select an inheritable skill.");
            if (entry.Character.Progress.InheritedSkills.All(skill => skill.Hash != hash)
                && entry.Character.Progress.EquippedSkills.All(skill => skill.Hash != hash))
                throw new ArgumentException("Unlock the skill before equipping it.");
            if (definition is not null && selected.Any(skill => RosterCatalog.Skill(skill.Hash)?.Family == definition.Family))
                throw new ArgumentException("Different tiers of the same skill cannot be equipped together.");
            selected.Add(entry.Character.Progress.EquippedSkills.FirstOrDefault(skill => skill.Hash == hash) ?? new(hash, 0, 11));
        }
        return ReplaceEquippedSkills(index, selected);
    }

    private EngageSave ReplaceEquippedSkills(int index, IReadOnlyList<RosterSkill> skills)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        if (entry.Character.Progress.EquippedSkills.SequenceEqual(skills)) return this;
        return ReplaceCharacterRange(layout, entry, entry.Progress.EquippedStart, entry.Progress.EquippedEnd, SkillBytes(skills));
    }

    private static byte[] SkillBytes(IReadOnlyList<RosterSkill> skills)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1u); writer.Write(skills.Count);
            foreach (var skill in skills)
            {
                writer.Write((ushort)0xefcd); writer.Write(skill.Hash);
                writer.Write(skill.Age); writer.Write(skill.Category);
            }
        }
        return output.ToArray();
    }

    public EngageSave WithRosterProficiencies(int index, uint mask)
    {
        const uint editable = 510;
        if ((mask & ~editable) != 0)
            throw new ArgumentException("Only Sword through Arts proficiencies can be edited.");
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var old = entry.Character.Progress;
        if ((old.Proficiencies & editable) == mask) return this;
        uint active = old.SelectedWeapons;
        var job = RosterCatalog.Class(entry.Character.ClassHash);
        if (active == 0 && job is not null && job.WeaponVariants().Count == 1) active = job.WeaponVariants()[0];
        uint required = (old.OriginalProficiencies | active) & editable;
        if ((mask & required) != required)
            throw new ArgumentException("Innate and current-class weapon proficiencies must be retained.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        Write32(payload, entry.Progress.MasksOffset + 4 - layout.Section.PayloadOffset, (old.Proficiencies & ~editable) | mask);
        return ReplaceSection(layout.Section, payload);
    }

    public int RosterMaximumHP(int index)
    {
        var entry = GetCharacter(RosterLayout.Read(this, _bytes), index);
        return Math.Clamp(entry.Character.Stats[0].Value + entry.Progress.HPBonus, 1, 255);
    }

    public EngageSave WithRosterCondition(int index, int internalLevel, int currentHP)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var old = entry.Character.Progress;
        if (internalLevel == old.InternalLevel && currentHP == old.CurrentHP) return this;
        if (internalLevel != old.InternalLevel && internalLevel is < -100 or > 100)
            throw new ArgumentOutOfRangeException(nameof(internalLevel), "Internal level must be between -100 and 100.");
        if (currentHP != old.CurrentHP && (currentHP < 0 || currentHP > RosterMaximumHP(index)))
            throw new ArgumentOutOfRangeException(nameof(currentHP), "Current HP must not exceed maximum HP.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        payload[entry.Progress.InternalLevelOffset - layout.Section.PayloadOffset] = unchecked((byte)(sbyte)internalLevel);
        payload[entry.LevelOffset + 2 - layout.Section.PayloadOffset] = (byte)currentHP;
        return ReplaceSection(layout.Section, payload);
    }
}
