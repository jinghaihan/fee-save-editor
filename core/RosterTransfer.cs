using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeeEditor.Core;

public sealed record RosterTransferItem([property: JsonRequired] uint ItemHash, [property: JsonRequired] int Uses,
    [property: JsonRequired] int RefineLevel, [property: JsonRequired] uint? EngravingHash);

public sealed record RosterTransfer
{
    public const int MaximumFileSize = 1_048_576;
    public required string Format { get; init; }
    public required int Version { get; init; }
    public required uint GameVersion { get; init; }
    public required uint PersonHash { get; init; }
    public required int Gender { get; init; }
    public required uint ClassHash { get; init; }
    public required RosterValue Values { get; init; }
    public required int[] PersonalStats { get; init; }
    public required int CurrentHP { get; init; }
    public required int InternalLevel { get; init; }
    public required uint Proficiencies { get; init; }
    public required uint SelectedWeapons { get; init; }
    public required uint? ClassSkill { get; init; }
    public required uint[] InheritedSkills { get; init; }
    public required uint[] EquippedSkills { get; init; }
    public required RosterTransferItem?[] Items { get; init; }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static RosterTransferItem? Item(InventoryItem? item) => item is null ? null
        : new(item.ItemHash, item.Uses, item.RefineLevel, item.EngravingHash);

    internal static RosterTransfer Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumFileSize)
            throw new InvalidDataException("A character file must be between 1 byte and 1 MiB.");
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            CheckDuplicateProperties(document.RootElement);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Values", out var values) && values.ValueKind == JsonValueKind.Object
                && new[] { "Level", "Experience", "SkillPoints" }.Any(name => !values.TryGetProperty(name, out _)))
                throw new InvalidDataException("The character file is missing a progression value.");
            return JsonSerializer.Deserialize<RosterTransfer>(bytes, JsonOptions)
                ?? throw new InvalidDataException("The character file is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Invalid character JSON.", error);
        }
    }

    public static void WriteNew(string path, byte[] bytes)
    {
        Read(bytes);
        string output = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(output)!;
        string temporary = Path.Combine(directory, $".fee-character-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            byte[] persisted = File.ReadAllBytes(temporary);
            Read(persisted);
            if (!bytes.AsSpan().SequenceEqual(persisted))
                throw new IOException("The exported character differs from its in-memory data.");
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate character property: {property.Name}");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateProperties(child);
    }
}
