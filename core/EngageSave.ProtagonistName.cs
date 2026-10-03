using System.Text;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public string? ReadProtagonistName() => FindProtagonist(RosterLayout.Read(this, _bytes))?.Character.Progress.CustomName;

    private static CharacterLayout? FindProtagonist(RosterLayout layout)
    {
        var entries = layout.Characters.Where(character =>
            character.Character.PersonHash == ItemCatalog.Hash(EmblemCatalog.AlearPersonId)).ToArray();
        if (entries.Length > 1) throw new InvalidDataException("The save contains ambiguous protagonist records.");
        return entries.SingleOrDefault();
    }

    public EngageSave WithProtagonistName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
            throw new ArgumentException("The protagonist needs a nonempty name without control characters.", nameof(name));
        var unicode = new UnicodeEncoding(false, false, true);
        byte[] encoded;
        try { encoded = unicode.GetBytes(name); }
        catch (EncoderFallbackException error) { throw new ArgumentException("The name contains invalid Unicode.", nameof(name), error); }
        if (encoded.Length > 4096) throw new ArgumentException("The name exceeds the serialized string limit.", nameof(name));
        var layout = RosterLayout.Read(this, _bytes);
        var entry = FindProtagonist(layout)
            ?? throw new InvalidDataException("The save does not contain the protagonist.");
        if (entry.Progress.NameStart is not int start || entry.Progress.NameEnd is not int end)
            throw new InvalidDataException("The protagonist has no saved customization record.");
        if (entry.Character.Progress.CustomName == name) return this;
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(encoded.Length);
            writer.Write(encoded);
        }
        var edited = ReplaceCharacterRange(layout, entry, start, end, output.ToArray());
        if (edited.ReadProtagonistName() != name) throw new InvalidDataException("The edited name did not survive serialization.");
        return edited;
    }
}
