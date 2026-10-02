using System.Buffers.Binary;
using System.Text;

namespace FeeEditor.Core;

public enum Difficulty { Normal, Hard, Maddening }
public enum GameMode { Casual, Classic }

public sealed record MainValues(int Money, int BondFragments, int IronIngots, int SteelIngots,
    int SilverIngots, Difficulty Difficulty, GameMode GameMode, string SommieName);

internal sealed class MainLayout
{
    private static readonly Encoding Unicode = new UnicodeEncoding(false, false, true);
    private static readonly string[] MaterialKeys =
    [
        "G_所持_IID_てつの晶石", "G_所持_IID_はがねの晶石", "G_所持_IID_ぎんの晶石"
    ];

    public required SaveSection Section { get; init; }
    public required MainValues Values { get; init; }
    public required int HeaderChecksumOffset { get; init; }
    public required int MoneyOffset { get; init; }
    public required int BondOffset { get; init; }
    public required int NameOffset { get; init; }
    public required int NameEnd { get; init; }
    public required int[] MaterialOffsets { get; init; }
    public int SettingsOffset => Section.Offset + 40;

    public static MainLayout Read(EngageSave save, byte[] bytes)
    {
        if (save.Kind != SaveKind.Game || save.FormatVersion != 9)
            throw new InvalidDataException("Main editing requires an Engage format-version 9 game save.");
        var sections = save.Sections.Where(section => section.Name == "USER").ToArray();
        if (sections.Length != 1)
            throw new InvalidDataException("The game save must contain one USER section.");
        var section = sections[0];
        var reader = new Reader(bytes, section.PayloadOffset, section.PayloadOffset + section.Length);
        if (reader.ReadUInt32() != 20)
            throw new InvalidDataException("Unsupported USER section version.");
        reader.Skip(28);
        reader.Skip(4); // User status.
        reader.Skip(1); // Sequence.
        var mode = (GameMode)reader.ReadByte();
        var difficulty = (Difficulty)reader.ReadByte();
        reader.Skip(10); // Original difficulty, version, chapter hash and context index.
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(difficulty))
            throw new InvalidDataException("The save contains an unknown difficulty or game mode.");

        int variablesStart = reader.Position;
        uint blockSize = reader.ReadUInt32();
        if (blockSize < 40 || blockSize > reader.End - variablesStart)
            throw new InvalidDataException("Invalid game-variable block size.");
        int variablesEnd = variablesStart + (int)blockSize;
        if (reader.ReadUInt32() != 0)
            throw new InvalidDataException("Unsupported game-variable block version.");
        reader.Skip(28);
        var variables = new Reader(bytes, reader.Position, variablesEnd);
        uint count = variables.ReadUInt32();
        if (count > (variables.End - variables.Position) / 9)
            throw new InvalidDataException("Invalid game-variable count.");
        var materialOffsets = Enumerable.Repeat(-1, MaterialKeys.Length).ToArray();
        for (uint index = 0; index < count; index++)
        {
            string key = variables.ReadString();
            byte type = variables.ReadByte();
            int material = Array.IndexOf(MaterialKeys, key);
            if (material >= 0)
            {
                if (type != 0 || materialOffsets[material] != -1)
                    throw new InvalidDataException("A material variable is duplicated or has an invalid type.");
                materialOffsets[material] = variables.Position;
            }
            switch (type)
            {
                case 0: variables.Skip(4); break;
                case 1: variables.ReadString(); break;
                default: throw new InvalidDataException("Unsupported game-variable type.");
            }
        }
        if (variables.Position != variablesEnd || materialOffsets.Contains(-1))
            throw new InvalidDataException("The game-variable block is incomplete.");

        reader = new Reader(bytes, variablesEnd, reader.End);
        int moneyOffset = reader.Position;
        int money = reader.ReadInt32();
        reader.Skip(16); // Progress, training count, arena count and unit-info mode.
        reader.ReadString(); // Current world-map spot.
        int bondOffset = reader.Position;
        int bond = reader.ReadInt32();
        reader.Skip(4); // Lifetime earned bond fragments, not spendable fragments.
        int nameOffset = reader.Position;
        string name = reader.ReadString();

