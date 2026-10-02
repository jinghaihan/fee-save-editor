namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    private sealed record EngravingOwner(int? Character, int Slot, InventoryItem Item);

    public EngageSave WithInventoryEngraving(int slot, string? engravingId)
    {
        var old = GetSlot(InventoryLayout.Read(this, _bytes), slot).Item
            ?? throw new ArgumentException("Select an occupied convoy slot.");
        return AssignEngraving(new(null, slot, SelectEngraving(old, engravingId)));
    }

    public EngageSave WithRosterEngraving(int index, int slot, string? engravingId)
    {
        var entry = GetCharacter(RosterLayout.Read(this, _bytes), index);
        CheckEngravingCharacter(entry.Character);
        var old = CharacterItem(entry, slot) ?? throw new ArgumentException("Select an occupied character item slot.");
        CheckCharacterItemEditable(old);
        return AssignEngraving(new(index, slot, SelectEngraving(old, engravingId)));
    }

    public EngageSave WithInventoryItem(int slot, string itemId, int uses, int refineLevel, string? engravingId)
    {
        var old = GetSlot(InventoryLayout.Read(this, _bytes), slot).Item;
        var item = SelectEngraving(new(ItemCatalog.Get(itemId).Hash, uses, refineLevel, old?.Flags ?? 0, null), engravingId);
        InventoryLayout.Validate(item);
        return AssignEngraving(new(null, slot, item));
    }

    public EngageSave WithRosterItem(int index, int slot, string itemId, int uses, int refineLevel, string? engravingId)
    {
        var entry = GetCharacter(RosterLayout.Read(this, _bytes), index);
        CheckEngravingCharacter(entry.Character);
        var old = CharacterItem(entry, slot);
        CheckCharacterItemEditable(old);
        var item = SelectEngraving(new(ItemCatalog.Get(itemId).Hash, uses, refineLevel, old?.Flags ?? 0, null), engravingId);
        InventoryLayout.Validate(item);
        return AssignEngraving(new(index, slot, item));
    }

    private static InventoryItem SelectEngraving(InventoryItem item, string? engravingId)
    {
        if (engravingId is null) return item with { EngravingHash = null };
        var engraving = EngravingCatalog.Get(engravingId);
        if (!EngravingCatalog.CanEngrave(item.ItemHash))
            throw new ArgumentException("Only verified engravable weapons can receive an engraving.");
        return item with { EngravingHash = engraving.Hash };
    }

    private static void CheckEngravingCharacter(RosterCharacter character)
    {
        if (character.Force is UnitForce.Enemy or UnitForce.Temporary)
            throw new ArgumentException("Enemy and temporary units cannot receive player weapon engravings.");
    }

    private EngageSave AssignEngraving(EngravingOwner target)
    {
        var edited = this;
        if (target.Item.EngravingHash is uint hash)
        {
            // Read both owned inventories before editing, so an unsupported layout cannot leave a partial transfer.
            var inventory = ReadInventory();
            var roster = ReadRoster();
            var engraving = EngravingCatalog.Find(hash)!;
            var owners = inventory.Where(row => row.Item is not null).Select(row => new EngravingOwner(null, row.Slot, row.Item!))
                .Concat(roster.Where(row => row.Force is not UnitForce.Enemy and not UnitForce.Temporary)
                    .SelectMany(character => character.Items.Where(row => row.Item is not null)
                        .Select(row => new EngravingOwner(character.Index, row.Slot, row.Item!))));
            foreach (var owner in owners)
            {
                if (owner.Character == target.Character && owner.Slot == target.Slot
                    || owner.Item.EngravingHash is not uint previous || EngravingCatalog.Find(previous)?.Id != engraving.Id)
                    continue;
                if (owner.Character.HasValue) CheckCharacterItemEditable(owner.Item);
                edited = edited.ReplaceEngravingOwner(owner with { Item = owner.Item with { EngravingHash = null } });
            }
        }
        return edited.ReplaceEngravingOwner(target);
    }

    private EngageSave ReplaceEngravingOwner(EngravingOwner owner)
    {
        if (owner.Character is int index)
        {
            var layout = RosterLayout.Read(this, _bytes);
            var entry = GetCharacter(layout, index);
            return CharacterItem(entry, owner.Slot) == owner.Item ? this
                : ReplaceCharacterItem(layout, entry, owner.Slot, owner.Item);
        }
        var inventory = InventoryLayout.Read(this, _bytes);
        if (GetSlot(inventory, owner.Slot).Item == owner.Item) return this;
        var edited = ReplaceSection(inventory.Section, inventory.Replace(_bytes, owner.Slot, owner.Item, validate: false));
        if (edited.ReadInventory()[owner.Slot].Item != owner.Item)
            throw new InvalidDataException("The edited engraving did not survive serialization.");
        return edited;
    }
}
