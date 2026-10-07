using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class PlayTimeTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = Fixture();
        var save = EngageSave.Parse(original);
        Check(save.ReadPlayTimeSeconds() == 632628.25f, "Play time was not read from TIME.");
        Check(ReferenceEquals(save, save.WithPlayTimeSeconds(save.ReadPlayTimeSeconds())), "No-op time edit changed the save.");
        foreach (float seconds in new[] { 0f, 1.5f, 864001f, EngageSave.MaxPlayTimeSeconds })
        {
            var edited = save.WithPlayTimeSeconds(seconds);
            Check(edited.ReadPlayTimeSeconds() == seconds, "Time edit did not round-trip.");
            byte[] bytes = edited.Serialize();
            Check(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(40, 4)) == seconds, "The save-slot time was not synchronized.");
            Check(edited.WithPlayTimeSeconds(save.ReadPlayTimeSeconds()).Serialize().AsSpan().SequenceEqual(original),
                "Restoring time changed unrelated bytes.");
            var time = save.Sections.Single(section => section.Name == "TIME");
            var allowed = Enumerable.Range(time.PayloadOffset + 32, 4).Concat(Enumerable.Range(40, 4))
                .Concat(Enumerable.Range(60, 4)).Concat(Enumerable.Range(bytes.Length - 4, 4)).ToHashSet();
            for (int index = 0; index < bytes.Length; index++)
                Check(bytes[index] == original[index] || allowed.Contains(index), "Time editing changed an unrelated field.");
        }
        foreach (float invalid in new[] { -1f, EngageSave.MaxPlayTimeSeconds + 1, float.NaN, float.PositiveInfinity })
            Reject(() => save.WithPlayTimeSeconds(invalid));
        Reject(() => EngageSave.Parse(MainTests.Fixture()).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(version: 1)).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(seconds: float.NaN)).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(seconds: -1)).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(seconds: 3_600_000)).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(duplicate: true)).ReadPlayTimeSeconds());
        Reject(() => EngageSave.Parse(Fixture(length: 44)).ReadPlayTimeSeconds());

        string source = Path.Combine(temporary, "play-time-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditPlayTime, "The time editor is unavailable.");
        var hours = window.FindControl<NumericUpDown>("PlayTimeHoursInput")!;
        var minutes = window.FindControl<NumericUpDown>("PlayTimeMinutesInput")!;
        var secondsInput = window.FindControl<NumericUpDown>("PlayTimeSecondsInput")!;
        Check(hours.Value == 175 && minutes.Value == 43 && secondsInput.Value == 48, "The GUI time decomposition is incorrect.");
        Check(window.ApplyMainValues() && window.Save!.Serialize().AsSpan().SequenceEqual(original), "Unchanged GUI time lost fractional seconds.");
        hours.Value = 268; minutes.Value = 37; secondsInput.Value = 24;
        window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
        window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
        Check(hours.Value == 268 && minutes.Value == 37 && secondsInput.Value == 24, "Language switching lost pending time.");
        Check(window.SaveCopy(Path.Combine(temporary, "play-time-copy")) && window.Save!.ReadPlayTimeSeconds() == 967044,
            "File saving did not apply the requested play time.");
        byte[] before = window.Save!.Serialize();
        foreach (var input in new[] { hours, minutes, secondsInput })
        {
            string valid = input.Text!;
            foreach (string invalid in new[] { "", "abc", "-1", "1.5", (input.Maximum + 1).ToString() })
            {
                input.Text = invalid;
                Check(!window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                    "Invalid time input partially changed the save.");
            }
            input.Text = valid;
        }
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "The source save was overwritten.");
        Console.WriteLine("Play time: core limits, byte preservation, summary synchronization and GUI saving passed.");
    }

    public static void CheckReal(EngageSave save)
    {
        float seconds = save.ReadPlayTimeSeconds();
        var edited = save.WithPlayTimeSeconds(seconds == 967044 ? 864001 : 967044);
        Check(edited.WithPlayTimeSeconds(seconds).Serialize().AsSpan().SequenceEqual(save.Serialize()),
            "Real play-time restoration changed unrelated bytes.");
        Check(edited.ReadAchievements().SequenceEqual(save.ReadAchievements()), "Time editing changed achievement flags.");
        Console.WriteLine("Real play time: exact restoration and unchanged achievements passed.");
    }

    public static byte[] Fixture(float seconds = 632628.25f, uint version = 0, bool duplicate = false, int length = 40)
    {
        byte[] original = MainTests.Fixture();
        var save = EngageSave.Parse(original);
        var sections = save.Sections.Select(section => original.AsSpan(section.Offset, section.Length + 8).ToArray()).ToList();
        byte[] time = new byte[length + 8];
        "EMIT"u8.CopyTo(time);
        BinaryPrimitives.WriteUInt32LittleEndian(time.AsSpan(4), (uint)(length + 4));
        BinaryPrimitives.WriteUInt32LittleEndian(time.AsSpan(8), version);
        time.AsSpan(12, 28).Fill(0xcd);
        BinaryPrimitives.WriteSingleLittleEndian(time.AsSpan(40), seconds);
        BinaryPrimitives.WriteSingleLittleEndian(time.AsSpan(44), 123.5f);
        sections.Add(time);
        if (duplicate) sections.Add(time);
        byte[] bytes = new byte[260 + sections.Sum(section => section.Length) + 8];
        original.AsSpan(0, 132).CopyTo(bytes);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(40), seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), Checksum(bytes.AsSpan(0, 60)));
        int offset = 260;
        for (int index = 0; index < sections.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(132 + index * 4), offset);
            sections[index].CopyTo(bytes, offset);
            offset += sections[index].Length;
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(132 + sections.Count * 4), offset);
        "LVRC"u8.CopyTo(bytes.AsSpan(offset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), Checksum(bytes.AsSpan(0, offset + 4)));
        return bytes;
    }

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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Unsupported play time was accepted.");
    }
}
