using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed record InventoryItem(uint ItemHash, int Uses, int RefineLevel, uint Flags, uint? EngravingHash);
public sealed record InventorySlot(int Slot, InventoryItem? Item);

internal sealed class InventoryLayout
{
    public const int MaxSlots = 999;
    public required SaveSection Section { get; init; }
    public required IReadOnlyList<InventorySlot> Slots { get; init; }
    public required int[] Starts { get; init; }
    public required int[] Ends { get; init; }

    public static InventoryLayout Read(EngageSave save, byte[] bytes)
    {
        if (save.Kind != SaveKind.Game || save.FormatVersion != 9)
            throw new InvalidDataException("Convoy editing requires an Engage format-version 9 game save.");
        var sections = save.Sections.Where(section => section.Name == "TRAN").ToArray();
        if (sections.Length != 1)
            throw new InvalidDataException("The game save must contain one TRAN section.");
        var section = sections[0];
        var reader = new Reader(bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.UInt32() != 1)
            throw new InvalidDataException("Unsupported TRAN section version.");
        reader.Skip(28);
        int count = reader.UInt16();
        if (count is < 1 or > MaxSlots || count > reader.Remaining / 9)
            throw new InvalidDataException("Invalid convoy capacity.");
        var slots = new InventorySlot[count];
        var starts = new int[count];
        var ends = new int[count];
        for (int slot = 0; slot < count; slot++)
        {
            starts[slot] = reader.Position;
            if (reader.UInt32() != 1 || reader.UInt32() != 5)
                throw new InvalidDataException("Unsupported convoy entry or UnitItem version.");
            byte present = reader.Byte();
            if (present > 1)
                throw new InvalidDataException("Invalid convoy item presence flag.");
            InventoryItem? item = null;
            if (present == 1)
            {
                uint hash = reader.Reference() ?? throw new InvalidDataException("An occupied item has no item reference.");
                int uses = reader.Byte();
                int refine = reader.Byte();
                uint flags = reader.UInt32();
                uint? engraving = reader.Reference();
                item = new InventoryItem(hash, uses, refine, flags, engraving);
            }
            slots[slot] = new InventorySlot(slot, item);
            ends[slot] = reader.Position;
        }
        if (reader.Remaining != 0)
            throw new InvalidDataException("The convoy contains unrecognized trailing data.");
        return new InventoryLayout { Section = section, Slots = Array.AsReadOnly(slots), Starts = starts, Ends = ends };
    }

    public byte[] Replace(byte[] bytes, int slot, InventoryItem? item)
    {
        if (slot < 0 || slot >= Slots.Count)
            throw new ArgumentOutOfRangeException(nameof(slot), "The convoy slot is outside the saved capacity.");
        if (item is not null)
            Validate(item);
        using var output = new MemoryStream();
        output.Write(bytes.AsSpan(Section.PayloadOffset, Starts[slot] - Section.PayloadOffset));
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1u);
            WriteItem(writer, item);
        }
        output.Write(bytes.AsSpan(Ends[slot], Section.PayloadOffset + Section.Length - Ends[slot]));
        return output.ToArray();
    }

    internal static void Validate(InventoryItem item)
    {
        var definition = ItemCatalog.Find(item.ItemHash)
            ?? throw new ArgumentException("This item is not in the verified convoy catalog.");
        if (definition.UnlimitedUses ? item.Uses != 255 : item.Uses < 1 || item.Uses > definition.MaxUses)
            throw new ArgumentOutOfRangeException(nameof(item.Uses), $"Invalid remaining uses for {definition.English}.");
        if (item.RefineLevel < 0 || item.RefineLevel > definition.MaxRefine)
            throw new ArgumentOutOfRangeException(nameof(item.RefineLevel), $"Refine level must be between 0 and {definition.MaxRefine}.");
        if (item.EngravingHash.HasValue && definition.MaxRefine == 0)
            throw new ArgumentException("An engraved weapon cannot be replaced with an item that cannot be forged.");
    }

    internal static void WriteItem(BinaryWriter writer, InventoryItem? item)
    {
        writer.Write(5u);
        writer.Write(item is not null);
        if (item is null)
            return;
        Reference(writer, item.ItemHash);
        writer.Write((byte)item.Uses);
        writer.Write((byte)item.RefineLevel);
        writer.Write(item.Flags);
        Reference(writer, item.EngravingHash);
    }

    private static void Reference(BinaryWriter writer, uint? hash)
    {
        writer.Write((ushort)(hash.HasValue ? 0xefcd : 0xccdb));
        if (hash.HasValue)
            writer.Write(hash.Value);
    }

    private sealed class Reader(byte[] bytes, int position, int end)
    {
        public int Position { get; private set; } = position;
        public int Remaining => end - Position;
        public void Skip(int length)
        {
            if (length < 0 || length > Remaining)
                throw new InvalidDataException("A convoy field extends beyond its section.");
            Position += length;
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
            _ => throw new InvalidDataException("Unsupported convoy data-reference encoding.")
        };
    }
}
