using System.Text.Json;

namespace PlantMonitor.Backend;

/// <summary>
/// A moisture reading as published by the firmware:
/// {"id":"plant-1","raw":3500,"percent":62}
/// </summary>
public sealed record Reading(string Id, int Raw, int Percent)
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
