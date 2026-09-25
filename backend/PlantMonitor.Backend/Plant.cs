namespace PlantMonitor.Backend;

/// <summary>A kind of plant; the name is unique and grows from user input.</summary>
public class Species
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// A plant the user owns. At most one sensor is bound to it via
/// <see cref="DeviceId"/> (unique → one sensor per plant).
/// </summary>
public class Plant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? SpeciesId { get; set; }
    public Species? Species { get; set; }
    public string? Location { get; set; }
    public string? SunExposure { get; set; }
    public string? DeviceId { get; set; }

    /// <summary>Moisture % below which watering is urgent (red); null = no limit.</summary>
    public int? MustWaterPercent { get; set; }

    /// <summary>Moisture % below which watering is OK but not urgent (amber); null = no limit.</summary>
    public int? CanWaterPercent { get; set; }

    /// <summary>
    /// The state this plant was last seen in, so a push is sent once per
    /// worsening transition rather than on every hourly reading. Null until the
    /// first reading is evaluated — which is what keeps already-dry plants
    /// silent when notifications are switched on.
    /// </summary>
    public WaterStatus? NotifiedStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
