using System.Buffers.Binary;
using FeeEditor.Core;

internal static class EngravingTests
{
    public const string Marth = "GID_マルス";
    public const string Sigurd = "GID_シグルド";
    public const string WeaponId = "IID_フェンサリル";
    private static readonly InventoryItem Weapon = new(ItemCatalog.Hash(WeaponId), 255, 3, 0x55, 0x123abc);

    public static void Run()
    {
        Check(EngravingCatalog.Engravings.Count == 20 && EngravingCatalog.Get(Marth) is
            { Power: 1, Weight: 0, Hit: 10, Critical: 10, Avoid: 5, Secure: 5 }, "The engraving catalog has incorrect effects.");
        Check(EngravingCatalog.Get("GID_ディミトリ").Id == "GID_エーデルガルト"
            && EngravingCatalog.Get("GID_クロード").Id == "GID_エーデルガルト", "The three-house engraving aliases are separate choices.");
        foreach (var engraving in EngravingCatalog.Engravings)
            Check(engraving.Name("en") != engraving.Name("zh-Hans"), "An engraving lacks translated names.");
        Check(EngravingCatalog.CanEngrave(Weapon.ItemHash) && !EngravingCatalog.CanEngrave(ItemCatalog.Hash("IID_リカバー"))
            && !EngravingCatalog.CanEngrave(ItemCatalog.Hash("IID_エンゲージ枠")), "Weapon eligibility was guessed from refinement.");
        var save = EngageSave.Parse(Fixture());
        byte[] original = save.Serialize();
        foreach (var engraving in EngravingCatalog.Engravings)
        {
            var changed = save.WithInventoryEngraving(1, engraving.Id);
            Check(changed.ReadInventory()[1].Item == Weapon with { EngravingHash = engraving.Hash }, "An engraving changed other item fields.");
            Check(Owners(changed, engraving.Id) == 1, "Assigning an engraving left multiple owners.");
            Unrelated(save, changed);
        }
        var cleared = save.WithInventoryEngraving(1, null);
        Check(cleared.Length == save.Length - 4 && cleared.ReadInventory()[1].Item == Weapon with { EngravingHash = null },
            "Clearing an unknown engraving changed other fields or reference length.");
        var transferred = save.WithRosterEngraving(0, 1, Marth);
        Check(transferred.ReadInventory()[2].Item!.EngravingHash is null && Owners(transferred, Marth) == 1
            && transferred.ReadRoster()[0].Items[1].Item == Weapon with { EngravingHash = EngravingCatalog.Get(Marth).Hash },
            "Convoy-to-character transfer did not clear the previous weapon.");
        transferred = transferred.WithRosterEngraving(1, 1, Marth);
        Check(transferred.ReadRoster()[0].Items[1].Item!.EngravingHash is null && Owners(transferred, Marth) == 1,
            "Character-to-character transfer failed.");
        transferred = transferred.WithInventoryEngraving(4, Marth);
        Check(transferred.ReadRoster()[1].Items[1].Item!.EngravingHash is null && Owners(transferred, Marth) == 1,
            "Character-to-convoy transfer failed.");
        Check(transferred.ReadInventory()[3].Item!.EngravingHash == EngravingCatalog.Get(Sigurd).Hash,
            "Transferring one engraving cleared an unrelated engraving.");
        var repaired = save.WithInventoryItem(1, "IID_リカバー", 10, 0, null);
        Check(repaired.ReadInventory()[1].Item is { EngravingHash: null, Uses: 10, Flags: 0x55 },
            "Clearing an engraving while changing to a staff failed.");
        var added = save.WithRosterItem(0, 3, WeaponId, 255, 0, Marth);
        Check(added.ReadRoster()[0].Items[3].Item is { Flags: 0, RefineLevel: 0 } && Owners(added, Marth) == 1,
            "Adding a carried weapon bypassed engraving uniqueness.");
        var aliases = EngageSave.Parse(Fixture(items: new InventoryItem?[]
        {
            Weapon with { EngravingHash = ItemCatalog.Hash("GID_ディミトリ") },
            Weapon with { EngravingHash = ItemCatalog.Hash("GID_クロード") },
            Weapon with { EngravingHash = null }
        }));
        var united = aliases.WithInventoryEngraving(2, "GID_エーデルガルト");
        Check(united.ReadInventory().Take(2).All(row => row.Item!.EngravingHash is null)
            && Owners(united, "GID_エーデルガルト") == 1, "Shared aliases retained duplicate engraving owners.");
        var raw = EngageSave.Parse(Fixture(items: new InventoryItem?[] { Weapon with { Uses = 7, RefineLevel = 200 } }));
        Check(raw.WithInventoryEngraving(0, Marth).ReadInventory()[0].Item is { Uses: 7, RefineLevel: 200, Flags: 0x55 },
            "An engraving-only edit normalized unrelated raw item fields.");
        var reserved = EngageSave.Parse(Fixture(RosterTests.Fixture(mutate: bytes =>
            Write32(bytes, 34 + 137, ItemCatalog.Hash("IID_エンゲージ枠")))));
        Reject(() => reserved.WithRosterEngraving(0, 0, null));
        Reject(() => reserved.WithRosterItem(0, 0, WeaponId, 255, 0, Marth));
        foreach (Action action in new Action[]
        {
            () => save.WithInventoryEngraving(0, Marth), () => save.WithInventoryEngraving(5, Marth),
            () => save.WithInventoryEngraving(-1, Marth), () => save.WithInventoryEngraving(1, "GID_missing"),
            () => save.WithRosterEngraving(0, 0, Marth), () => save.WithRosterEngraving(0, 3, Marth),
            () => save.WithRosterEngraving(0, 2, Marth), () => save.WithRosterEngraving(2, 1, Marth),
            () => save.WithInventoryItem(1, WeaponId, 254, 3, Marth),
            () => save.WithRosterItem(0, 1, WeaponId, 255, 6, Marth),
            () => EngageSave.Parse(InventoryTests.Fixture()).WithInventoryEngraving(1, Marth),
            () => EngageSave.Parse(RosterTests.Fixture()).WithRosterEngraving(0, 1, Marth)
        }) Reject(action);
        var enemies = EngageSave.Parse(Fixture(RosterTests.Fixture(force: UnitForce.Enemy)));
        Reject(() => enemies.WithRosterEngraving(0, 1, Marth));
        var corrupt = original.ToArray();
        var unit = save.Sections.Single(row => row.Name == "UNIT");
        corrupt[unit.PayloadOffset] = 99;
        Write32(corrupt, corrupt.Length - 4, Crc(corrupt.AsSpan(0, corrupt.Length - 4)));
        var unsupported = EngageSave.Parse(corrupt);
        Reject(() => unsupported.WithInventoryEngraving(1, Marth));
        Check(unsupported.WithInventoryEngraving(1, null).ReadInventory()[1].Item!.EngravingHash is null,
            "Clearing a selected engraving incorrectly requires unrelated ownership parsing.");
        Check(save.Serialize().AsSpan().SequenceEqual(original), "Engraving operations mutated the original save.");
        Console.WriteLine("Engravings: 20 base/DLC choices, transfers, uniqueness, clearing, bounds and preservation passed.");
    }

