using System.Buffers.Binary;

namespace FeeEditor.Core;

internal sealed class SaveReader(byte[] bytes, int position, int end)
{
    private static readonly System.Text.Encoding Unicode = new System.Text.UnicodeEncoding(false, false, true);
    public int Position { get; private set; } = position;
    public int Remaining => end - Position;
    public void Skip(int count)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException("A roster field extends beyond its record.");
        Position += count;
    }
    public byte Byte()
    {
        int offset = Position;
        Skip(1);
        return bytes[offset];
    }
    public ushort UInt16()
    {
        int offset = Position;
        Skip(2);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }
    public uint UInt32()
    {
        int offset = Position;
        Skip(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }
    public uint? Reference() => UInt16() switch
    {
        0xccdb => null,
        0xefcd => UInt32(),
        _ => throw new InvalidDataException($"Unsupported roster reference encoding at 0x{Position - 2:X}.")
    };
    public string? String()
    {
        uint length = UInt32();
        if (length == uint.MaxValue)
            return null;
        if (length > 4096 || (length & 1) != 0)
            throw new InvalidDataException("Invalid character string length.");
        int start = Position;
        Skip((int)length);
        try { return Unicode.GetString(bytes, start, (int)length); }
        catch (System.Text.DecoderFallbackException error)
        { throw new InvalidDataException("Invalid character string encoding.", error); }
    }
}
