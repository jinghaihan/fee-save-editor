using System.Text;

namespace FeeEditor.Core;

internal sealed record GameVariableEntry(string? Key, byte Type, int ValueOffset);

internal sealed class GameVariableLayout
{
    public required SaveSection Section { get; init; }
    public required int Start { get; init; }
    public required uint Length { get; init; }
    public required int CountOffset { get; init; }
    public required uint Count { get; init; }
    public required GameVariableEntry[] Entries { get; init; }
    public int End => Start + (int)Length;

    public static GameVariableLayout Read(EngageSave save, byte[] bytes)
    {
        var section = EmblemLayout.GetSection(save, "USER");
        var reader = new SaveReader(bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.UInt32() != 20) throw new InvalidDataException("Unsupported USER version for game-variable editing.");
        reader.Skip(45);
        int start = reader.Position;
        uint length = reader.UInt32();
        if (length < 40 || length > reader.Remaining + 4)
            throw new InvalidDataException("Invalid game-variable block length.");
        var variables = new SaveReader(bytes, reader.Position, start + (int)length);
        if (variables.UInt32() != 0) throw new InvalidDataException("Unsupported game-variable block version.");
        variables.Skip(28);
        int countOffset = variables.Position;
        uint count = variables.UInt32();
        if (count > variables.Remaining / 9) throw new InvalidDataException("Invalid game-variable count.");
        var entries = new List<GameVariableEntry>();
        for (uint index = 0; index < count; index++)
        {
            string? key = variables.String();
            byte type = variables.Byte();
            entries.Add(new(key, type, variables.Position));
            if (type == 0) variables.Skip(4);
            else if (type == 1) variables.String();
            else throw new InvalidDataException("Unsupported game-variable type.");
        }
        if (variables.Remaining != 0) throw new InvalidDataException("Unrecognized game-variable trailing data.");
        return new() { Section = section, Start = start, Length = length, CountOffset = countOffset, Count = count, Entries = entries.ToArray() };
    }

    public int? IntegerOffset(string key)
    {
        var matching = Entries.Where(entry => entry.Key == key).ToArray();
        if (matching.Length > 1 || matching.Any(entry => entry.Type != 0))
            throw new InvalidDataException($"Duplicated or noninteger game variable: {key}");
        return matching.Length == 0 ? null : matching[0].ValueOffset;
    }
}

public sealed partial class EngageSave
{
    private EngageSave WithGameIntegerValues(IReadOnlyDictionary<string, int> values)
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        using var addition = new MemoryStream();
        using var writer = new BinaryWriter(addition, Encoding.UTF8, leaveOpen: true);
        uint added = 0;
        bool changed = false;
        foreach (var (key, value) in values)
        {
            if (layout.IntegerOffset(key) is int offset)
            {
                if (unchecked((int)ReadUInt32(_bytes, offset)) == value) continue;
                Write32(payload, offset - layout.Section.PayloadOffset, unchecked((uint)value));
                changed = true;
            }
            else if (value != 0)
            {
                byte[] name = Encoding.Unicode.GetBytes(key);
                writer.Write((uint)name.Length); writer.Write(name); writer.Write((byte)0); writer.Write(value);
                added++;
            }
        }
        if (!changed && added == 0) return this;
        if (added > 0)
        {
            byte[] records = addition.ToArray();
            Write32(payload, layout.Start - layout.Section.PayloadOffset, checked(layout.Length + (uint)records.Length));
            Write32(payload, layout.CountOffset - layout.Section.PayloadOffset, checked(layout.Count + added));
            int insert = layout.End - layout.Section.PayloadOffset;
            payload = [.. payload.AsSpan(0, insert), .. records, .. payload.AsSpan(insert)];
        }
        return ReplaceSection(layout.Section, payload);
    }
}
