using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicEngramSimulator;

internal static class EngramReportJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
