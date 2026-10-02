using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public IReadOnlyList<RosterCharacter> ReadRoster() =>
        RosterLayout.Read(this, _bytes).Characters.Select(layout => layout.Character).ToArray();

    public EngageSave WithRosterValues(int index, RosterValue values)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var old = entry.Character.Values;
        if (values == old)
            return this;
        if (values.Level != old.Level || values.Experience != old.Experience)
        {
            var job = RosterCatalog.Class(entry.Character.ClassHash)
                ?? throw new ArgumentException("This class has no verified level limit.");
            if (values.Level < 1 || values.Level > job.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(values.Level), $"Level must be between 1 and {job.MaxLevel}.");
            if (values.Experience is < 0 or > 99 || values.Level == job.MaxLevel && values.Experience != 0)
                throw new ArgumentOutOfRangeException(nameof(values.Experience), "Experience must be 0–99, or 0 at maximum level.");
        }
        if (values.SkillPoints != old.SkillPoints && values.SkillPoints is < 0 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(values.SkillPoints), "SP must be between 0 and 9999.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int levelOffset = entry.LevelOffset - layout.Section.PayloadOffset;
        payload[levelOffset] = (byte)values.Level;
        payload[levelOffset + 1] = (byte)values.Experience;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(entry.End - 8 - layout.Section.PayloadOffset, 2), (short)values.SkillPoints);
        return ReplaceSection(layout.Section, payload);
    }

    public EngageSave WithRosterStat(int index, RosterStat stat, int value)
    {
        if (!Enum.IsDefined(stat) || stat == RosterStat.Sight)
            throw new ArgumentException("Select a displayed character stat.", nameof(stat));
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var current = entry.Character.Stats[(int)stat];
        if (current.Value == value)
            return this;
        var job = RosterCatalog.Class(entry.Character.ClassHash);
        if (job is null || !current.Maximum.HasValue)
            throw new ArgumentException("This character or class has no verified stat limits.");
        int minimum = stat == RosterStat.HP ? 1 : 0;
        int stored = value - job.BaseStats[(int)stat];
        if (value < minimum || value > current.Maximum || stored is < -120 or > 120)
            throw new ArgumentOutOfRangeException(nameof(value), $"{stat} must be between {minimum} and {current.Maximum}.");
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        payload[entry.BaseStatsOffset + (int)stat - layout.Section.PayloadOffset] = unchecked((byte)(sbyte)stored);
        if (stat == RosterStat.HP)
        {
            int hp = entry.LevelOffset + 2 - layout.Section.PayloadOffset;
            payload[hp] = (byte)Math.Min(payload[hp], value);
        }
        return ReplaceSection(layout.Section, payload);
    }

    public EngageSave WithRosterItem(int index, int slot, string itemId, int uses, int refineLevel)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var old = CharacterItem(entry, slot);
        CheckCharacterItemEditable(old);
        var item = new InventoryItem(ItemCatalog.Get(itemId).Hash, uses, refineLevel, old?.Flags ?? 0, old?.EngravingHash);
        InventoryLayout.Validate(item);
        return old == item ? this : ReplaceCharacterItem(layout, entry, slot, item);
    }

    public EngageSave DeleteRosterItem(int index, int slot)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        var old = CharacterItem(entry, slot);
        CheckCharacterItemEditable(old);
        return old is null ? this : ReplaceCharacterItem(layout, entry, slot, null);
    }

    public EngageSave RestoreRosterUses(int index)
    {
        var layout = RosterLayout.Read(this, _bytes);
        var entry = GetCharacter(layout, index);
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        bool changed = false;
        foreach (var slot in entry.Character.Items)
        {
            var item = slot.Item;
            var definition = item is null ? null : ItemCatalog.Find(item.ItemHash);
            if (item is null || definition is null || definition.UnlimitedUses || item.Uses == definition.MaxUses)
                continue;
            payload[entry.ItemStarts[slot.Slot] + 11 - layout.Section.PayloadOffset] = (byte)definition.MaxUses;
            changed = true;
        }
        return changed ? ReplaceSection(layout.Section, payload) : this;
    }

    private EngageSave ReplaceCharacterItem(RosterLayout layout, CharacterLayout entry, int slot, InventoryItem? item)
    {
        using var record = new MemoryStream();
        using (var writer = new BinaryWriter(record, System.Text.Encoding.UTF8, leaveOpen: true))
            InventoryLayout.WriteItem(writer, item);
        byte[] replacement = record.ToArray();
        int delta = replacement.Length - (entry.ItemEnds[slot] - entry.ItemStarts[slot]);
        using var output = new MemoryStream();
        output.Write(_bytes.AsSpan(layout.Section.PayloadOffset, entry.ItemStarts[slot] - layout.Section.PayloadOffset));
        output.Write(replacement);
        output.Write(_bytes.AsSpan(entry.ItemEnds[slot], layout.Section.PayloadOffset + layout.Section.Length - entry.ItemEnds[slot]));
        byte[] payload = output.ToArray();
        Write32(payload, entry.Start - layout.Section.PayloadOffset, checked((uint)(entry.End - entry.Start + delta)));
        var edited = ReplaceSection(layout.Section, payload);
        if (edited.ReadRoster()[entry.Character.Index].Items[slot].Item != item)
            throw new InvalidDataException("The edited character item did not survive serialization.");
        return edited;
    }

    private static CharacterLayout GetCharacter(RosterLayout layout, int index) => index >= 0 && index < layout.Characters.Count
        ? layout.Characters[index] : throw new ArgumentOutOfRangeException(nameof(index));

    private static InventoryItem? CharacterItem(CharacterLayout entry, int slot) => slot >= 0 && slot < entry.Character.Items.Count
        ? entry.Character.Items[slot].Item : throw new ArgumentOutOfRangeException(nameof(slot));

    private static void CheckCharacterItemEditable(InventoryItem? item)
    {
        if (item is not null && RosterCatalog.Item(item.ItemHash) is { EngageOnly: true })
            throw new ArgumentException("Engage weapons and their reserved slots are managed by the game.");
    }
}
