using System.Text.Json;
using System.Text.Json.Serialization;

namespace STranslate.Core;

internal sealed class LayoutAnalysisModeJsonConverter : JsonConverter<LayoutAnalysisMode>
{
    public override LayoutAnalysisMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => ReadFromString(reader.GetString()),
            JsonTokenType.Number => ReadFromNumber(ref reader),
            _ => LayoutAnalysisMode.Smart
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        LayoutAnalysisMode value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value == LayoutAnalysisMode.NoMerge ? "noMerge" : "smart");
    }

    private static LayoutAnalysisMode ReadFromString(string? value) =>
        string.Equals(value, "noMerge", StringComparison.OrdinalIgnoreCase)
            ? LayoutAnalysisMode.NoMerge
            : LayoutAnalysisMode.Smart;

    private static LayoutAnalysisMode ReadFromNumber(ref Utf8JsonReader reader)
    {
        if (!reader.TryGetInt32(out var value))
            return LayoutAnalysisMode.Smart;

        return value == 1 || value == 4
            ? LayoutAnalysisMode.NoMerge
            : LayoutAnalysisMode.Smart;
    }
}
