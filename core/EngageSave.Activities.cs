using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed record SomnielActivities(int TrainingRemaining, int ArenaRemaining)
{
    public const int MaxTraining = 1;
    public const int MaxArena = 3;

    internal void Validate()
    {
        if (TrainingRemaining is < 0 or > MaxTraining || ArenaRemaining is < 0 or > MaxArena)
            throw new ArgumentException("Remaining strength-training uses must be 0–1 and standard Arena uses 0–3.");
    }
}

public sealed partial class EngageSave
{
    public SomnielActivities ReadSomnielActivities()
    {
        var layout = MainLayout.Read(this, _bytes);
        // USER stores uses already spent, not uses remaining.
        int training = unchecked((int)ReadUInt32(_bytes, layout.MoneyOffset + 8));
        int arena = unchecked((int)ReadUInt32(_bytes, layout.MoneyOffset + 12));
        if (training is < 0 or > SomnielActivities.MaxTraining || arena is < 0 or > SomnielActivities.MaxArena)
            throw new InvalidDataException("The saved activity counters are outside the native game's limits.");
        return new(SomnielActivities.MaxTraining - training, SomnielActivities.MaxArena - arena);
    }

    public EngageSave WithSomnielActivities(SomnielActivities values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.Validate();
        if (ReadSomnielActivities() == values) return this;
        var layout = MainLayout.Read(this, _bytes);
        byte[] payload = _bytes.AsSpan(layout.Section.PayloadOffset, layout.Section.Length).ToArray();
        int offset = layout.MoneyOffset - layout.Section.PayloadOffset;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 8), SomnielActivities.MaxTraining - values.TrainingRemaining);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 12), SomnielActivities.MaxArena - values.ArenaRemaining);
        var edited = ReplaceSection(layout.Section, payload);
        if (edited.ReadSomnielActivities() != values)
            throw new InvalidDataException("Edited activity counters did not survive serialization.");
        return edited;
    }

    public EngageSave WithRestoredSomnielActivities() => WithSomnielActivities(new(SomnielActivities.MaxTraining, SomnielActivities.MaxArena));
}
