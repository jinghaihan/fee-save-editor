using System.Text.Json;
using FeeEditor.Core;

namespace FeeEditor.Cli;

internal static class ItemsCommand
{
    public static int Run(string[] args)
    {
        switch (args)
        {
            case ["catalog", .. var options] when options is [] or ["--json"]:
                Print(ItemCatalog.Items.Select(item => new
                {
                    item.Id, item.English, item.MaxUses, item.MaxRefine, item.UnlimitedUses
                }));
                return 0;
            case ["list", var source, .. var options] when options is [] or ["--json"]:
                var slots = EngageSave.Load(source).ReadInventory();
                Print(new
                {
                    Capacity = slots.Count,
                    Occupied = slots.Count(slot => slot.Item is not null),
                    Items = slots.Where(slot => slot.Item is not null).Select(slot =>
                    {
                        var item = slot.Item!;
                        var definition = ItemCatalog.Find(item.ItemHash);
                        return new
                        {
                            slot.Slot, Id = definition?.Id,
                            Name = definition?.English ?? $"Unknown item (0x{item.ItemHash:X8})",
                            item.ItemHash, item.Uses, item.RefineLevel, item.Flags, item.EngravingHash
                        };
                    })
                });
                return 0;
            case [var verb, var source, var destination, .. var options]:
                var save = EngageSave.Load(source);
                Edit(save, verb, options).WriteCopy(destination);
                Console.WriteLine(Path.GetFullPath(destination));
                return 0;
            default:
                throw new ArgumentException("Unknown items command. Run --help for usage.");
        }
    }

    private static EngageSave Edit(EngageSave save, string verb, string[] options)
    {
        if (verb == "restore" && options is ["--all"])
            return save.RestoreInventoryUses();
        string[] allowed = verb switch
        {
            "set" => ["--slot", "--item", "--uses", "--refine"],
            "add" => ["--item", "--uses", "--refine"],
            "delete" or "restore" => ["--slot"],
            _ => throw new ArgumentException("Unknown items command. Run --help for usage.")
        };
        var values = Options(options, allowed);
        if (verb == "add")
        {
            int? addedUses = values.TryGetValue("--uses", out string? text) ? Number(text) : null;
            int addedRefine = values.TryGetValue("--refine", out string? level) ? Number(level) : 0;
            return save.AddInventoryItem(Required(values, "--item"), addedUses, addedRefine);
        }
        int slot = Number(Required(values, "--slot"));
        if (verb == "delete")
            return save.DeleteInventoryItem(slot);
        if (verb == "restore")
            return save.RestoreInventoryUses(slot);
        var slots = save.ReadInventory();
        if (slot >= slots.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var old = slots[slot].Item;
        string id = values.GetValueOrDefault("--item") ?? ItemCatalog.Find(old?.ItemHash ?? 0)?.Id
            ?? throw new ArgumentException("Choose an item with --item for an empty or unknown slot.");
        var definition = ItemCatalog.Get(id);
        bool sameItem = old?.ItemHash == definition.Hash;
        int uses = sameItem ? old!.Uses : definition.MaxUses;
        int refine = sameItem ? old!.RefineLevel : 0;
        if (values.TryGetValue("--uses", out string? usesText))
            uses = Number(usesText);
        if (values.TryGetValue("--refine", out string? refineText))
            refine = Number(refineText);
        return save.WithInventoryItem(slot, id, uses, refine);
    }

    private static Dictionary<string, string> Options(string[] options, string[] allowed)
    {
        if (options.Length == 0 || options.Length % 2 != 0)
            throw new ArgumentException("Provide item options and their values.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < options.Length; index += 2)
        {
            string key = options[index];
            if (!allowed.Contains(key))
                throw new ArgumentException($"Unknown item option: {key}");
            if (!result.TryAdd(key, options[index + 1]))
                throw new ArgumentException($"Duplicate option: {key}");
        }
        return result;
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.GetValueOrDefault(key) ?? throw new ArgumentException($"Missing item option: {key}");

    private static int Number(string value)
    {
        if (!int.TryParse(value, out int number) || number < 0)
            throw new ArgumentException("Item values must be nonnegative whole numbers within their game limits.");
        return number;
    }

    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));
}
