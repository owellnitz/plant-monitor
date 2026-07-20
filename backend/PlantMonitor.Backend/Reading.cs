using System.Text.Json;

namespace PlantMonitor.Backend;

/// <summary>
/// A moisture reading as published by the firmware:
/// {"id":"plant-1","raw":3500,"percent":62,"reset":"deep_sleep"}
/// </summary>
/// <param name="Reset">
/// Why the device booted before sending this reading: "deep_sleep" for the
/// normal hourly wake, anything else means the previous cycle died ("panic",
/// "rwdt", "brownout", ...). Null for firmware predating the field.
/// </param>
public sealed record Reading(string Id, int Raw, int Percent, string? Reset = null)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>The device id segment of a sensors/{id}/moisture topic; null if malformed.</summary>
    public static string? DeviceIdFromTopic(string topic) =>
        topic.Split('/') is ["sensors", { Length: > 0 } id, "moisture"] ? id : null;

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
