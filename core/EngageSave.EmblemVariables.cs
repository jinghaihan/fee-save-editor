using System.Text;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private EngageSave WithEmblemCapVariable(string key)
    {
        var section = EmblemLayout.GetSection(this, "USER");
        var reader = new SaveReader(_bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.UInt32() != 20) throw new InvalidDataException("Unsupported USER version for bond level-cap editing.");
        reader.Skip(45);
        int start = reader.Position;
        uint length = reader.UInt32();
        if (length < 40 || length > reader.Remaining + 4)
            throw new InvalidDataException("Invalid game-variable block length.");
        int end = start + (int)length;
        var variables = new SaveReader(_bytes, reader.Position, end);
        if (variables.UInt32() != 0) throw new InvalidDataException("Unsupported game-variable block version.");
        variables.Skip(28);
        int countOffset = variables.Position;
        uint count = variables.UInt32();
        if (count > variables.Remaining / 9) throw new InvalidDataException("Invalid game-variable count.");
        int? valueOffset = null;
        for (uint index = 0; index < count; index++)
        {
            string? name = variables.String();
            byte type = variables.Byte();
            if (name == key)
            {
                if (valueOffset.HasValue || type != 0) throw new InvalidDataException("Invalid bond level-cap variable.");
                valueOffset = variables.Position;
            }
            if (type == 0) variables.Skip(4);
            else if (type == 1) variables.String();
            else throw new InvalidDataException("Unsupported game-variable type.");
        }
        if (variables.Remaining != 0) throw new InvalidDataException("Unrecognized game-variable trailing data.");
        byte[] payload = _bytes.AsSpan(section.PayloadOffset, section.Length).ToArray();
        if (valueOffset is int offset)
        {
            if (ReadUInt32(_bytes, offset) != 0) return this;
            Write32(payload, offset - section.PayloadOffset, 1);
            return ReplaceSection(section, payload);
        }
        using var addition = new MemoryStream();
        using (var writer = new BinaryWriter(addition, Encoding.UTF8, leaveOpen: true))
        {
            byte[] name = Encoding.Unicode.GetBytes(key);
            writer.Write((uint)name.Length); writer.Write(name); writer.Write((byte)0); writer.Write(1);
        }
        byte[] record = addition.ToArray();
        Write32(payload, start - section.PayloadOffset, checked(length + (uint)record.Length));
        Write32(payload, countOffset - section.PayloadOffset, checked(count + 1));
        int insert = end - section.PayloadOffset;
        return ReplaceSection(section, [.. payload.AsSpan(0, insert), .. record, .. payload.AsSpan(insert)]);
    }
}
