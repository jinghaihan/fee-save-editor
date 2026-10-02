using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class MainTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = Fixture();
        var save = EngageSave.Parse(original);
        var values = save.ReadMainValues();
        Check(values == new MainValues(5000, 1200, 12, 8, 3, Difficulty.Hard, GameMode.Classic, "Sommie"),
            "Main fields do not match the independent fixture.");
        Check(save.WithMainValues(values).Serialize().AsSpan().SequenceEqual(original), "No-op editing changed bytes.");
        foreach (string name in new[] { "S", "索拉 🐾", "A longer Sommie name" })
        {
            var target = values with { Money = 123, BondFragments = 456, IronIngots = 7, SteelIngots = 9,
                SilverIngots = 11, Difficulty = Difficulty.Maddening, GameMode = GameMode.Casual, SommieName = name };
            var edited = save.WithMainValues(target);
            Check(edited.ReadMainValues() == target, "Edited values did not round-trip.");
            byte[] result = edited.Serialize();
            int delta = Encoding.Unicode.GetByteCount(name) - Encoding.Unicode.GetByteCount(values.SommieName);
            Check(result.Length == original.Length + delta, "Name resizing changed the wrong number of bytes.");
            Check(result[23] == 2 && result[25] == 0 && result[305] == 0 && result[306] == 2,
                "The summary and gameplay settings were not both updated.");
            Check(result[307] == original[307], "The original difficulty flag changed.");
            Check(Read32(result, 60) == Checksum(result.AsSpan(0, 60)), "The summary checksum is invalid.");
            var opaqueBefore = save.Sections[1];
            var opaqueAfter = edited.Sections[1];
            Check(opaqueAfter.Offset == opaqueBefore.Offset + delta, "Later sections were not relocated.");
            Check(original.AsSpan(opaqueBefore.Offset, opaqueBefore.Length + 8)
                .SequenceEqual(result.AsSpan(opaqueAfter.Offset, opaqueAfter.Length + 8)), "An unrelated section changed.");
            // Preserve lifetime fragments and all unmodeled trailing fields around the resized name.
            Check(result.AsSpan(opaqueAfter.Offset - 13, 13).SequenceEqual(original.AsSpan(opaqueBefore.Offset - 13, 13)),
                "Unmodeled USER fields changed.");
            var restored = edited.WithMainValues(values).Serialize();
            Check(restored.AsSpan().SequenceEqual(original), "Reversing a Main edit did not restore every byte.");
            Check(save.Serialize().AsSpan().SequenceEqual(original), "Editing mutated the original object.");
        }

        foreach (var invalid in new[] { values with { Money = -1 }, values with { BondFragments = -1 },
            values with { IronIngots = -1 }, values with { SteelIngots = -1 }, values with { SilverIngots = -1 },
            values with { Difficulty = (Difficulty)9 }, values with { GameMode = (GameMode)9 },
            values with { SommieName = " " }, values with { SommieName = "bad\nname" },
            values with { SommieName = "\ud800" }, values with { SommieName = new string('x', 2049) } })
            Reject(() => save.WithMainValues(invalid), "Invalid Main values were accepted.");

        var maximum = values with { Money = MainLimits.MaxMoney, BondFragments = MainLimits.MaxBondFragments,
            IronIngots = MainLimits.MaxIngots, SteelIngots = MainLimits.MaxIngots, SilverIngots = MainLimits.MaxIngots };
        Check(save.WithMainValues(maximum).ReadMainValues() == maximum, "A game resource limit was rejected.");
        var zero = values with { Money = 0, BondFragments = 0, IronIngots = 0, SteelIngots = 0, SilverIngots = 0 };
        Check(save.WithMainValues(zero).ReadMainValues() == zero, "Zero resource amounts were rejected.");
        foreach (var invalid in new[] { values with { Money = MainLimits.MaxMoney + 1 },
            values with { BondFragments = MainLimits.MaxBondFragments + 1 },
            values with { IronIngots = MainLimits.MaxIngots + 1 }, values with { SteelIngots = MainLimits.MaxIngots + 1 },
            values with { SilverIngots = MainLimits.MaxIngots + 1 }, values with { Money = int.MaxValue } })
            Reject(() => save.WithMainValues(invalid), "An amount above the game's limit was accepted.");

        foreach (byte[] invalid in new[] { Fixture(userVersion: 19), Fixture(variableVersion: 1), Fixture(duplicateMaterial: true),
            Fixture(missingMaterial: true), Fixture(materialString: true), Fixture(unknownType: true) })
            Reject(() => EngageSave.Parse(invalid).ReadMainValues(), "An unsupported USER layout was editable.");
        foreach ((int offset, uint value) in new[] { (0, 10u), (268, 19u), (317, uint.MaxValue),
            (353, uint.MaxValue), (357, uint.MaxValue), (60, 0u) })
        {
            byte[] invalid = (byte[])original.Clone();
            Write32(invalid, offset, value);
            Seal(invalid);
            Reject(() => EngageSave.Parse(invalid).ReadMainValues(), "A malformed Main field was accepted.");
        }
        byte[] invalidUnicode = (byte[])original.Clone();
        invalidUnicode[361] = 0;
        invalidUnicode[362] = 0xd8;
        Seal(invalidUnicode);
        Reject(() => EngageSave.Parse(invalidUnicode).ReadMainValues(), "Malformed UTF-16 was accepted.");
        byte[] negativeAmount = (byte[])original.Clone();
        // The final word of the first variable record is its integer amount.
        int firstAmount = 361 + Encoding.Unicode.GetByteCount("G_所持_IID_てつの晶石") + 1;
        Write32(negativeAmount, firstAmount, uint.MaxValue);
        Seal(negativeAmount);
        Reject(() => EngageSave.Parse(negativeAmount).ReadMainValues(), "An unsupported negative resource was silently clamped.");

        string source = Path.Combine(temporary, "main-fixture");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditMain, "The Main panel was not enabled.");
        Check(window.FindControl<Grid>("MainPanel")!.IsVisible, "Main is not the default editable panel.");
        var money = window.FindControl<NumericUpDown>("MoneyInput")!;
        var difficulty = window.FindControl<ComboBox>("DifficultyInput")!;
        var nameInput = window.FindControl<TextBox>("SommieNameInput")!;
        Check(money.Value == 5000 && difficulty.SelectedIndex == 1 && nameInput.Text == "Sommie", "GUI values are empty or incorrect.");
        foreach ((string name, int maximumValue) in new[] { ("MoneyInput", MainLimits.MaxMoney),
            ("BondFragmentsInput", MainLimits.MaxBondFragments), ("IronIngotsInput", MainLimits.MaxIngots),
            ("SteelIngotsInput", MainLimits.MaxIngots), ("SilverIngotsInput", MainLimits.MaxIngots) })
        {
            var input = window.FindControl<NumericUpDown>(name)!;
            Check(input.Minimum == 0 && input.Maximum == maximumValue, "A GUI resource range differs from the core.");
            input.Text = (maximumValue + 1).ToString();
            Check(!window.ApplyMainValues(), "The GUI accepted an amount above its resource limit.");
            Check(window.Save!.Serialize().AsSpan().SequenceEqual(original), "A rejected range changed the loaded save.");
            Check(window.LoadSave(source), "Could not restore the valid GUI fixture.");
            input.Text = maximumValue.ToString();
            Check(window.ApplyMainValues(), "The GUI rejected an amount at its limit.");
            Check(window.LoadSave(source), "Could not restore the valid GUI fixture.");
        }
        money.Value = 777;
        difficulty.SelectedIndex = 0;
        nameInput.Text = "索拉";
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans");
            Dispatcher.UIThread.RunJobs();
            Check(Equals(difficulty.Items[0], "普通"), "Difficulty options did not translate.");
            window.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Check(Equals(difficulty.Items[0], "Normal"), "English difficulty options did not recover.");
            Check(money.Value == 777 && difficulty.SelectedIndex == 0 && nameInput.Text == "索拉",
                "Language switching discarded pending edits.");
        }
        string copy = Path.Combine(temporary, "main-gui-copy");
        Check(window.SaveCopy(copy), "Saving did not apply pending form values.");
        Check(EngageSave.Load(copy).ReadMainValues() == values with { Money = 777, Difficulty = Difficulty.Normal, SommieName = "索拉" },
            "The GUI wrote different values than requested.");
        byte[] beforeInvalid = window.Save!.Serialize();
        foreach (string invalidAmount in new[] { "", "abc", "-1", "1.5", "10000000", "2147483648" })
        {
            money.Text = invalidAmount;
            Check(!window.ApplyMainValues(), "An invalid amount silently saved its previous value.");
            Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeInvalid), "An invalid amount changed the loaded save.");
        }
        money.Value = 777;
        money.Text = "777";
        nameInput.Text = "";
        string rejected = Path.Combine(temporary, "main-rejected");
        Check(!window.SaveCopy(rejected) && !File.Exists(rejected), "An invalid form created an output file.");
        Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeInvalid), "An invalid form changed the loaded save.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "The original save was overwritten.");
        byte[] aboveLimit = (byte[])original.Clone();
        Write32(aboveLimit, firstAmount, MainLimits.MaxIngots + 1u);
        Seal(aboveLimit);
        var unusualSave = EngageSave.Parse(aboveLimit);
        Check(unusualSave.ReadMainValues().IronIngots == MainLimits.MaxIngots + 1,
            "Reading an existing over-limit value silently clamped it.");
        Reject(() => unusualSave.WithMainValues(unusualSave.ReadMainValues()), "A no-op bypassed range validation.");
        string unusualSource = Path.Combine(temporary, "main-above-limit");
        File.WriteAllBytes(unusualSource, aboveLimit);
        Check(window.LoadSave(unusualSource) && !window.CanEditMain, "An over-limit save enabled bounded form controls.");
        Check(window.FindControl<NumericUpDown>("IronIngotsInput")!.Value is null, "An over-limit save was clamped into the form.");
        string unusualCopy = Path.Combine(temporary, "main-above-limit-copy");
        Check(window.SaveCopy(unusualCopy) && File.ReadAllBytes(unusualCopy).AsSpan().SequenceEqual(aboveLimit),
            "An existing over-limit save could not be copied without changing its bytes.");
        Console.WriteLine("Main core, preservation, malformed-input and GUI tests passed.");
    }

    public static byte[] Fixture(uint userVersion = 20, uint variableVersion = 0, bool duplicateMaterial = false,
        bool missingMaterial = false, bool materialString = false, bool unknownType = false)
    {
        using var variables = new MemoryStream();
        using var vw = new BinaryWriter(variables, Encoding.UTF8, leaveOpen: true);
        vw.Write(0u);
        vw.Write(variableVersion);
        vw.Write(new byte[28]);
        var keys = new List<string> { "G_所持_IID_てつの晶石", "G_所持_IID_はがねの晶石", "G_所持_IID_ぎんの晶石" };
        if (duplicateMaterial) keys.Add(keys[0]);
        if (missingMaterial) keys.RemoveAt(2);
        vw.Write((uint)(keys.Count + 2));
        int[] amounts = [12, 8, 3, 42];
        for (int index = 0; index < keys.Count; index++)
        {
            String(vw, keys[index]);
            vw.Write((byte)(materialString && index == 0 ? 1 : 0));
            if (materialString && index == 0) String(vw, "wrong type");
            else vw.Write(amounts[index]);
        }
        String(vw, "unrelated string variable"); vw.Write((byte)1); String(vw, "preserve me");
        String(vw, "G_所持_IID_てつの晶石_extra"); vw.Write((byte)(unknownType ? 2 : 0)); vw.Write(333);
        byte[] variableBytes = variables.ToArray();
        Write32(variableBytes, 0, (uint)variableBytes.Length);

        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
        writer.Write(userVersion); writer.Write(new byte[28]); writer.Write(3u); writer.Write((byte)1);
        writer.Write((byte)GameMode.Classic); writer.Write((byte)Difficulty.Hard);
        writer.Write((byte)2); writer.Write((byte)2); writer.Write(0x12345678u); writer.Write(7u);
        writer.Write(variableBytes); writer.Write(5000); writer.Write(1u); writer.Write(2u); writer.Write(3u); writer.Write(4u);
        String(writer, "M001"); writer.Write(1200); writer.Write(999); String(writer, "Sommie");
        writer.Write(Encoding.ASCII.GetBytes("unknown-tail!"));
        byte[] user = Section("RESU", payload.ToArray());
        byte[] opaque = Section("PMAP", [0, 255, 42, 7, 6, 5, 4, 3]);
        var result = new byte[260 + user.Length + opaque.Length + 8];
        Write32(result, 0, 9); Write32(result, 4, 0x130);
        result[12] = 2; result[17] = 2; result[23] = 1; result[25] = 1;
        Write32(result, 60, Checksum(result.AsSpan(0, 60)));
        "EDNI"u8.CopyTo(result.AsSpan(128));
        Write32(result, 132, 260); Write32(result, 136, (uint)(260 + user.Length));
        Write32(result, 140, (uint)(260 + user.Length + opaque.Length));
        user.CopyTo(result, 260); opaque.CopyTo(result, 260 + user.Length);
        "LVRC"u8.CopyTo(result.AsSpan(result.Length - 8));
        Seal(result);
        return result;
    }

    private static byte[] Section(string tag, byte[] payload)
    {
        byte[] bytes = new byte[payload.Length + 8];
        Encoding.ASCII.GetBytes(tag).CopyTo(bytes, 0);
        Write32(bytes, 4, (uint)(payload.Length + 4));
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static void String(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void Seal(byte[] bytes) => Write32(bytes, bytes.Length - 4, Checksum(bytes.AsSpan(0, bytes.Length - 4)));
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        return ~crc;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
