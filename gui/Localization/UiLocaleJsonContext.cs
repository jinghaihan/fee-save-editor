using System.Text.Json.Serialization;

namespace FeeEditor.Gui.Localization;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class UiLocaleJsonContext : JsonSerializerContext { }
