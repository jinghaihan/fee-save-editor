using System.Buffers.Binary;
using System.Text;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private EngageSave WithNewCharacterBonds(string personId)
    {
        var edited = this;
        var owned = OwnedEmblemLayout.Read(this, _bytes).Select(god => god.BondHolderId).ToHashSet();
        foreach (var holder in ReadEmblems().Where(holder => owned.Contains(holder.InstanceId)
            && EmblemCreationCatalog.Emblems.Any(row => row.Id == holder.EmblemId) && holder.Bonds.All(bond => bond.PersonId != personId)))
        {
            if (holder.Bonds.Count >= EmblemCreationCatalog.MaxBondsPerHolder)
                throw new InvalidDataException("The Emblem holder has no free character-bond slot.");
            var layout = EmblemLayout.Read(edited, edited._bytes);
            var location = layout.Holders.Single(row => row.InstanceId == holder.InstanceId);
            using var record = new MemoryStream();
            using (var writer = new BinaryWriter(record, Encoding.UTF8, leaveOpen: true))
                WriteInitialCharacterBond(writer, personId);
            using var output = new MemoryStream();
            output.Write(edited._bytes.AsSpan(layout.Section.PayloadOffset, location.End - layout.Section.PayloadOffset));
            output.Write(record.ToArray());
            output.Write(edited._bytes.AsSpan(location.End, layout.Section.PayloadOffset + layout.Section.Length - location.End));
            byte[] payload = output.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(location.CountOffset - layout.Section.PayloadOffset), checked((ushort)(holder.Bonds.Count + 1)));
            edited = edited.ReplaceSection(layout.Section, payload);
        }
        edited.ReadEmblems();
        return edited;
    }
}
