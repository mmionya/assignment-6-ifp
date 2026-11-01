using Astra.App;
using Astra.Core;
using Xunit;

namespace Astra.Tests;

public class NativeIntegrationTests
{
    private static string SamplePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "samples", name + ".json");

    private sealed class FailingEligibility : IGroupEligibility
    {
        public int Calls { get; private set; }
        public Result<bool> IsEligible(int passengers, int minimum)
        {
            Calls++;
            return Result<bool>.Failure("Simulated native load failure (DllNotFoundException).");
        }
    }

    [Fact]
    public void RealNativeCallMatchesHandCalculatedBoundaryExamples()
    {
        var native = new NativeGroupEligibility();
        // C checks >=: 4 >= 4 is true; 3 >= 4 is false.
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

    [Fact]
    public void RealNativeSummaryCountsAcceptedBookingsOnly()
    {
        var batch = Input.Parse(File.ReadAllText(SamplePath("bookings"))).Bind(Input.Validate).Value;
        var reports = BookingReports.Build(batch, new DateOnly(2030, 6, 15), Pricing.Group10);
        var result = NativeReportCalculation.CountEligible(reports.Quotes, new NativeGroupEligibility());
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(4, reports.Quotes.Quotes.Length);
        Assert.Equal(new NativeGroupSummary(4, 2), result.Value); // B (4) and C (6); F has 3.
    }

    [Fact]
    public void SimulatedNativeFailureIsExplicitAndDoesNotExportOrPrintPartialReports()
    {
        var failed = new FailingEligibility();
        var export = Path.Combine(Path.GetTempPath(), "astra-native-failure-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = ReportCommand.Run(new[] { SamplePath("bookings"), "2030-06-15", "group10", export },
                output, error, failed);
            Assert.Equal(1, code);
            Assert.True(failed.Calls > 0);
            Assert.Empty(output.ToString());
            Assert.Contains("Booking 'A'", error.ToString());
            Assert.Contains("Simulated native load failure", error.ToString());
            Assert.False(File.Exists(export));
            Assert.False(File.Exists(export + ".tmp"));
        }
        finally
        {
            if (File.Exists(export)) File.Delete(export);
            if (File.Exists(export + ".tmp")) File.Delete(export + ".tmp");
        }
    }

    [Fact]
    public void NormalCliReportIncludesActualNativeCalculation()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CommandLine.Run(new[] { "report", SamplePath("bookings"), "2030-06-15", "standard" }, output, error);
        Assert.Equal(0, code);
        Assert.Equal("", error.ToString());
        Assert.Contains("Report 1: Quote list", output.ToString());
        Assert.Contains("Report 2: Demand by destination", output.ToString());
        Assert.Contains("Native C: 2 accepted booking(s) have at least 4 passengers.", output.ToString());
    }

    [Fact]
    public void EmptySnapshotDoesNotNeedNativeCall()
    {
        var quotes = new QuoteSummary(System.Collections.Immutable.ImmutableArray<BookingQuote>.Empty, 0m);
        var failing = new FailingEligibility();
        var result = NativeReportCalculation.CountEligible(quotes, failing);
        Assert.Equal(0, result.Value.EligibleBookings);
        Assert.Equal(0, failing.Calls);
    }

    [Fact]
    public void NativeReportRejectsInvalidThresholdBeforeCallingAdapter()
    {
        var quotes = new QuoteSummary(System.Collections.Immutable.ImmutableArray<BookingQuote>.Empty, 0m);
        var failing = new FailingEligibility();
        var result = NativeReportCalculation.CountEligible(quotes, failing, 7);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, failing.Calls);
    }
}
