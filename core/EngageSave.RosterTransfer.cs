using System.Text.Json;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public byte[] ExportRosterCharacter(int index)
    {
        var character = GetCharacter(RosterLayout.Read(this, _bytes), index).Character;
        CheckTransferCharacter(character);
        var progress = character.Progress;
        var transfer = new RosterTransfer
        {
            Format = "FeeEditor.RosterCharacter", Version = 1, GameVersion = GameVersion!.Value,
            PersonHash = character.PersonHash, Gender = progress.Gender, ClassHash = character.ClassHash,
            Values = character.Values, PersonalStats = character.Stats.Select(stat => stat.PersonalValue).ToArray(),
            CurrentHP = progress.CurrentHP, InternalLevel = progress.InternalLevel,
            Proficiencies = progress.Proficiencies, SelectedWeapons = progress.SelectedWeapons,
            ClassSkill = progress.ClassSkill,
            InheritedSkills = progress.InheritedSkills.Select(skill => skill.Hash).ToArray(),
            EquippedSkills = progress.EquippedSkills.Select(skill => skill.Hash).ToArray(),
            Items = character.Items.Select(slot => RosterTransfer.Item(slot.Item)).ToArray()
        };
        return JsonSerializer.SerializeToUtf8Bytes(transfer, RosterTransfer.JsonOptions);
    }

    public EngageSave ImportRosterCharacter(int index, ReadOnlySpan<byte> bytes)
    {
        var transfer = RosterTransfer.Read(bytes);
        var layout = RosterLayout.Read(this, _bytes);
        var character = GetCharacter(layout, index).Character;
        CheckTransferCharacter(character);
        ValidateTransfer(character, transfer);
        var job = RosterCatalog.Class(transfer.ClassHash)!;
        var edited = this;
        if (transfer.ClassHash != character.ClassHash || transfer.SelectedWeapons != character.Progress.SelectedWeapons)
        {
            edited = edited.WithRosterClass(index, job.Id, TransferWeapons(job, transfer.SelectedWeapons));
        }
        edited = edited.WithRosterValues(index, transfer.Values);
        for (int stat = 0; stat < transfer.PersonalStats.Length; stat++)
            if (stat != (int)RosterStat.Sight)
                edited = edited.WithRosterPersonalStat(index, (RosterStat)stat, transfer.PersonalStats[stat]);
        if (transfer.CurrentHP < 0 || transfer.CurrentHP > edited.RosterMaximumHP(index))
            throw new ArgumentException("Imported current HP must not exceed maximum HP.");
        edited = edited.WithRosterCondition(index, transfer.InternalLevel, transfer.CurrentHP);
        edited = edited.WithRosterProficiencies(index, transfer.Proficiencies & 510);
        if (edited.ReadRoster()[index].Progress.SelectedWeapons != transfer.SelectedWeapons)
        {
            var currentLayout = RosterLayout.Read(edited, edited._bytes);
            var entry = GetCharacter(currentLayout, index);
            byte[] payload = edited._bytes.AsSpan(currentLayout.Section.PayloadOffset, currentLayout.Section.Length).ToArray();
            Write32(payload, entry.Progress.MasksOffset + 8 - currentLayout.Section.PayloadOffset, transfer.SelectedWeapons);
            edited = edited.ReplaceSection(currentLayout.Section, payload);
        }
        if (edited.ReadRoster()[index].Progress.ClassSkill != transfer.ClassSkill)
            edited = edited.WithRosterClassSkill(index, transfer.ClassSkill.HasValue);
        edited = edited.ImportTransferSkills(index, transfer);
        for (int slot = 0; slot < transfer.Items.Length; slot++)
        {
            var current = edited.ReadRoster()[index].Items[slot].Item;
            var requested = transfer.Items[slot];
            if (RosterTransfer.Item(current) == requested) continue;
            var item = requested is null ? null : new InventoryItem(requested.ItemHash, requested.Uses,
                requested.RefineLevel, current?.Flags ?? 0, requested.EngravingHash);
            var currentLayout = RosterLayout.Read(edited, edited._bytes);
            edited = edited.ReplaceCharacterItem(currentLayout, GetCharacter(currentLayout, index), slot, item);
        }
        // Reparse the complete save before returning; the caller's save remains immutable on failure.
        var verified = Parse(edited.Serialize());
        byte[] expected = JsonSerializer.SerializeToUtf8Bytes(transfer, RosterTransfer.JsonOptions);
        if (!verified.ExportRosterCharacter(index).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("The imported character values did not survive serialization.");
        return verified;
    }

    private static void CheckTransferCharacter(RosterCharacter character)
    {
        if (character.Force is UnitForce.Enemy or UnitForce.Temporary || RosterCatalog.Person(character.PersonHash) is null)
            throw new ArgumentException("Select a known playable roster character.");
    }

    private void ValidateTransfer(RosterCharacter target, RosterTransfer transfer)
    {
        if (transfer.Format != "FeeEditor.RosterCharacter" || transfer.Version != 1 || transfer.GameVersion != GameVersion)
            throw new InvalidDataException("The character file format or game version does not match this save.");
        if (transfer.PersonHash != target.PersonHash || transfer.Gender != target.Progress.Gender)
            throw new ArgumentException("Import into the same character and gender; character identity is not replaced.");
        if (transfer.Values is null || transfer.PersonalStats is null || transfer.PersonalStats.Length != 11
            || transfer.Items is null || transfer.Items.Length != 8
            || transfer.InheritedSkills is null || transfer.EquippedSkills is null)
            throw new InvalidDataException("The character file has missing or incorrectly sized fields.");
        var job = ReadRosterClasses(target.Index).FirstOrDefault(job => job.Hash == transfer.ClassHash)
            ?? throw new ArgumentException("This character cannot use the imported class.");
        TransferWeapons(job, transfer.SelectedWeapons);
        var values = transfer.Values;
        if (values.Level < 1 || values.Level > job.MaxLevel || values.Experience is < 0 or > 99
            || values.Level == job.MaxLevel && values.Experience != 0 || values.SkillPoints is < 0 or > 9999
            || transfer.InternalLevel is < -100 or > 100)
            throw new ArgumentException("Invalid imported level, experience, SP or internal level.");
        for (int stat = 0; stat < transfer.PersonalStats.Length; stat++)
        {
            int value = transfer.PersonalStats[stat];
            if (stat == (int)RosterStat.Sight)
            {
                if (value != target.Stats[stat].PersonalValue)
                    throw new ArgumentException("Sight is not an editable stat.");
                continue;
            }
            var range = RosterStats.PersonalRange((RosterStat)stat, job);
            if (value < range.Minimum || value > range.Maximum)
                throw new ArgumentException($"Imported personal {(RosterStat)stat} is outside its limits.");
        }
        if ((transfer.Proficiencies & ~510u) != (target.Progress.Proficiencies & ~510u))
            throw new ArgumentException("Reserved proficiency bits must not change.");
        if (transfer.ClassSkill.HasValue && (string.IsNullOrEmpty(job.LearningSkill)
            || transfer.ClassSkill != ItemCatalog.Hash(job.LearningSkill)
            || values.Level < (job.MaxLevel >= 40 ? 25 : 5)))
            throw new ArgumentException("The imported class skill is not available at this class level.");
        ValidateTransferSkills(transfer.InheritedSkills, target.Progress.InheritedSkills, 1280, equipped: false);
        ValidateTransferSkills(transfer.EquippedSkills, target.Progress.EquippedSkills, 2, equipped: true);
        if (transfer.EquippedSkills.Any(hash => !transfer.InheritedSkills.Contains(hash)
            && (!target.Progress.EquippedSkills.Any(skill => skill.Hash == hash)
                || target.Progress.InheritedSkills.Any(skill => skill.Hash == hash))))
            throw new ArgumentException("Unlock imported skills before equipping them.");
        ValidateTransferItems(target, transfer.Items);
    }

    private void ValidateTransferItems(RosterCharacter target, RosterTransferItem?[] items)
    {
        for (int slot = 0; slot < items.Length; slot++)
        {
            var requested = items[slot];
            var current = target.Items[slot].Item;
            if (RosterTransfer.Item(current) == requested) continue;
            CheckCharacterItemEditable(current);
            if (requested is null) continue;
            if (RosterCatalog.Item(requested.ItemHash) is { EngageOnly: true })
                throw new ArgumentException("Engage weapons and reserved slots are not portable character items.");
            InventoryLayout.Validate(new(requested.ItemHash, requested.Uses, requested.RefineLevel, 0, requested.EngravingHash));
            if (requested.EngravingHash is not uint hash) continue;
            var engraving = EngravingCatalog.Find(hash);
            if (engraving is null
                || items.Count(item => item?.EngravingHash is uint other && EngravingCatalog.Find(other)?.Id == engraving.Id) != 1)
                throw new ArgumentException("Invalid or duplicate imported engraving.");
            // Import never silently removes an engraving from the convoy or another character.
            if (ReadInventory().Any(row => row.Item?.EngravingHash is uint other && EngravingCatalog.Find(other)?.Id == engraving.Id)
                || ReadRoster().Where(row => row.Index != target.Index && row.Force is not UnitForce.Enemy and not UnitForce.Temporary)
                    .Any(row => row.Items.Any(item => item.Item?.EngravingHash is uint other && EngravingCatalog.Find(other)?.Id == engraving.Id)))
                throw new ArgumentException("An imported engraving is already used elsewhere in this save.");
        }
    }

    private static void ValidateTransferSkills(uint[] hashes, IReadOnlyList<RosterSkill> existing, int maximum, bool equipped)
    {
        if (hashes.SequenceEqual(existing.Select(skill => skill.Hash))) return;
        if (hashes.Length > maximum || hashes.Distinct().Count() != hashes.Length
            || hashes.Any(hash => RosterCatalog.Skill(hash) is not { Inheritable: true }))
            throw new ArgumentException("Invalid imported inherited skills.");
        if (equipped && hashes.Select(hash => RosterCatalog.Skill(hash)!.Family).Distinct().Count() != hashes.Length)
            throw new ArgumentException("Different tiers of the same skill cannot occupy both slots.");
    }

    private static uint TransferWeapons(ClassDefinition job, uint saved)
    {
        var variants = job.WeaponVariants();
        if (variants.Contains(saved)) return saved;
        // Native DLC units omit the Special bit; fixed classes may store zero and derive their weapon mask.
        uint special = 1u << (int)WeaponType.Special;
        if (job.Weapons[(int)WeaponType.Special] == 1 && variants.Contains(saved | special))
            return saved | special;
        if (saved == 0 && variants.Count == 1) return variants[0];
        throw new ArgumentException("Invalid imported class weapon combination.");
    }

    private EngageSave ImportTransferSkills(int index, RosterTransfer transfer)
    {
        var edited = this;
        var layout = RosterLayout.Read(edited, edited._bytes);
        var entry = GetCharacter(layout, index);
        var existing = entry.Character.Progress.InheritedSkills;
        if (!transfer.InheritedSkills.SequenceEqual(existing.Select(skill => skill.Hash)))
        {
            var skills = transfer.InheritedSkills.Select(hash => existing.FirstOrDefault(skill => skill.Hash == hash)
                ?? new RosterSkill(hash, 0, 11)).ToArray();
            edited = edited.ReplaceCharacterRange(layout, entry, entry.Progress.PoolStart, entry.Progress.PoolEnd, SkillBytes(skills));
        }
        var equipped = edited.ReadRoster()[index].Progress.EquippedSkills;
        return edited.ReplaceEquippedSkills(index, transfer.EquippedSkills.Select(hash => equipped.FirstOrDefault(skill => skill.Hash == hash)
            ?? new RosterSkill(hash, 0, 11)).ToArray());
    }
}
