namespace PlantMonitor.Backend;

// Ordered by urgency: callers compare with > to detect worsening.
public enum WaterStatus
{
    Ok = 0,
    Can = 1,
    Must = 2,
}

public static class Watering
{
    // Mirrors waterStatus() in frontend/src/app/moisture.ts; assumes mustWater <= canWater (PlantService.LimitsValid).
    public static WaterStatus? Evaluate(int percent, int? mustWater, int? canWater)
    {
        if (mustWater is { } must && percent < must) return WaterStatus.Must;
        if (canWater is { } can && percent < can) return WaterStatus.Can;
        if (mustWater is not null || canWater is not null) return WaterStatus.Ok;
        return null;
    }
}
