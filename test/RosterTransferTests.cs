using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterTransferTests
{
    private const string Canter = "SID_再移動";

    public static void Run(MainWindow window, string temporary)
    {
        var save = EngageSave.Parse(RosterTests.Fixture());
        byte[] snapshot = save.ExportRosterCharacter(0);
        Exact(save, save.ImportRosterCharacter(0, snapshot), "No-op character transfer was not byte-identical.");
        Reject(() => save.ImportRosterCharacter(1, snapshot), "A different character accepted the transfer.");
        Reject(() => EngageSave.Parse(RosterTests.Fixture(force: UnitForce.Enemy)).ExportRosterCharacter(0),
            "An enemy was exportable.");
        var trained = save.WithRosterValues(0, new(10, 50, 900)).WithRosterPersonalStat(0, RosterStat.Strength, 18)
            .WithRosterItem(0, 3, "IID_リカバー", 10, 0).WithRosterCondition(0, 7, 12)
            .WithRosterSkill(0, Canter, true).WithRosterEquippedSkills(0, ItemCatalog.Hash(Canter), null);
        byte[] trainedFile = trained.ExportRosterCharacter(0);
        var imported = save.ImportRosterCharacter(0, trainedFile);
        Exact(trained, imported, "Character transfer lost skill metadata, item flags or unrelated bytes.");
        Exact(save, imported.ImportRosterCharacter(0, snapshot), "Restoring a transferred character was not exact.");
        var swordmaster = trained.WithRosterClass(0, "JID_ソードマスター", 2).WithRosterValues(0, new(8, 12, 900))
            .WithRosterClassSkill(0, true);
        Exact(swordmaster, save.ImportRosterCharacter(0, swordmaster.ExportRosterCharacter(0)),
            "Reclassed character transfer lost progression or class skill.");
        foreach (var person in RosterCatalog.Persons)
        {
            var owner = EngageSave.Parse(RosterTests.Fixture(mutate: payload =>
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(34 + 46, 4), person.Hash)));
            var birth = RosterCatalog.Class(ItemCatalog.Hash(person.BirthClass))!;
            owner = owner.WithRosterClass(0, birth.Id, birth.WeaponVariants().First());
            Exact(owner, owner.ImportRosterCharacter(0, owner.ExportRosterCharacter(0)),
                $"{person.Id}: native/exclusive class transfer changed bytes.");
            var updated = owner.WithRosterValues(0, owner.ReadRoster()[0].Values with { SkillPoints = 888 });
            Exact(updated, owner.ImportRosterCharacter(0, updated.ExportRosterCharacter(0)),
                $"{person.Id}: native/exclusive class transfer lost values.");
        }

        foreach (Action<JsonObject> mutate in new Action<JsonObject>[]
        {
            node => node["Version"] = 2, node => node["GameVersion"] = 0,
            node => node["Gender"] = 2, node => node["Format"] = "other",
            node => node["Values"]!["Level"] = 0, node => node["Values"]!["Experience"] = 100,
            node => node["Values"]!["SkillPoints"] = 10000,
            node => node["InternalLevel"] = 101, node => node["CurrentHP"] = 256,
            node => node["PersonalStats"]![1] = 128, node => node["PersonalStats"]![9] = 1,
            node => node["ClassHash"] = ItemCatalog.Hash("JID_ダンサー"),
            node => node["SelectedWeapons"] = 8, node => node["Proficiencies"] = 0,
            node => node["ClassSkill"] = ItemCatalog.Hash(Canter),
            node => node["InheritedSkills"] = new JsonArray(0x12345678u),
            node => node["EquippedSkills"] = new JsonArray(ItemCatalog.Hash(Canter)),
            node => node["Items"]![0]!["Uses"] = 11,
            node => node["Items"]![0]!["ItemHash"] = ItemCatalog.Hash("IID_エンゲージ枠"),
            node => node["Items"]![0]!["EngravingHash"] = 0x12345678u,
            node => node["PersonalStats"] = null, node => node["Items"] = new JsonArray(),
            node => node["Values"] = null, node => node["InheritedSkills"] = null,
            node => node["Extra"] = 1, node => node.Remove("PersonHash"),
            node => node["Values"]!.AsObject().Remove("Experience"),
            node => node["Items"]![0]!.AsObject().Remove("Uses")
        })
        {
            byte[] invalid = Change(snapshot, mutate);
            Reject(() => save.ImportRosterCharacter(0, invalid), $"Malformed or out-of-range transfer was accepted: {Encoding.UTF8.GetString(invalid)}");
            Exact(save, EngageSave.Parse(RosterTests.Fixture()), "Rejected transfer changed the source.");
        }
        foreach (byte[] invalid in new[] { Array.Empty<byte>(), new byte[1_048_577], Encoding.UTF8.GetBytes("{}"),
            Encoding.UTF8.GetBytes("null"), snapshot[..^1],
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(snapshot).Replace("\"Version\": 1", "\"Version\": 1, \"Version\": 1")) })
            Reject(() => save.ImportRosterCharacter(0, invalid), "Invalid character JSON was accepted.");
        var reserved = EngageSave.Parse(RosterTests.Fixture(mutate: bytes =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34 + 137, 4), ItemCatalog.Hash("IID_エンゲージ枠"))));
        Reject(() => reserved.ImportRosterCharacter(0, snapshot), "A reserved equipment slot was replaced.");
        var conflict = EngravingTests.Fixture();
        var conflictSave = EngageSave.Parse(conflict);
        byte[] conflictFile = Change(conflictSave.ExportRosterCharacter(0), node =>
            node["Items"]![1]!["EngravingHash"] = ItemCatalog.Hash(EngravingTests.Marth));
        Reject(() => conflictSave.ImportRosterCharacter(0, conflictFile), "An engraving was taken from another weapon.");
        string? alias = EngravingCatalog.Get(EngravingTests.Marth).Aliases.FirstOrDefault();
        if (alias is not null)
        {
            byte[] aliasFile = Change(conflictSave.ExportRosterCharacter(0), node =>
                node["Items"]![1]!["EngravingHash"] = ItemCatalog.Hash(alias));
            Reject(() => conflictSave.ImportRosterCharacter(0, aliasFile), "An engraving alias bypassed unique ownership.");
        }
        var engraved = conflictSave.WithRosterEngraving(0, 1, "GID_セリカ");
        Exact(engraved, conflictSave.ImportRosterCharacter(0, engraved.ExportRosterCharacter(0)),
            "A valid portable engraving changed another weapon or lost item flags.");
        byte[] missingEquipped = Change(trainedFile, node => node["InheritedSkills"] = new JsonArray());
        Reject(() => trained.ImportRosterCharacter(0, missingEquipped), "Removing an inherited pool left its equipped skill unlocked.");

        string source = Path.Combine(temporary, "transfer-source");
        string path = Path.Combine(temporary, "Alear.fee-character.json");
        File.WriteAllBytes(source, save.Serialize());
        Check(window.LoadSave(source), "Could not load transfer GUI fixture.");
        window.ShowRoster();
        window.FindControl<NumericUpDown>("RosterLevel")!.Text = "21";
        string invalidExport = Path.Combine(temporary, "invalid-export.json");
        Check(!window.ExportSelectedRosterCharacter(invalidExport) && !File.Exists(invalidExport),
            "Invalid pending editor values created a character file.");
        Exact(save, window.Save!, "Rejected export changed the loaded save.");
        window.FindControl<NumericUpDown>("RosterLevel")!.Text = "5";
        window.FindControl<NumericUpDown>("RosterSkillPoints")!.Text = "901";
        Check(window.ExportSelectedRosterCharacter(path), "GUI export failed.");
        Check(JsonNode.Parse(File.ReadAllBytes(path))!["Values"]!["SkillPoints"]!.GetValue<int>() == 901,
            "Export ignored pending editor values.");
        Exact(save, window.Save!, "Export changed the loaded save.");
        Check(!window.ExportSelectedRosterCharacter(path), "Export overwrote an existing character file.");
        Check(window.ImportSelectedRosterCharacter(path), "GUI import failed.");
        Check(window.FindControl<NumericUpDown>("RosterSkillPoints")!.Text == "901", "Import did not refresh controls.");
        for (int pass = 0; pass < 2; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(window.FindControl<Button>("ExportRosterCharacterButton")!.Content?.ToString() == "导出", "Export did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(window.FindControl<Button>("ImportRosterCharacterButton")!.Content?.ToString() == "Import", "Import did not recover English.");
        }
        byte[] beforeReject = window.Save!.Serialize();
        string invalidPath = Path.Combine(temporary, "wrong-character.json");
        File.WriteAllBytes(invalidPath, save.ExportRosterCharacter(1));
        Check(!window.ImportSelectedRosterCharacter(invalidPath), "GUI accepted the wrong character.");
        Check(window.Save.Serialize().AsSpan().SequenceEqual(beforeReject), "Rejected GUI import changed the save.");
        string copy = Path.Combine(temporary, "transfer-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadRoster()[0].Values.SkillPoints == 901,
            "Imported values did not survive File Save Copy.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(save.Serialize()), "GUI transfer overwrote the private source.");
        RosterTransfer.WriteNew(Path.Combine(temporary, "transfer-new.json"), snapshot);
        Check(!Directory.EnumerateFiles(temporary, ".fee-character-*.tmp").Any(), "Export left temporary files behind.");
        Console.WriteLine("Roster transfer: identity, bounds, class/skills/items, exact restoration, engraving conflicts, translations and File Save Copy passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        foreach (var character in save.ReadRoster().Where(row => row.Force is not UnitForce.Enemy and not UnitForce.Temporary
            && RosterCatalog.Person(row.PersonHash) is not null))
        {
            byte[] snapshot = save.ExportRosterCharacter(character.Index);
            Exact(save, save.ImportRosterCharacter(character.Index, snapshot), "Real-save no-op transfer changed bytes.");
            var changed = save.WithRosterValues(character.Index, character.Values with
                { SkillPoints = character.Values.SkillPoints == 9999 ? 9998 : 9999 });
            Exact(save, changed.ImportRosterCharacter(character.Index, snapshot), "Real-save character restoration changed unrelated bytes.");
        }
        Console.WriteLine("Real roster transfers: every known playable character round-tripped without changing opaque data.");
    }

    private static byte[] Change(byte[] bytes, Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(bytes)!.AsObject();
        mutate(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }
    private static void Exact(EngageSave expected, EngageSave actual, string message) =>
        Check(expected.Serialize().AsSpan().SequenceEqual(actual.Serialize()), message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
}
