namespace FeeEditor.Core;

public sealed record CharacterSupport(string Key, SupportRank Rank, int Points, int Score);
internal sealed record SupportLocation(CharacterSupport Support, int ValuesOffset);
internal sealed record SupportLayout(SaveSection Section, IReadOnlyList<SupportLocation> Entries)
{
    public static SupportLayout Read(EngageSave save, byte[] bytes)
    {
        var section = EmblemLayout.GetSection(save, "UREL");
        var reader = EmblemLayout.PoolReader(section, bytes, 1);
        uint count = reader.UInt32();
        if (count > 10000 || count > reader.Remaining / 11)
            throw new InvalidDataException("Invalid character support count.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<SupportLocation>();
        for (uint index = 0; index < count; index++)
        {
            string key = reader.String() ?? throw new InvalidDataException("A character support has no key.");
            var definition = SupportCatalog.Pair(key);
            if (!keys.Add(definition?.Key ?? key) || reader.UInt32() != 1)
                throw new InvalidDataException("Duplicate character support or unsupported record version.");
            int offset = reader.Position;
            int rank = reader.Byte();
            if (rank > 4) throw new InvalidDataException("Unsupported character support rank.");
            int points = unchecked((sbyte)reader.Byte()), score = unchecked((sbyte)reader.Byte());
            entries.Add(new(new(key, (SupportRank)rank, points, score), offset));
        }
        if (reader.Remaining != 0) throw new InvalidDataException("Unrecognized character support trailing data.");
        return new(section, entries.AsReadOnly());
    }
}
