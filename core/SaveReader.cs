using System.Buffers.Binary;

namespace FeeEditor.Core;

internal sealed class SaveReader(byte[] bytes, int position, int end)
{
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
}
