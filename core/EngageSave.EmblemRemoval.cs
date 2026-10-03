using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public bool CanRemoveEmblem(uint instance)
    {
        try { ValidateEmblemRemoval(instance); return true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return false; }
    }

    public EngageSave WithoutEmblem(uint instance)
    {
        ValidateEmblemRemoval(instance);
        var edited = this;
        var owner = ReadCharacterRingLinks().FirstOrDefault(link => link.EmblemInstance == instance);
        if (owner is not null)
            edited = edited.WithRosterEquipment(owner.CharacterIndex, new(RosterEquipmentKind.None, 0));
        var gods = OwnedEmblemLayout.Read(edited, edited._bytes);
        var entry = gods.Single(god => god.InstanceId == instance);
        var section = EmblemLayout.GetSection(edited, "GOD");
        using var output = new MemoryStream();
        output.Write(edited._bytes.AsSpan(section.PayloadOffset, entry.Start - section.PayloadOffset));
        output.Write(edited._bytes.AsSpan(entry.End, section.PayloadOffset + section.Length - entry.End));
        byte[] payload = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), checked((uint)gods.Count - 1));
        edited = edited.ReplaceSection(section, payload);
        edited.ValidateEmblemCreation(OwnedEmblemLayout.Read(edited, edited._bytes), edited.ReadEmblems());
        return edited;
    }

    private void ValidateEmblemRemoval(uint instance)
    {
        ValidateOutOfBattleRoster(RosterLayout.Read(this, _bytes));
        var gods = OwnedEmblemLayout.Read(this, _bytes);
        ValidateEmblemCreation(gods, ReadEmblems());
        var entry = gods.FirstOrDefault(god => god.InstanceId == instance)
            ?? throw new ArgumentException("Select an owned Emblem instance.", nameof(instance));
        if (EmblemCreationCatalog.Emblems.All(row => ItemCatalog.Hash(row.Id) != entry.GodHash)
            || entry.Darkness || entry.Reserved || entry.Escaping
            || ReadCharacterRingLinks().Any(link => link.PartnerEmblemInstance == instance
                || link.EmblemInstance == instance && link.PartnerEmblemInstance != 0))
            throw new ArgumentException("Story-reserved, unknown and Engage+ Emblems cannot be removed.");
    }
}
