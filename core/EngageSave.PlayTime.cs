using System.Buffers.Binary;

namespace FeeEditor.Core;

public sealed partial class EngageSave
{
    public const float MaxPlayTimeSeconds = 3_599_999.5f;

    public float ReadPlayTimeSeconds()
    {
        var section = PlayTimeSection();
        float seconds = BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(section.PayloadOffset + 32, 4));
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > MaxPlayTimeSeconds)
            throw new InvalidDataException("The saved play time is outside the game's supported range.");
        return seconds;
    }

    public EngageSave WithPlayTimeSeconds(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > MaxPlayTimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Play time must be between 0 and 3,599,999.5 seconds.");
        var section = PlayTimeSection();
        var main = MainLayout.Read(this, _bytes);
        // With the verified hashed chapter encoding, the summary stores play time at 0x28.
        const int summaryOffset = 40;
        if (main.HeaderChecksumOffset < summaryOffset + 4)
            throw new InvalidDataException("The save summary does not contain a supported play-time field.");
        if (ReadPlayTimeSeconds() == seconds
            && BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(summaryOffset, 4)) == seconds)
            return this;

        byte[] edited = Serialize();
        BinaryPrimitives.WriteSingleLittleEndian(edited.AsSpan(section.PayloadOffset + 32, 4), seconds);
        BinaryPrimitives.WriteSingleLittleEndian(edited.AsSpan(summaryOffset, 4), seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(edited.AsSpan(main.HeaderChecksumOffset, 4),
            Crc32.Compute(edited.AsSpan(0, main.HeaderChecksumOffset)));
        BinaryPrimitives.WriteUInt32LittleEndian(edited.AsSpan(edited.Length - 4),
            Crc32.Compute(edited.AsSpan(0, edited.Length - 4)));
        var result = Parse(edited);
        if (result.ReadPlayTimeSeconds() != seconds)
            throw new InvalidDataException("Edited play time did not survive serialization.");
        return result;
    }

    private SaveSection PlayTimeSection()
    {
        if (Kind != SaveKind.Game || FormatVersion != 9)
            throw new InvalidDataException("Play-time editing requires a format-version 9 game save.");
        var sections = Sections.Where(section => section.Name == "TIME").ToArray();
        if (sections.Length != 1 || sections[0].Length != 40
            || ReadUInt32(_bytes, sections[0].PayloadOffset) != 0)
            throw new InvalidDataException("The save does not contain a supported TIME section.");
        return sections[0];
    }
}
