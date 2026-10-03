using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private const ulong DeadStatusMask = 0x10000a00UL;
    private const ulong NeverSortieStatus = 1UL << 3;
    private const ulong GuestStatus = 1UL << 21;
    private const ulong DispositionGuestStatus = 1UL << 22;
    private const ulong RelayLeaveStatus = 1UL << 39;
    private const ulong SummonStatus = 1UL << 45;
    private const ulong ProtectedRecoveryStatus = NeverSortieStatus | GuestStatus | DispositionGuestStatus | RelayLeaveStatus | SummonStatus;

    public bool CanRestoreRosterCharacter(int index)
    {
        try
        {
            var layout = RosterLayout.Read(this, _bytes);
            ValidateRosterRecovery(layout, GetCharacter(layout, index).Character);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    public EngageSave RestoreRosterCharacter(int index)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        ValidateRosterRecovery(layout, entry.Character);
        byte[] restored = _bytes.AsSpan(entry.Start, entry.End - entry.Start).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(restored.AsSpan(36), entry.Character.StatusFlags & ~DeadStatusMask);
        restored[entry.LevelOffset + 2 - entry.Start] = (byte)RosterMaximumHP(index);

        if (entry.Character.Force == UnitForce.Absent)
        {
            byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
            restored.CopyTo(payload, entry.Start - layout.Section.PayloadOffset);
            return ReplaceSection(layout.Section, payload);
        }

        using var output = new MemoryStream();
        output.Write(_bytes.AsSpan(layout.Section.PayloadOffset, 32));
        for (int force = 0; force <= (int)UnitForce.Temporary; force++)
        {
            var group = layout.Characters.Where(character => character.Character.Index != index
                && (int)character.Character.Force == force).ToArray();
            bool includeRestored = force == (int)UnitForce.Absent;
            int count = group.Length + (includeRestored ? 1 : 0);
            if (count == 0) continue;
            output.WriteByte((byte)force);
            output.WriteByte((byte)count);
            foreach (var character in group)
                output.Write(_bytes.AsSpan(character.Start, character.End - character.Start));
            if (includeRestored) output.Write(restored);
        }
        output.WriteByte(255);
        var edited = ReplaceSection(layout.Section, output.ToArray());
        var characterAfter = edited.ReadRoster().Single(character => character.PersonHash == entry.Character.PersonHash);
        if (characterAfter.Force != UnitForce.Absent || characterAfter.Availability != RosterAvailability.Available)
            throw new InvalidDataException("Character restoration did not survive serialization.");
        return edited;
    }

    private void ValidateRosterRecovery(RosterLayout layout, RosterCharacter character)
    {
        if (RosterCatalog.Person(character.PersonHash) is null || character.Force is not (UnitForce.Absent or UnitForce.Dead or UnitForce.Lost)
            || character.Availability is not (RosterAvailability.Dead or RosterAvailability.Lost))
            throw new ArgumentException("Select an existing dead or lost playable character.");
        if ((character.StatusFlags & ProtectedRecoveryStatus) != 0)
            throw new ArgumentException("Guest, summoned, relay or story-restricted characters cannot be restored.");
        if (RosterCatalog.Class(character.ClassHash) is null)
            throw new InvalidDataException("Character restoration requires a verified class for maximum HP.");
        if (layout.Characters.Count(entry => entry.Character.PersonHash == character.PersonHash) != 1)
            throw new InvalidDataException("The save contains duplicate records for this character.");
        ValidateOutOfBattleRoster(layout);
    }

    private void ValidateOutOfBattleRoster(RosterLayout layout)
    {
        var user = Sections.Where(section => section.Name == "USER").ToArray();
        if (user.Length != 1 || user[0].Length < 37 || ReadUInt32(_bytes, user[0].PayloadOffset) != 20)
            throw new InvalidDataException("Roster management requires a verified USER section.");
        int context = user[0].PayloadOffset + 32;
        uint status = ReadUInt32(_bytes, context);
        byte sequence = _bytes[context + 4];
        if ((status & 2) != 0 || sequence is not (1 or 4 or 6)
            || layout.Characters.Any(entry => entry.Character.Force <= UnitForce.Ally))
            throw new ArgumentException("Manage characters and Emblems in an out-of-battle chapter, Somniel or world-map save.");
    }
}
