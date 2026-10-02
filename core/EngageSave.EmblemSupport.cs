namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private EngageSave WithAlearBondSupport(string personId, int bondLevel)
    {
        if (personId == EmblemCatalog.AlearPersonId) return this;
        var section = EmblemLayout.GetSection(this, "UREL");
        var reader = EmblemLayout.PoolReader(section, _bytes, 1);
        uint count = reader.UInt32();
        if (count > 10000 || count > reader.Remaining / 11)
            throw new InvalidDataException("Invalid character support count.");
        int? selectedOffset = null;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (uint index = 0; index < count; index++)
        {
            string key = reader.String() ?? throw new InvalidDataException("A character support has no key.");
            if (!keys.Add(key) || reader.UInt32() != 1)
                throw new InvalidDataException("Duplicate character support or unsupported record version.");
            int offset = reader.Position;
            int rank = reader.Byte();
            reader.Skip(2);
            if (rank > 4) throw new InvalidDataException("Unsupported character support rank.");
            if (key == EmblemCatalog.AlearPersonId + personId || key == personId + EmblemCatalog.AlearPersonId)
            {
                if (selectedOffset.HasValue) throw new InvalidDataException("Duplicate Alear support pair.");
                selectedOffset = offset;
            }
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unrecognized character support trailing data.");
        if (selectedOffset is not int selected)
            throw new ArgumentException("The character has no saved support with Alear; a missing relationship cannot be invented.");
        byte rankValue = bondLevel switch { 1 => 0, 5 => 1, 10 => 2, 20 => 3, 21 => 4,
            _ => throw new ArgumentException("Alear's bond must correspond to a support rank.") };
        if (_bytes[selected] == 4 && rankValue != 4)
            throw new ArgumentException("A Pact support cannot be downgraded through an ordinary bond edit.");
        if (_bytes[selected] == rankValue) return this;
        byte[] payload = _bytes.AsSpan(section.PayloadOffset, section.Length).ToArray();
        payload[selected - section.PayloadOffset] = rankValue;
        payload[selected + 1 - section.PayloadOffset] = 0;
        return ReplaceSection(section, payload);
    }
}