        // Chapter IDs preceding the summary settings use the verified hash encoding.
        if (bytes[12] != 2 || bytes[17] != 2)
            throw new InvalidDataException("Unsupported chapter encoding in the save summary.");
        var checksums = new List<int>();
        for (int offset = 26; offset <= 124; offset++)
            if (ReadInt32(bytes, offset) == unchecked((int)Crc32.Compute(bytes.AsSpan(0, offset))))
                checksums.Add(offset);
        if (checksums.Count != 1)
            throw new InvalidDataException("The save summary checksum is invalid or ambiguous.");

        var values = new MainValues(money, bond, ReadInt32(bytes, materialOffsets[0]),
            ReadInt32(bytes, materialOffsets[1]), ReadInt32(bytes, materialOffsets[2]), difficulty, mode, name);
        if (HasNegativeAmount(values))
            throw new InvalidDataException("A resource amount is outside the supported nonnegative range.");
        return new MainLayout
        {
            Section = section, MoneyOffset = moneyOffset, BondOffset = bondOffset,
            NameOffset = nameOffset, NameEnd = reader.Position, MaterialOffsets = materialOffsets,
            HeaderChecksumOffset = checksums[0],
            Values = values
        };
    }

    public byte[] Edit(byte[] bytes, MainValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        MainLimits.ValidateAmounts(values);
        if (!Enum.IsDefined(values.Difficulty) || !Enum.IsDefined(values.GameMode))
            throw new ArgumentException("Select a valid difficulty and game mode.");
        if (string.IsNullOrWhiteSpace(values.SommieName) || values.SommieName.Any(char.IsControl))
            throw new ArgumentException("Sommie needs a nonempty name without control characters.");
        byte[] name = Unicode.GetBytes(values.SommieName);
        if (name.Length > 4096)
            throw new ArgumentException("The name exceeds the supported serialized string size.");

        byte[] edited = (byte[])bytes.Clone();
        WriteInt32(edited, MoneyOffset, values.Money);
        WriteInt32(edited, BondOffset, values.BondFragments);
        WriteInt32(edited, MaterialOffsets[0], values.IronIngots);
        WriteInt32(edited, MaterialOffsets[1], values.SteelIngots);
        WriteInt32(edited, MaterialOffsets[2], values.SilverIngots);
        edited[SettingsOffset + 5] = (byte)values.GameMode;
        edited[SettingsOffset + 6] = (byte)values.Difficulty;
        edited[23] = (byte)values.Difficulty;
        edited[25] = (byte)values.GameMode;
        WriteInt32(edited, HeaderChecksumOffset, unchecked((int)Crc32.Compute(edited.AsSpan(0, HeaderChecksumOffset))));

        using var output = new MemoryStream();
        output.Write(edited.AsSpan(0, NameOffset));
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            writer.Write(name.Length);
        output.Write(name);
        output.Write(edited.AsSpan(NameEnd));
        byte[] result = output.ToArray();
        int delta = 4 + name.Length - (NameEnd - NameOffset);
        WriteInt32(result, Section.Offset + 4, Section.Length + 4 + delta);
        for (int slot = 0; slot < 32; slot++)
        {
            int indexOffset = 132 + slot * 4;
            int offset = ReadInt32(result, indexOffset);
            if (offset > Section.Offset)
                WriteInt32(result, indexOffset, offset + delta);
        }
        WriteInt32(result, result.Length - 4, unchecked((int)Crc32.Compute(result.AsSpan(0, result.Length - 4))));
        return result;
    }

    private static bool HasNegativeAmount(MainValues values) => values.Money < 0 || values.BondFragments < 0 ||
        values.IronIngots < 0 || values.SteelIngots < 0 || values.SilverIngots < 0;

    private static int ReadInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void WriteInt32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private sealed class Reader(byte[] bytes, int position, int end)
    {
        public int Position { get; private set; } = position;
        public int End { get; } = end;

        public void Skip(int length)
        {
            if (length < 0 || length > End - Position)
                throw new InvalidDataException("A USER field extends beyond its data block.");
            Position += length;
        }

        public byte ReadByte()
        {
            int offset = Position;
            Skip(1);
            return bytes[offset];
        }

        public uint ReadUInt32()
        {
            int offset = Position;
            Skip(4);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        }
        public int ReadInt32() => unchecked((int)ReadUInt32());

        public string ReadString()
        {
            uint length = ReadUInt32();
            if (length > 4096 || (length & 1) != 0)
                throw new InvalidDataException("Invalid UTF-16 string size.");
            int start = Position;
            Skip((int)length);
            try
            {
                return Unicode.GetString(bytes, start, (int)length);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Invalid UTF-16 string.", error);
            }
        }
    }
}
