using Go2HDR.Services;
using System.Runtime.InteropServices;
using Xunit;

namespace Go2HDR.Tests;

public class DisplayConfigServiceTests
{
    [Fact]
    public void AdvancedColorInfo2_HasExpectedNativeSize()
    {
        Assert.Equal(36, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>());
        Assert.Equal(80, Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>());
        Assert.Equal(72, Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>());
        Assert.Equal(24, Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>());
        Assert.Equal(28, Marshal.SizeOf<DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>());
    }

    [Fact]
    public void HdrMode_IsActive()
    {
        BuiltInHdrState state = DisplayConfigService.ClassifyAdvancedColorInfo2(1u << 4, 2);

        Assert.Equal(BuiltInHdrState.Active, state);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SdrAndAcmModes_AreNotHdr(int activeColorMode)
    {
        BuiltInHdrState state = DisplayConfigService.ClassifyAdvancedColorInfo2(1u << 4, activeColorMode);

        Assert.Equal(BuiltInHdrState.Inactive, state);
    }

    [Fact]
    public void AcmActiveFlag_DoesNotMakeWcgModeHdr()
    {
        const uint advancedColorActiveAndHdrSupported = (1u << 1) | (1u << 4);

        Assert.Equal(BuiltInHdrState.Inactive,
            DisplayConfigService.ClassifyAdvancedColorInfo2(advancedColorActiveAndHdrSupported, 1));
    }

    [Fact]
    public void NonHdrTarget_IsUnavailable()
    {
        BuiltInHdrState state = DisplayConfigService.ClassifyAdvancedColorInfo2(0, 0);

        Assert.Equal(BuiltInHdrState.Unavailable, state);
    }

    [Theory]
    [InlineData(-1, 1000)]
    [InlineData(0, 1000)]
    [InlineData(50, 3500)]
    [InlineData(100, 6000)]
    [InlineData(101, 6000)]
    public void SdrValueToRaw_ClampsAndConverts(int sdrValue, uint expectedRaw)
    {
        Assert.Equal(expectedRaw, DisplayConfigService.SdrValueToRaw(sdrValue));
    }
}