    public static byte[] Fixture(byte[]? rosterSource = null, InventoryItem?[]? items = null)
    {
        var roster = EngageSave.Parse(rosterSource ?? RosterTests.Fixture());
        byte[] rosterBytes = roster.Serialize();
        var inventory = EngageSave.Parse(InventoryTests.Fixture(items ?? new InventoryItem?[]
        {
            new(ItemCatalog.Hash("IID_リカバー"), 4, 0, 0xdeadbeef, null), Weapon,
            Weapon with { EngravingHash = EngravingCatalog.Get(Marth).Hash },
            Weapon with { EngravingHash = EngravingCatalog.Get(Sigurd).Hash },
            Weapon with { EngravingHash = null }, null
        }));
        byte[] inventoryBytes = inventory.Serialize();
        var sections = roster.Sections.Select(row => rosterBytes.AsSpan(row.Offset, row.Length + 8).ToArray()).ToList();
        var convoy = inventory.Sections.Single(row => row.Name == "TRAN");
        sections.Add(inventoryBytes.AsSpan(convoy.Offset, convoy.Length + 8).ToArray());
        byte[] result = new byte[260 + sections.Sum(row => row.Length) + 8];
        rosterBytes.AsSpan(0, 132).CopyTo(result);
        int offset = 260;
        for (int index = 0; index < sections.Count; index++)
        {
            Write32(result, 132 + index * 4, (uint)offset);
            sections[index].CopyTo(result, offset);
            offset += sections[index].Length;
        }
        Write32(result, 132 + sections.Count * 4, (uint)offset);
        "LVRC"u8.CopyTo(result.AsSpan(offset));
        Write32(result, offset + 4, Crc(result.AsSpan(0, offset + 4)));
        return result;
    }

    public static void CheckReal(EngageSave save)
    {
        int checkedCount = 0;
        foreach (var character in save.ReadRoster())
            foreach (var slot in character.Items)
                if (slot.Item?.EngravingHash is uint hash && EngravingCatalog.Find(hash) is { } engraving
                    && EngravingCatalog.CanEngrave(slot.Item.ItemHash))
                {
                    Check(Owners(save, engraving.Id) == 1, "A real engraving has duplicate player owners.");
                    var cleared = save.WithRosterEngraving(character.Index, slot.Slot, null);
                    Check(cleared.WithRosterEngraving(character.Index, slot.Slot, engraving.Id).Serialize().AsSpan().SequenceEqual(save.Serialize()),
                        "Clearing/restoring a real engraving changed unrelated bytes.");
                    checkedCount++;
                }
        Check(checkedCount > 0, "No real engravings were checked.");
        Console.WriteLine($"Real engravings: {checkedCount} equipped weapons resolved and restored byte-for-byte.");
    }

    private static int Owners(EngageSave save, string id) => save.ReadInventory().Select(row => row.Item)
        .Concat(save.ReadRoster().Where(row => row.Force is not UnitForce.Enemy and not UnitForce.Temporary)
            .SelectMany(row => row.Items.Select(slot => slot.Item)))
        .Count(item => item?.EngravingHash is uint hash && EngravingCatalog.Find(hash)?.Id == id);
    private static void Unrelated(EngageSave before, EngageSave after)
    {
        byte[] first = before.Serialize(), second = after.Serialize();
        foreach (var section in before.Sections.Where(row => row.Name is not "TRAN" and not "UNIT"))
        {
            var other = after.Sections.Single(row => row.Name == section.Name);
            Check(first.AsSpan(section.PayloadOffset, section.Length).SequenceEqual(second.AsSpan(other.PayloadOffset, other.Length)),
                "An engraving modified an unrelated section.");
        }
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new Exception("An invalid or unverifiable engraving edit was accepted.");
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
