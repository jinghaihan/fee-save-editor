namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public bool CanMoveRosterCharacter(int index)
    {
        try { ValidateInactiveRosterCharacter(index); return true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return false; }
    }

    public EngageSave WithRosterForce(int index, UnitForce force)
    {
        if (force is not (UnitForce.Absent or UnitForce.Dead or UnitForce.Lost))
            throw new ArgumentException("Choose an inactive force: Absent, Dead or Lost.");
        var layout = ValidateInactiveRosterCharacter(index);
        var entry = GetCharacter(layout, index);
        if (entry.Character.Force == force) return this;
        return RewriteRosterPool(layout, index, force, _bytes.AsSpan(entry.Start, entry.End - entry.Start).ToArray());
    }

    public bool CanDeleteRosterCharacter(int index)
    {
        try { ValidateRosterDeletion(index); return true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return false; }
    }

    public EngageSave WithoutRosterCharacter(int index)
    {
        ValidateRosterDeletion(index);
        // Return equipment to its inventory before removing the unit record.
        var edited = WithRosterEquipment(index, new(RosterEquipmentKind.None, 0));
        return edited.RewriteRosterPool(RosterLayout.Read(edited, edited._bytes), index, null, null);
    }

    private RosterLayout ValidateInactiveRosterCharacter(int index)
    {
        var layout = RosterLayout.Read(this, _bytes);
        ValidateOutOfBattleRoster(layout);
        var character = GetCharacter(layout, index).Character;
        var person = RosterCatalog.Person(character.PersonHash);
        if (person is null || person.Id == "PID_リュール"
            || character.Force is not (UnitForce.Absent or UnitForce.Dead or UnitForce.Lost)
            || (character.StatusFlags & ProtectedRecoveryStatus) != 0)
            throw new ArgumentException("The protagonist, deployed, guest and story-restricted characters cannot be managed.");
        if (layout.Characters.Count(entry => entry.Character.PersonHash == character.PersonHash) != 1)
            throw new InvalidDataException("Duplicate character records cannot be managed.");
        var equipment = CharacterEquipmentLayout.Read(this, _bytes);
        if (equipment[index].Links.PartnerEmblemInstance != 0)
            throw new ArgumentException("End Engage+ in-game before managing this character.");
        return layout;
    }

    private void ValidateRosterDeletion(int index)
    {
        var layout = ValidateInactiveRosterCharacter(index);
        var character = GetCharacter(layout, index).Character;
        if (character.Items.Any(slot => slot.Item is { } item && RosterCatalog.Item(item.ItemHash) is not { EngageOnly: true }))
            throw new ArgumentException("Move the character's items to the convoy or clear the item slots before deletion.");
        string pid = RosterCatalog.Person(character.PersonHash)!.Id;
        if (ReadEmblems().Any(holder => holder.PactPartner == pid)
            || layout.Characters.Any(entry => entry.AttackTarget == character.PersonHash)
            || CharacterEquipmentLayout.Read(this, _bytes).Any(location => location.Target == character.PersonHash)
            || layout.Characters.Any(entry => ReadUInt32(_bytes, entry.End - 6) != 0))
            throw new ArgumentException("Pact partners or characters with saved target/owner links cannot be deleted.");
        var equipment = ReadRosterEquipment(index);
        if (!ReadRosterEquipmentOptions(index).Any(option => option.Selection == equipment))
            throw new ArgumentException("The character's special equipment cannot be safely returned.");
    }

    private EngageSave RewriteRosterPool(RosterLayout layout, int? removedIndex, UnitForce? destination, byte[]? record)
    {
        using var output = new MemoryStream();
        output.Write(_bytes.AsSpan(layout.Section.PayloadOffset, 32));
        foreach (var force in Enum.GetValues<UnitForce>())
        {
            var group = layout.Characters.Where(entry => entry.Character.Index != removedIndex && entry.Character.Force == force).ToArray();
            bool append = record is not null && destination == force;
            int count = group.Length + (append ? 1 : 0);
            if (count == 0) continue;
            if (count > byte.MaxValue) throw new InvalidDataException("The destination force is full.");
            output.WriteByte((byte)force);
            output.WriteByte((byte)count);
            foreach (var entry in group) output.Write(_bytes.AsSpan(entry.Start, entry.End - entry.Start));
            if (append) output.Write(record!);
        }
        output.WriteByte(255);
        var edited = ReplaceSection(layout.Section, output.ToArray());
        edited.ReadRoster();
        edited.ReadCharacterRingLinks();
        return edited;
    }
}
