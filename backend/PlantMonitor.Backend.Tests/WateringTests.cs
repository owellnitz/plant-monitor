using Xunit;

namespace PlantMonitor.Backend.Tests;

/// <summary>
/// Mirrors frontend/src/app/moisture.spec.ts. The rule exists in both
/// languages; these cases are what keeps the two copies honest.
/// </summary>
public class WateringTests
{
    [Fact]
    public void Returns_null_when_no_limits_are_set()
    {
        Assert.Null(Watering.Evaluate(10, null, null));
        Assert.Null(Watering.Evaluate(90, null, null));
    }

    [Fact]
    public void Flags_must_below_the_must_water_limit()
    {
        Assert.Equal(WaterStatus.Must, Watering.Evaluate(15, 20, 40));
    }

    [Fact]
    public void Flags_can_between_the_limits()
    {
        Assert.Equal(WaterStatus.Can, Watering.Evaluate(30, 20, 40));
    }

    [Fact]
    public void Is_ok_at_or_above_the_can_water_limit()
    {
        Assert.Equal(WaterStatus.Ok, Watering.Evaluate(40, 20, 40));
        Assert.Equal(WaterStatus.Ok, Watering.Evaluate(80, 20, 40));
    }

    [Fact]
    public void Works_with_only_one_limit_set()
    {
        Assert.Equal(WaterStatus.Must, Watering.Evaluate(10, 20, null));
        Assert.Equal(WaterStatus.Ok, Watering.Evaluate(50, 20, null));
        Assert.Equal(WaterStatus.Can, Watering.Evaluate(30, null, 40));
        Assert.Equal(WaterStatus.Ok, Watering.Evaluate(50, null, 40));
    }

    [Fact]
    public void Orders_the_states_by_urgency()
    {
        Assert.True(WaterStatus.Must > WaterStatus.Can);
        Assert.True(WaterStatus.Can > WaterStatus.Ok);
    }
}
