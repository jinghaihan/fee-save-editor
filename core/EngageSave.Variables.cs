namespace FeeEditor.Core;

public sealed record GameVariable(string Key, int Value);

public sealed partial class EngageSave
{
    public IReadOnlyList<GameVariable> ReadGameVariables()
    {
        var layout = GameVariableLayout.Read(this, _bytes);
        if (layout.Entries.Where(entry => entry.Key is not null).GroupBy(entry => entry.Key).Any(group => group.Count() > 1))
            throw new InvalidDataException("The game-variable block contains duplicate keys.");
        return layout.Entries.Where(entry => entry.Type == 0 && entry.Key is not null)
            .Select(entry => new GameVariable(entry.Key!, unchecked((int)ReadUInt32(_bytes, entry.ValueOffset))))
            .ToArray();
    }

    public EngageSave WithGameVariable(string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key) || !ReadGameVariables().Any(row => row.Key == key))
            throw new ArgumentException("Select an existing numeric game variable.", nameof(key));
        return WithGameIntegerValues(new Dictionary<string, int>(StringComparer.Ordinal) { [key] = value });
    }
}
