using System.Buffers.Binary;
using System.Text;
using FeeEditor.Core;

internal static class EmblemTests
{
    public const string Alear = "PID_リュール";
    public const string Marth = "GID_マルス";
    public const string Tiki = "GID_チキ";
    public const string Caeda = "RNID_紋章_シーダ_C";

    public static void Run()
    {
        Check(EmblemCatalog.Emblems.Count == 20 && EmblemCatalog.Emblems.Count(row => row.Dlc) == 7,
            "The Emblem catalog must include all DLC bracelets.");
        Check(EmblemCatalog.Rings.Count == 483 && EmblemCatalog.Emblem(Marth)!.Name("zh-Hans") == "马尔斯",
            "Localized Emblem/ring names are incomplete.");
        int[] thresholds = [0, 11, 22, 33, 44, 55, 66, 77, 88, 99, 109, 120, 131, 142, 153, 164, 175, 186, 197, 208];
        for (int level = 1; level <= 20; level++)
        {
            Check(EmblemCatalog.ExperienceForLevel(level) == thresholds[level - 1], "Incorrect bond EXP threshold.");
            int end = level == 20 ? 208 : thresholds[level] - 1;
            for (int exp = thresholds[level - 1]; exp <= end; exp++)
                Check(EmblemCatalog.LevelForExperience(exp) == level, "Incorrect bond EXP interval.");
        }
        var save = EngageSave.Parse(Fixture());
        var emblems = save.ReadEmblems();
        Check(emblems.Count == 3 && emblems[0].Bonds.Count == 2 && emblems[2].PactPartner == "PID_ユナカ",
            "Emblem bonds or the Pact Ring partner were misread.");
        Check(save.ReadBondRings().Count == 2, "Ring stock was misread.");
        Check(ReferenceEquals(save, save.WithEmblemBond(1, Alear, 1)), "A bond no-op changed the save.");
        for (int level = 1; level <= 20; level++)
        {
            var edited = save.WithEmblemBond(1, Alear, level);
            var bond = edited.ReadEmblems()[0].Bonds[0];
            Check(bond.Level == level && bond.Experience == thresholds[level - 1], "Bond edit failed.");
            Check(bond.InheritedSkills.SequenceEqual(new uint[] { 0x12345678, 0x87654321 }) && (bond.TalkFlags & 32) != 0,
                "Bond editing erased purchased skills or unrelated flags.");
            Unrelated(save, edited, "GDBD", "USER");
            if (level <= 10)
                Exact(save, edited.WithEmblemBond(1, Alear, 1), "Reversing a bond edit changed unknown bytes.");
            else
            {
                Check(edited.ReadMainValues() == save.ReadMainValues(), "Adding a level-cap variable changed Main values.");
                int length = edited.Length;
                Check(edited.WithEmblemBond(1, Alear, 19).Length == length, "A level-cap variable was duplicated.");
            }
        }
        var rawExp = save.WithEmblemBond(2, Alear, 10, 108);
        Check(rawExp.ReadEmblems()[1].Bonds[0].Experience == 108, "Partial bond EXP was discarded.");
        Unrelated(save, rawExp, "GDBD");
        Reject(() => save.WithEmblemBond(1, Alear, 10, 109));
        Reject(() => save.WithEmblemBond(1, Alear, 20, 209));
        Reject(() => save.WithEmblemBond(1, Alear, 0));
        Reject(() => save.WithEmblemBond(1, Alear, 21));
        Reject(() => save.WithEmblemBond(99, Alear, 5));
        Reject(() => save.WithEmblemBond(1, "PID_missing", 5));
        Reject(() => save.WithEmblemBond(3, "PID_ユナカ", 20));
        var maximum = save.WithMaximumEmblemBonds(1);
        Check(maximum.ReadEmblems()[0].Bonds.All(bond => bond.Level == 20 && bond.Experience == 208), "Batch bonds did not reach maximum.");
        Unrelated(save, maximum, "GDBD", "USER");
        Check(ReferenceEquals(maximum, maximum.WithMaximumEmblemBonds(1)), "Repeating maximum bonds was not a no-op.");
        var one = save.WithMaximumEmblemBonds(1, Alear);
        Check(one.ReadEmblems()[0].Bonds[0].Level == 20 && one.ReadEmblems()[0].Bonds[1].Level == 1, "Single maximum edited other characters.");
        Check(save.WithMaximumEmblemBonds(2).ReadEmblems()[1].Bonds.All(bond => bond.Level == 20), "DLC batch maximum failed.");
        var pactMaximum = save.WithMaximumEmblemBonds(3);
        Check(pactMaximum.ReadEmblems()[2].Bonds[1].Level == 21 && pactMaximum.ReadEmblems()[2].Bonds[1].Experience == 209,
            "Batch maximum downgraded the Pact partner.");
        Check(EmblemCatalog.MaximumLevel(emblems[2], Alear) == 20 && EmblemCatalog.MaximumLevel(emblems[2], "PID_ユナカ") == 21,
            "Pact maximum was applied to other characters.");
        var noPact = EngageSave.Parse(Container(Bonds(pactPartner: false), Rings()));
        var noPactMaximum = noPact.WithMaximumEmblemBonds(3);
        Check(noPactMaximum.ReadEmblems()[2].Bonds.All(bond => bond.Level == 20 && bond.Experience == 208), "Alear normal maximum failed.");
        Check(SupportRank(noPactMaximum) == 3 && SupportRank(noPact) == 1, "Alear support was not synchronized or the source mutated.");
        Unrelated(noPact, noPactMaximum, "GDBD", "UREL");
        Check(ReferenceEquals(noPactMaximum, noPactMaximum.WithMaximumEmblemBonds(3)), "Alear maximum was not idempotent.");
        Reject(() => noPact.WithEmblemBond(3, "PID_ユナカ", 21, 209));
        Reject(() => noPact.WithEmblemBond(3, "PID_ユナカ", 19));
        Reject(() => noPact.WithMaximumEmblemBonds(99));
        var missingSupport = EngageSave.Parse(Container(Bonds(pactPartner: false), Rings(), supportKey: "PID_unknownPID_unknown"));
        byte[] missingOriginal = missingSupport.Serialize();
        Reject(() => missingSupport.WithMaximumEmblemBonds(3));
        Check(missingSupport.Serialize().AsSpan().SequenceEqual(missingOriginal), "A failed batch partially modified the save.");
        var unsupportedSupport = EngageSave.Parse(Container(Bonds(pactPartner: false), Rings(), supportVersion: 2));
        Reject(() => unsupportedSupport.WithMaximumEmblemBonds(3));
        var oldPactSupport = EngageSave.Parse(Container(Bonds(pactPartner: false), Rings(), supportRank: 4));
        Reject(() => oldPactSupport.WithMaximumEmblemBonds(3));
        for (int stock = 0; stock <= 99; stock++)
        {
            var edited = save.WithBondRingStock(10, stock);
            Check(edited.ReadBondRings()[0].StockCount == stock, "Ring stock did not round-trip.");
            Unrelated(save, edited, "RING");
            Exact(save, edited.WithBondRingStock(10, 7), "Ring stock reversal was not exact.");
        }
        Reject(() => save.WithBondRingStock(10, -1));
        Reject(() => save.WithBondRingStock(10, 100));
        Reject(() => save.WithBondRingStock(99, 1));
        uint caedaHash = ItemCatalog.Hash(Caeda);
        var stacked = BondRingTests.Fixture([new(10, caedaHash, 40, null), new(11, caedaHash, 50, null)]);
        Check(stacked.MaximumBondRingStock(10) == 49 && stacked.WithBondRingStock(10, 49).ReadBondRings()[1].StockCount == 50,
            "Identical ring stacks must share the 99-copy limit without changing other stacks.");
        Reject(() => stacked.WithBondRingStock(10, 50));
        var wornStack = BondRingTests.Fixture([new(10, caedaHash, 1, null), new(11, caedaHash, 20, null)],
            RosterTests.Fixture(validEquipment: true));
        Check(wornStack.MaximumBondRingStock(11) == 98 && wornStack.WithBondRingStock(11, 98).ReadBondRings()[0].OwnerIndex == 0,
            "Equipped copies were not counted or their ownership changed.");
        Reject(() => wornStack.WithBondRingStock(11, 99));
        var overstocked = BondRingTests.Fixture([new(10, caedaHash, 90, null), new(11, caedaHash, 20, null)]);
        Check(ReferenceEquals(overstocked, overstocked.WithBondRingStock(10, 90)), "Preserving pre-existing excess stock rewrote the save.");
        Reject(() => overstocked.WithBondRingStock(10, 80));
        Check(overstocked.WithBondRingStock(10, 79).ReadBondRings()[0].StockCount == 79, "Excess stock cannot be corrected.");
        var equippedSave = EngageSave.Parse(Container(Bonds(), Rings(1), RosterTests.Fixture(validEquipment: true)));
        Check(equippedSave.ReadBondRings()[0].OwnerIndex == 0 && equippedSave.ReadCharacterRingLinks()[0].EmblemInstance == 1,
            "Variable-length unit battle data did not resolve ring ownership.");
        Reject(() => equippedSave.WithBondRingStock(10, 0));
        Reject(() => equippedSave.WithBondRingStock(10, 2));
        Check(ReferenceEquals(equippedSave, equippedSave.WithBondRingStock(10, 1)), "An equipped ring no-op changed the save.");
        foreach (byte[] bad in new[] { Bonds()[..^1], [.. Bonds(), (byte)0] })
            Reject(() => EngageSave.Parse(Container(bad, Rings())).ReadEmblems());
        foreach (byte[] bad in new[] { Rings()[..^1], [.. Rings(), (byte)0] })
            Reject(() => EngageSave.Parse(Container(Bonds(), bad)).ReadBondRings());
        var badVersion = Bonds(); badVersion[0] = 1;
        Reject(() => EngageSave.Parse(Container(badVersion, Rings())).ReadEmblems());
        var badCount = Bonds(); Write32(badCount, 32, uint.MaxValue);
        Reject(() => EngageSave.Parse(Container(badCount, Rings())).ReadEmblems());
        var badRing = Rings(); Write32(badRing, 47, 10);
        Reject(() => EngageSave.Parse(Container(Bonds(), badRing)).ReadBondRings());
        Console.WriteLine("Emblem core: bond thresholds, DLC, Pact preservation, ring stock and malformed records passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        var emblems = save.ReadEmblems();
        Check(emblems.Count == 20 && emblems.Sum(row => row.Bonds.Count) == 820,
            "The real save's 20 Emblems / 820 bonds were not fully parsed.");
        Check(emblems.All(row => EmblemCatalog.Emblem(row.EmblemId) is not null), "A real Emblem lacks a catalog name.");
        var links = save.ReadCharacterRingLinks();
        var rings = save.ReadBondRings();
        Check(rings.Count == 374 && rings.All(row => EmblemCatalog.Ring(row.RingHash) is not null),
            "The real save's ring stock was not fully recognized.");
        foreach (var emblem in emblems)
        {
            if (emblem.EmblemId == EmblemCatalog.AlearEmblemId)
            {
                var maximumAlear = save.WithMaximumEmblemBonds(emblem.InstanceId);
                Check(maximumAlear.ReadEmblems().First(row => row.InstanceId == emblem.InstanceId).Bonds.All(bond => bond.Level == 20),
                    "Real Alear maximum failed.");
                Unrelated(save, maximumAlear, "GDBD", "UREL");
                continue;
            }
            var bond = emblem.Bonds.FirstOrDefault(row => row.TalkFlags == 14) ?? emblem.Bonds[0];
            var edited = save.WithEmblemBond(emblem.InstanceId, bond.PersonId, 19);
            Check(edited.ReadEmblems().First(row => row.InstanceId == emblem.InstanceId).Bonds
                .First(row => row.PersonId == bond.PersonId).Level == 19, "A real/DLC bond edit failed.");
            var restored = edited.WithEmblemBond(emblem.InstanceId, bond.PersonId, bond.Level, bond.Experience);
            Unrelated(save, restored, "GDBD");
            var result = restored.ReadEmblems().First(row => row.InstanceId == emblem.InstanceId).Bonds
                .First(row => row.PersonId == bond.PersonId);
            Check(result.Level == bond.Level && result.Experience == bond.Experience
                && result.InheritedSkills.SequenceEqual(bond.InheritedSkills), "Restoring a bond lost its saved skills.");
            if (bond.TalkFlags == 14)
                Exact(save, restored, "Restoring a real bond changed another field.");
        }
        var ring = rings.First(row => !row.OwnerIndex.HasValue && row.StockCount > 0);
        Exact(save, save.WithBondRingStock(ring.InstanceId, save.MaximumBondRingStock(ring.InstanceId)).WithBondRingStock(ring.InstanceId, ring.StockCount),
            "Real ring stock did not restore exactly.");
        foreach (var equipped in rings.Where(row => row.OwnerIndex.HasValue))
        {
            Reject(() => save.WithBondRingStock(equipped.InstanceId, 0));
            Reject(() => save.WithBondRingStock(equipped.InstanceId, 2));
        }
        Check(links.Count == save.ReadRoster().Count, "Some character equipment links were skipped.");
        Console.WriteLine($"Real Emblems: 820 bonds, {rings.Count} rings and {links.Count} character links verified.");
    }

    public static byte[] Fixture() => Container(Bonds(), Rings());

    private static byte[] Bonds(bool pactPartner = true)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        Header(writer, 2); writer.Write(3u);
        foreach ((uint id, string gid) in new[] { (1u, Marth), (2u, Tiki), (3u, "GID_リュール") })
        {
            writer.Write(id); Wide(writer, gid); writer.Write(id == 3);
            if (id == 3) { writer.Write(0u); Wide(writer, pactPartner ? "PID_ユナカ" : ""); }
            writer.Write((ushort)2);
            foreach (string pid in new[] { Alear, "PID_ユナカ" })
            {
                bool pact = pactPartner && id == 3 && pid == "PID_ユナカ";
                Wide(writer, pid); writer.Write(3u); writer.Write((byte)(pact ? 21 : 1)); writer.Write((ushort)(pact ? 209 : 0));
                writer.Write(2u); writer.Write(2u); writer.Write(0x12345678u); writer.Write(0x87654321u);
                writer.Write((byte)(pact ? 46 : 32));
            }
        }
        return output.ToArray();
    }

