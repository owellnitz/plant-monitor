using System.ComponentModel.DataAnnotations;

namespace PlantMonitor.Backend.Dtos;

/// <summary>A reading as stored in Postgres, served to the frontend.</summary>
public sealed record StoredReading(Guid Id, string DeviceId, int Raw, int Percent, DateTimeOffset ReceivedAt);

/// <summary>A sensor with its most recent reading, for the sensor pages.</summary>
public sealed record Sensor(string DeviceId, int Raw, int Percent, DateTimeOffset ReceivedAt);

/// <summary>A plant with its species name and latest reading (null when no sensor/readings).</summary>
public sealed record PlantDto(Guid Id, string Name, string? Species, string? Location,
    string? SunExposure, string? DeviceId, int? MustWaterPercent, int? CanWaterPercent,
    int? Percent, int? Raw, DateTimeOffset? ReceivedAt);

/// <summary>Request body for creating/updating a plant. SpeciesName is upserted by name.</summary>
public sealed record PlantInput(string Name, string? SpeciesName, string? Location,
    string? SunExposure, string? DeviceId,
    [Range(0, 100)] int? MustWaterPercent = null,
    [Range(0, 100)] int? CanWaterPercent = null);

public sealed record SpeciesDto(Guid Id, string Name);

/// <summary>The running backend's release version, e.g. "1.0.0".</summary>
public sealed record VersionDto(string Version);
