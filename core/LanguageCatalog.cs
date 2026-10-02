namespace FeeEditor.Core;

public sealed record SupportedLanguage(string Code, string NativeName);

public static class LanguageCatalog
{
    public static IReadOnlyList<SupportedLanguage> Languages { get; } = Array.AsReadOnly<SupportedLanguage>([
        new("en", "English"), new("zh-Hans", "简体中文"), new("zh-Hant", "繁體中文"),
        new("ja", "日本語"), new("ko", "한국어"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano")
    ]);

    public static IEnumerable<string> Codes => Languages.Select(language => language.Code);
}