    private static byte[] Rings(int stock = 7)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        Header(writer, 3); writer.Write(2u);
        writer.Write(10u); writer.Write((ushort)0xefcd); writer.Write(ItemCatalog.Hash(Caeda)); writer.Write((byte)stock);
        writer.Write(11u); writer.Write((ushort)0xefcd); writer.Write(ItemCatalog.Hash("RNID_紋章_シーダ_S")); writer.Write((byte)1);
        return output.ToArray();
    }

    private static void Header(BinaryWriter writer, uint version)
    { writer.Write(version); writer.Write(0xcdcdcdcdu); writer.Write(new byte[24]); }
    private static void Wide(BinaryWriter writer, string value)
    { byte[] bytes = Encoding.Unicode.GetBytes(value); writer.Write((uint)bytes.Length); writer.Write(bytes); }
    internal static byte[] Container(byte[] bonds, byte[] rings, byte[]? original = null,
        string? supportKey = null, uint supportVersion = 1, byte supportRank = 1)
    {
        original ??= MainTests.Fixture();
        var save = EngageSave.Parse(original);
        var sections = save.Sections.Select(row => original.AsSpan(row.Offset, row.Length + 8).ToArray()).ToList();
        using var support = new MemoryStream();
        using (var writer = new BinaryWriter(support, Encoding.UTF8, leaveOpen: true))
        {
            Header(writer, 1); writer.Write(1u); Wide(writer, supportKey ?? Alear + "PID_ユナカ");
            writer.Write(supportVersion); writer.Write(supportRank); writer.Write((byte)7); writer.Write((byte)2);
        }
        foreach ((string tag, byte[] payload) in new[] { ("DBDG", bonds), ("GNIR", rings), ("LERU", support.ToArray()) })
        {
            byte[] section = new byte[payload.Length + 8];
            Encoding.ASCII.GetBytes(tag).CopyTo(section, 0);
            Write32(section, 4, (uint)payload.Length + 4); payload.CopyTo(section, 8); sections.Add(section);
        }
        byte[] result = new byte[260 + sections.Sum(section => section.Length) + 8];
        original.AsSpan(0, 132).CopyTo(result); "EDNI"u8.CopyTo(result.AsSpan(128));
        int offset = 260;
        for (int index = 0; index < sections.Count; index++)
        {
            Write32(result, 132 + index * 4, (uint)offset); sections[index].CopyTo(result, offset); offset += sections[index].Length;
        }
        Write32(result, 132 + sections.Count * 4, (uint)offset); "LVRC"u8.CopyTo(result.AsSpan(offset));
        Write32(result, offset + 4, Checksum(result.AsSpan(0, offset + 4)));
        return result;
    }

    private static int SupportRank(EngageSave save)
    {
        byte[] bytes = save.Serialize();
        var section = save.Sections.Single(row => row.Name == "UREL");
        int start = section.PayloadOffset + 36;
        return bytes[start + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(start)) + 4];
    }

    private static void Unrelated(EngageSave before, EngageSave after, params string[] changed)
    {
        byte[] source = before.Serialize(), target = after.Serialize();
        Check(source.AsSpan(0, 132).SequenceEqual(target.AsSpan(0, 132)), "An Emblem edit changed the summary.");
        foreach (var section in before.Sections.Where(row => !changed.Contains(row.Name)))
        {
            var edited = after.Sections.Single(row => row.Name == section.Name);
            Check(source.AsSpan(section.Offset, section.Length + 8).SequenceEqual(target.AsSpan(edited.Offset, edited.Length + 8)),
                "An Emblem edit changed an unrelated section.");
        }
    }
    private static void Exact(EngageSave before, EngageSave after, string message) =>
        Check(before.Serialize().AsSpan().SequenceEqual(after.Serialize()), message);
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        return ~crc;
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException("An invalid Emblem/ring edit was accepted.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
