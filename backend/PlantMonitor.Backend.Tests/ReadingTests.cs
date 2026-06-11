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
    public void Accepts_differently_cased_keys()
    {
        var reading = Reading.TryParse("""{"Id":"plant-1","Raw":1,"Percent":2}""");

        Assert.NotNull(reading);
        Assert.Equal("plant-1", reading.Id);
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
