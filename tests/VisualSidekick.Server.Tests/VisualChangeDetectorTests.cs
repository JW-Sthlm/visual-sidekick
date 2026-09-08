using VisualSidekick.Server;
using Xunit;

namespace VisualSidekick.Server.Tests;

public sealed class VisualChangeDetectorTests
{
    [Fact]
    public void IdenticalSignaturesAreNotMeaningful()
    {
        var signature = new byte[] { 10, 20, 30, 40, 50, 60 };

        var distance = VisualChangeDetector.ComputeDistance(signature, signature);

        Assert.Equal(0, distance);
        Assert.False(VisualChangeDetector.IsMeaningful(distance));
    }

    [Fact]
    public void LargePixelChangesAreMeaningful()
    {
        var first = new byte[] { 0, 0, 0, 0, 0, 0 };
        var second = new byte[] { 255, 255, 255, 255, 255, 255 };

        var distance = VisualChangeDetector.ComputeDistance(first, second);

        Assert.True(distance >= VisualChangeDetector.MeaningfulChangeThreshold);
        Assert.True(VisualChangeDetector.IsMeaningful(distance));
    }

    [Fact]
    public void InvalidSignatureLengthsForceAChange()
    {
        Assert.Equal(255, VisualChangeDetector.ComputeDistance([1, 2, 3], [1, 2]));
    }
}
