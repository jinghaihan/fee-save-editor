namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<ClassDefinition> ReadRosterClasses(int index)
    {
        var character = GetCharacter(RosterLayout.Read(this, _bytes), index).Character;
        return RosterCatalog.ClassesFor(character.PersonHash, character.Progress.Gender);
    }

    public EngageSave WithRosterClass(int index, string classId, uint weapons)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var character = entry.Character;
        var job = ReadRosterClasses(index).FirstOrDefault(job => job.Id == classId)
            ?? throw new ArgumentException("This character cannot use the selected class.", nameof(classId));
        if (!job.WeaponVariants().Contains(weapons))
            throw new ArgumentException("Select a valid weapon combination for this class.", nameof(weapons));
        bool changedClass = character.ClassHash != job.Hash;
        if (!changedClass && (character.Progress.SelectedWeapons == weapons
            || character.Progress.SelectedWeapons == 0 && job.WeaponVariants().Count == 1))
            return this;

        if (changedClass)
        {
            var old = RosterCatalog.Class(character.ClassHash)
                ?? throw new ArgumentException("The existing class is not recognized.");
            int level = 1;
            if (job.MaxLevel >= 40 && (old.Advanced || old.MaxLevel >= 40 && character.Values.Level >= 21))
                level = 21;
            int cap = ReadMainValues().Difficulty switch { Difficulty.Maddening => 50, Difficulty.Hard => 40, _ => 30 };
            int internalLevel = Math.Max(0, Math.Clamp(character.Progress.InternalLevel + character.Values.Level - 1, 0, cap) - level + 1);
            byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
            int relative = layout.Section.PayloadOffset;
            Write32(payload, entry.BaseStatsOffset - 8 - relative, job.Hash);
            payload[entry.LevelOffset - relative] = (byte)level;
            payload[entry.LevelOffset + 1 - relative] = 0;
            payload[entry.Progress.InternalLevelOffset - relative] = (byte)internalLevel;
            int storedHP = (sbyte)_bytes[entry.BaseStatsOffset];
            int hpLimit = Math.Clamp(job.Limits[0] + RosterCatalog.Person(character.PersonHash)!.LimitModifiers[0], 1, 255);
            int hp = Math.Clamp(Math.Clamp(job.BaseStats[0] + storedHP, 1, hpLimit) + entry.Progress.HPBonus, 1, 255);
            payload[entry.LevelOffset + 2 - relative] = (byte)Math.Min(character.Progress.CurrentHP, hp);
            Write32(payload, entry.Progress.MasksOffset + 4 - relative, character.Progress.Proficiencies | weapons);
            Write32(payload, entry.Progress.MasksOffset + 8 - relative, weapons);
            var edited = ReplaceSection(layout.Section, payload);
            return edited.WithRosterClassSkill(index, unlocked: false);
        }
        byte[] variants = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        Write32(variants, entry.Progress.MasksOffset + 4 - layout.Section.PayloadOffset, character.Progress.Proficiencies | weapons);
        Write32(variants, entry.Progress.MasksOffset + 8 - layout.Section.PayloadOffset, weapons);
        return ReplaceSection(layout.Section, variants);
    }

    public EngageSave WithRosterClassSkill(int index, bool unlocked)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var job = RosterCatalog.Class(entry.Character.ClassHash) ?? throw new ArgumentException("Unknown class.");
        uint? hash = null;
        if (unlocked)
        {
            if (string.IsNullOrEmpty(job.LearningSkill) || entry.Character.Values.Level < (job.MaxLevel >= 40 ? 25 : 5))
                throw new ArgumentException("The class skill requires the class's learning level (5 or 25).");
            hash = ItemCatalog.Hash(job.LearningSkill);
        }
        if (hash == entry.Character.Progress.ClassSkill)
            return this;
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(hash.HasValue);
            if (hash.HasValue) { writer.Write((ushort)0xefcd); writer.Write(hash.Value); }
        }
        return ReplaceCharacterRange(layout, entry, entry.Progress.ClassSkillStart, entry.Progress.ClassSkillEnd, output.ToArray());
    }

    private EngageSave ReplaceCharacterRange(RosterLayout layout, CharacterLayout entry, int start, int end, byte[] replacement)
    {
        int delta = replacement.Length - (end - start);
        using var output = new MemoryStream();
        output.Write(_bytes.AsSpan(layout.Section.PayloadOffset, start - layout.Section.PayloadOffset));
        output.Write(replacement);
        output.Write(_bytes.AsSpan(end, layout.Section.PayloadOffset + layout.Section.Length - end));
        byte[] payload = output.ToArray();
        Write32(payload, entry.Start - layout.Section.PayloadOffset, checked((uint)(entry.End - entry.Start + delta)));
        var edited = ReplaceSection(layout.Section, payload);
        edited.ReadRoster();
        return edited;
    }
}
