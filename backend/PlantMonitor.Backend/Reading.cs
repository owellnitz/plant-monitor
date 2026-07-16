using System.Text.Json;

namespace PlantMonitor.Backend;

/// <summary>
/// A moisture reading as published by the firmware:
/// {"id":"plant-1","raw":3500,"percent":62,"reset":"CoreDeepSleep"}
/// Extra fields like "reset" (diagnostic, firmware-side only) are ignored
/// by the deserializer.
/// </summary>
public sealed record Reading(string Id, int Raw, int Percent)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Returns null for malformed JSON or a missing/empty id.</summary>
    public static Reading? TryParse(string json)
    {
        try
        {
            var reading = JsonSerializer.Deserialize<Reading>(json, JsonOptions);
            return string.IsNullOrEmpty(reading?.Id) ? null : reading;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
