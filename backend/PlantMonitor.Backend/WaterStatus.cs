namespace PlantMonitor.Backend;

/// <summary>
/// Traffic-light watering state derived from a plant's optional limits.
/// Ordered by urgency so a worsening transition is a plain comparison.
/// </summary>
public enum WaterStatus
{
    Ok = 0,
    Can = 1,
    Must = 2,
}

public static class Watering
{
    /// <summary>
    /// Maps a reading to a plant's traffic-light state. Higher moisture = wetter,
    /// so a valid pair has mustWater &lt;= canWater. Either limit may be null (unset);
    /// when both are null the plant has no limits and this returns null (neutral).
    /// Mirrors waterStatus() in frontend/src/app/moisture.ts — keep both in step.
    /// </summary>
    public static WaterStatus? Evaluate(int percent, int? mustWater, int? canWater)
    {
        if (mustWater is { } must && percent < must) return WaterStatus.Must;
        if (canWater is { } can && percent < can) return WaterStatus.Can;
        if (mustWater is not null || canWater is not null) return WaterStatus.Ok;
        return null;
    }
}
