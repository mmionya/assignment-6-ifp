using Astra.App;
using Xunit;

namespace Astra.Tests;

public sealed class NativeIntegrationTests
{
    [Fact]
    public void RealNativeCallMatchesHandCalculatedBoundaryExamples()
    {
        var native = new NativeGroupEligibility();
        Assert.True(native.IsEligible(4, 4).Value);
        Assert.False(native.IsEligible(3, 4).Value);
        Assert.True(native.IsEligible(6, 6).Value);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(7, 4)]
    [InlineData(4, 0)]
    [InlineData(4, 7)]
    public void InvalidInputsAreRejectedAtManagedBoundary(int passengers, int minimum)
    {
        var result = new NativeGroupEligibility().IsEligible(passengers, minimum);
        Assert.False(result.IsSuccess);
        Assert.Contains("between 1 and 6", result.Error);
    }
}
