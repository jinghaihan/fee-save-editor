namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public bool CanRepairRosterClass(int index)
    {
        try { ValidateRosterClassRepair(index); return true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return false; }
    }

    public EngageSave RepairRosterClass(int index)
    {
        var job = ValidateRosterClassRepair(index);
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        byte[] reference = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(reference, 0xefcd);
        Write32(reference, 2, job.Hash);
        var edited = ReplaceCharacterRange(layout, entry, entry.ClassStart, entry.ClassEnd, reference);
        // Re-read offsets after replacing a null (two-byte) class reference.
        layout = RosterLayout.Read(edited, edited._bytes);
        entry = GetCharacter(layout, index);
        byte[] payload = edited._bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int relative = layout.Section.PayloadOffset;
        int level = Math.Clamp(entry.Character.Values.Level, 1, job.MaxLevel);
        payload[entry.LevelOffset - relative] = (byte)level;
        payload[entry.LevelOffset + 1 - relative] = (byte)(level == job.MaxLevel ? 0 : Math.Min(entry.Character.Values.Experience, 99));
        uint weapons = job.WeaponVariants()[0];
        Write32(payload, entry.Progress.MasksOffset + 4 - relative, entry.Character.Progress.Proficiencies | weapons);
        Write32(payload, entry.Progress.MasksOffset + 8 - relative, weapons);
        payload[entry.LevelOffset + 2 - relative] = (byte)Math.Min(entry.Character.Progress.CurrentHP, edited.RosterMaximumHP(index));
        edited = edited.ReplaceSection(layout.Section, payload);
        return edited.WithRosterClassSkill(index, unlocked: false);
    }

    public EngageSave RepairAllRosterClasses()
    {
        var edited = this;
        foreach (var character in ReadRoster())
            if (edited.CanRepairRosterClass(character.Index)) edited = edited.RepairRosterClass(character.Index);
        return edited;
    }

    private ClassDefinition ValidateRosterClassRepair(int index)
    {
        var layout = RosterLayout.Read(this, _bytes);
        ValidateOutOfBattleRoster(layout);
        var character = GetCharacter(layout, index).Character;
        if (RosterCatalog.Class(character.ClassHash) is not null
            || character.Force is not (UnitForce.Absent or UnitForce.Dead or UnitForce.Lost)
            || (character.StatusFlags & ProtectedRecoveryStatus) != 0)
            throw new ArgumentException("Select an inactive playable character with a missing or unknown class.");
        var person = RosterCatalog.Person(character.PersonHash)
            ?? throw new ArgumentException("Unknown characters cannot be repaired.");
        if (layout.Characters.Count(entry => entry.Character.PersonHash == character.PersonHash) != 1)
            throw new InvalidDataException("Duplicate characters cannot be repaired.");
        return RosterCatalog.ClassesFor(person.Hash, character.Progress.Gender).FirstOrDefault(job => job.Id == person.BirthClass)
            ?? throw new InvalidDataException("This character has no verified starting class.");
    }
}
