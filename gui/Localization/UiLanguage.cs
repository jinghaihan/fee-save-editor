using System.Text.Json;
using Avalonia;
using FeeEditor.Core;

namespace FeeEditor.Gui.Localization;

public static class UiLanguage
{
    public static string Current { get; private set; } = "en";
    private static IReadOnlyDictionary<string, string> _strings = Read("en");

    public static IReadOnlyDictionary<string, string> Read(string language)
    {
        if (!LanguageCatalog.Codes.Contains(language))
            throw new ArgumentException("Unsupported UI language.", nameof(language));
        using var stream = typeof(UiLanguage).Assembly.GetManifestResourceStream(
            $"FeeEditor.Gui.Localization.{language}.json")
            ?? throw new InvalidOperationException($"Missing language resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Invalid language resource: {language}");
    }

    public static void Apply(string language)
    {
        var strings = Read(language);
        if (!strings.Keys.Order().SequenceEqual(Read("en").Keys.Order()))
            throw new InvalidOperationException("Language resource keys do not match English.");
        var application = Application.Current ?? throw new InvalidOperationException("The app is not initialized.");
        foreach (var (key, value) in strings)
            application.Resources[key] = value;
        _strings = strings;
        Current = language;
    }

    public static string Get(string key) => _strings[key];
}
