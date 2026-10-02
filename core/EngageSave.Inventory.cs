using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<InventorySlot> ReadInventory() => InventoryLayout.Read(this, _bytes).Slots;

    public EngageSave WithInventoryItem(int slot, string itemId, int uses, int refineLevel)
    {
        var layout = InventoryLayout.Read(this, _bytes);
        var old = GetSlot(layout, slot).Item;
        var item = new InventoryItem(ItemCatalog.Get(itemId).Hash, uses, refineLevel, old?.Flags ?? 0, old?.EngravingHash);
        byte[] payload = layout.Replace(_bytes, slot, item);
        if (old == item)
            return this;
        var edited = ReplaceSection(layout.Section, payload);
        if (edited.ReadInventory()[slot].Item != item)
            throw new InvalidDataException("The edited item did not survive serialization.");
        return edited;
    }

    public EngageSave AddInventoryItem(string itemId, int? uses = null, int refineLevel = 0)
    {
        var slots = ReadInventory();
        var empty = slots.FirstOrDefault(slot => slot.Item is null)
            ?? throw new ArgumentException("The convoy is full.");
        var definition = ItemCatalog.Get(itemId);
        return WithInventoryItem(empty.Slot, itemId, uses ?? definition.MaxUses, refineLevel);
    }

    public EngageSave DeleteInventoryItem(int slot)
    {
        var layout = InventoryLayout.Read(this, _bytes);
        if (GetSlot(layout, slot).Item is null)
            return this;
        var edited = ReplaceSection(layout.Section, layout.Replace(_bytes, slot, null));
        if (edited.ReadInventory()[slot].Item is not null)
            throw new InvalidDataException("The deleted item remained in the convoy.");
        return edited;
    }

    public EngageSave RestoreInventoryUses(int? slot = null)
    {
        var layout = InventoryLayout.Read(this, _bytes);
        if (slot.HasValue)
        {
            var selected = GetSlot(layout, slot.Value).Item;
            if (selected is null || ItemCatalog.Find(selected.ItemHash) is null)
                throw new ArgumentException("Select a known item to restore its uses.");
        }
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        bool changed = false;
        foreach (var entry in layout.Slots.Where(entry => !slot.HasValue || entry.Slot == slot.Value))
        {
            var item = entry.Item;
            var definition = item is null ? null : ItemCatalog.Find(item.ItemHash);
            if (definition is null || definition.UnlimitedUses || item!.Uses == definition.MaxUses)
                continue;
            // Two version words, presence byte, and a six-byte item reference precede remaining uses.
            int offset = layout.Starts[entry.Slot] + 15 - layout.Section.PayloadOffset;
            payload[offset] = (byte)definition.MaxUses;
            changed = true;
        }
        return changed ? ReplaceSection(layout.Section, payload) : this;
    }

    private static InventorySlot GetSlot(InventoryLayout layout, int slot) => slot >= 0 && slot < layout.Slots.Count
        ? layout.Slots[slot] : throw new ArgumentOutOfRangeException(nameof(slot));

    private EngageSave ReplaceSection(SaveSection section, byte[] payload)
    {
        int delta = payload.Length - section.Length;
        using var output = new MemoryStream();
        output.Write(_bytes.AsSpan(0, section.PayloadOffset));
        output.Write(payload);
        output.Write(_bytes.AsSpan(section.PayloadOffset + section.Length));
        byte[] edited = output.ToArray();
        Write32(edited, section.Offset + 4, (uint)(payload.Length + 4));
        int index = Kind == SaveKind.Game ? 132 : 4;
        for (int slot = 0; slot < 32; slot++)
        {
            int position = index + slot * 4;
            uint offset = ReadUInt32(edited, position);
            if (offset > section.Offset)
                Write32(edited, position, checked((uint)(offset + delta)));
        }
        Write32(edited, edited.Length - 4, Crc32.Compute(edited.AsSpan(0, edited.Length - 4)));
        return Parse(edited);
    }

    private static void Write32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
