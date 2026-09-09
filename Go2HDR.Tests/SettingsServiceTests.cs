using Go2HDR.Models;
using Go2HDR.Services;
using Xunit;

namespace Go2HDR.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Normalize_RepairsInvalidScalarValuesAndCurve()
    {
        var settings = new AppSettings
        {
            MinimumBrightness = 100,
            Theme = "Unknown",
            CurvePoints =
            [
                new(double.NaN, 50),
                new(79.6, -5),
                new(80.4, 120),
                new(80.2, 40),
                new(95, 60)
            ]
        };

        SettingsService.Normalize(settings);

        Assert.Equal(AppSettings.MaximumMinimumBrightness, settings.MinimumBrightness);
        Assert.Equal("System", settings.Theme);
        Assert.Equal([80d, 95d, 100d], settings.CurvePoints.Select(point => point.Brightness));
        Assert.Equal([40d, 60d, 60d], settings.CurvePoints.Select(point => point.SdrValue));
    }

    [Fact]
    public void Normalize_UsesDefaultsWhenCurveHasNoValidPoints()
    {
        var settings = new AppSettings
        {
            MinimumBrightness = 42,
            CurvePoints = [new(double.PositiveInfinity, double.NaN)]
        };

        SettingsService.Normalize(settings);

        Assert.NotEmpty(settings.CurvePoints);
        Assert.Equal(42, settings.CurvePoints[0].Brightness);
        Assert.Equal(100, settings.CurvePoints[^1].Brightness);
    }
}
