using System.Buffers.Binary;
using System.Text;

namespace FeeEditor.Core;

public enum SaveKind { Game, Global }

public sealed record SaveSection(string Name, int Offset, int Length)
{
    public int PayloadOffset => Offset + 8;
}

/// <summary>Reads the section container while preserving every original byte.</summary>
public sealed class EngageSave
{
    private const int IndexSize = 132;
    private readonly byte[] _bytes;

    public SaveKind Kind { get; }
    public uint? FormatVersion { get; }
    public uint? GameVersion { get; }
    public int Length => _bytes.Length;
    public uint Checksum { get; }
    public IReadOnlyList<SaveSection> Sections { get; }

    private EngageSave(byte[] bytes, SaveKind kind, List<SaveSection> sections)
    {
        _bytes = bytes;
        Kind = kind;
        Sections = sections.AsReadOnly();
        Checksum = ReadUInt32(bytes, bytes.Length - 4);
        if (kind == SaveKind.Game)
        {
            FormatVersion = ReadUInt32(bytes, 0);
            GameVersion = ReadUInt32(bytes, 4);
        }
    }

    public static EngageSave Load(string path) => Parse(File.ReadAllBytes(path));

    public static EngageSave Parse(ReadOnlySpan<byte> source)
    {
        if (source.Length < IndexSize + 8)
            throw new InvalidDataException("The save is truncated.");

        SaveKind kind;
        int indexOffset;
        if (source[..4].SequenceEqual("EDNI"u8))
        {
            kind = SaveKind.Global;
            indexOffset = 0;
        }
        else if (source.Length >= 128 + IndexSize + 8 && source.Slice(128, 4).SequenceEqual("EDNI"u8))
        {
            kind = SaveKind.Game;
            indexOffset = 128;
        }
        else
            throw new InvalidDataException("The Engage section index was not found.");

        int checksumOffset = source.Length - 4;
        if (!source.Slice(checksumOffset - 4, 4).SequenceEqual("LVRC"u8))
            throw new InvalidDataException("The checksum footer is missing.");
        if (ReadUInt32(source, checksumOffset) != Crc32.Compute(source[..checksumOffset]))
            throw new InvalidDataException("The save checksum does not match its contents.");

        var offsets = new List<int>();
        bool ended = false;
        for (int slot = 0; slot < 32; slot++)
        {
            uint offset = ReadUInt32(source, indexOffset + 4 + slot * 4);
            if (offset == 0)
            {
                ended = true;
                continue;
            }
            if (ended || offset > checksumOffset - 4)
                throw new InvalidDataException("The section index contains an invalid offset.");
            offsets.Add((int)offset);
        }

        if (offsets.Count < 2 || offsets[0] != indexOffset + IndexSize || offsets[^1] != checksumOffset - 4)
            throw new InvalidDataException("The section index does not cover the save body.");

        var sections = new List<SaveSection>();
        for (int slot = 0; slot < offsets.Count - 1; slot++)
        {
            int start = offsets[slot];
            int end = offsets[slot + 1];
            // The stored size includes the size word, but excludes the FourCC.
            if (end - start < 8 || ReadUInt32(source, start + 4) != end - start - 4)
                throw new InvalidDataException("A section has an invalid size or overlaps another section.");
            ReadOnlySpan<byte> tag = source.Slice(start, 4);
            foreach (byte value in tag)
                if (value < 0x20 || value > 0x7e)
                    throw new InvalidDataException("A section tag is not a printable FourCC.");
            string name = new string(Encoding.ASCII.GetString(tag).Reverse().ToArray()).TrimEnd();
            sections.Add(new SaveSection(name, start, end - start - 8));
        }

        return new EngageSave(source.ToArray(), kind, sections);
    }

    public byte[] Serialize() => (byte[])_bytes.Clone();

    public MainValues ReadMainValues() => MainLayout.Read(this, _bytes).Values;

    public EngageSave WithMainValues(MainValues values)
    {
        MainLimits.ValidateAmounts(values);
        var layout = MainLayout.Read(this, _bytes);
        if (layout.Values == values)
            return this;
        var edited = Parse(layout.Edit(_bytes, values));
        if (edited.ReadMainValues() != values)
            throw new InvalidDataException("The edited Main values did not survive serialization.");
        return edited;
    }

    /// <summary>Creates a new, verified copy. Never overwrites an existing file.</summary>
    public void WriteCopy(string destination)
    {
        string output = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The output directory does not exist.");
        if (File.Exists(output))
            throw new IOException("The output file already exists. Choose a new path.");

        string temporary = Path.Combine(directory, $".fee-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(_bytes);
                file.Flush(flushToDisk: true);
            }
            byte[] persisted = File.ReadAllBytes(temporary);
            Parse(persisted);
            if (!_bytes.AsSpan().SequenceEqual(persisted))
                throw new IOException("The written save differs from the in-memory save.");
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
}
