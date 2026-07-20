using Xunit;

namespace PlantMonitor.Backend.Tests;

public class ReadingTests
{
    [Fact]
    public void Parses_firmware_payload()
    {
        var reading = Reading.TryParse("""{"id":"plant-1","raw":3500,"percent":62}""");

        Assert.NotNull(reading);
        Assert.Equal("plant-1", reading.Id);
        Assert.Equal(3500, reading.Raw);
        Assert.Equal(62, reading.Percent);
    }

    [Fact]
    public void Parses_the_reset_reason()
    {
        var reading = Reading.TryParse("""{"id":"plant-1","raw":3500,"percent":62,"reset":"rwdt"}""");

        Assert.NotNull(reading);
        Assert.Equal("rwdt", reading.Reset);
    }

    [Fact]
    public void Reset_is_null_for_firmware_predating_the_field()
    {
        var reading = Reading.TryParse("""{"id":"plant-1","raw":3500,"percent":62}""");

        Assert.NotNull(reading);
        Assert.Null(reading.Reset);
    }

    [Fact]
    public void Accepts_differently_cased_keys()
    {
        var reading = Reading.TryParse("""{"Id":"plant-1","Raw":1,"Percent":2}""");

        Assert.NotNull(reading);
        Assert.Equal("plant-1", reading.Id);
    }

    [Theory]
    [InlineData("sensors/plant-1/moisture", "plant-1")]
    [InlineData("sensors/a1b2c3d4e5f6/moisture", "a1b2c3d4e5f6")]
    [InlineData("sensors//moisture", null)]
    [InlineData("sensors/plant-1/temperature", null)]
    [InlineData("other/plant-1/moisture", null)]
    [InlineData("sensors/plant-1", null)]
    [InlineData("sensors/plant-1/extra/moisture", null)]
    public void Extracts_device_id_from_topic(string topic, string? expected)
    {
        Assert.Equal(expected, Reading.DeviceIdFromTopic(topic));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"id":"","raw":1,"percent":2}""")]
    [InlineData("""{"raw":1,"percent":2}""")]
    [InlineData("")]
    public void Rejects_malformed_payloads(string json)
    {
        Assert.Null(Reading.TryParse(json));
    }
}
