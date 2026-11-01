using System.Collections.Immutable;
using System.Globalization;
using Astra.App;
using Astra.Core;
using Xunit;

namespace Astra.Tests;

public class AnalyticsTests
{
    private static readonly DateOnly AsOf = new(2030, 6, 15);

    private static string SamplePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "samples", name + ".json");

    private static ValidatedBookingBatch Batch(string name) =>
        Input.Parse(File.ReadAllText(SamplePath(name))).Bind(Input.Validate).Value;

    private static RawBooking Raw(string id, Destination destination, int passengers = 1, bool cancelled = false) =>
        new(id, destination, passengers, TravelClass.Explorer,
            BookingPipeline.GetMission(destination).Departure, cancelled);

    private static ValidatedBookingBatch BatchOf(params RawBooking[] rows) =>
        ValidatedBookingBatch.Create(rows).Value;

    private static BookingRequest Booking(string id, int passengers, bool cancelled = false, DateOnly? departure = null) =>
        new(id, Destination.Moon, passengers, TravelClass.Explorer, departure ?? AsOf, cancelled);

    [Fact]
    public void QuoteListKeepsTheExistingPipelineOrderAndTotals()
    {
        var report = BookingReports.QuoteList(Batch("bookings"), AsOf, BookingPipeline.Standard);

        Assert.Equal(new[] { "A", "B", "F", "C" }, report.Quotes.Select(q => q.Booking.Id));
        Assert.Equal(new[] { 49_000m, 264_600m, 7_200_000m, 25_920_000m }, report.Quotes.Select(q => q.TotalUsd));
        Assert.Equal(33_433_600m, report.TotalUsd);
    }

    [Fact]
    public void DemandReportGroupsActiveUpcomingBookingsByDestinationAndSumsThem()
    {
        var report = BookingReports.DemandByDestination(Batch("bookings"), AsOf, BookingPipeline.Standard);

        Assert.Equal(
            new[]
            {
                new DestinationLine(Destination.Mars, 2, 9, 33_120_000m),
                new DestinationLine(Destination.Europa, 1, 1, 8_700_000m),
                new DestinationLine(Destination.Moon, 2, 5, 313_600m)
            },
            report.Lines);
        Assert.Equal(42_133_600m, report.TotalUsd);
    }

    [Fact]
    public void DemandReportBreaksRevenueTiesByDestinationNameWhateverTheInputOrder()
    {
        Func<BookingRequest, decimal> flatPrice = _ => 100m;
        var forward = BatchOf(Raw("1", Destination.Moon), Raw("2", Destination.Mars), Raw("3", Destination.Europa));
        var reversed = BatchOf(Raw("3", Destination.Europa), Raw("2", Destination.Mars), Raw("1", Destination.Moon));
        var expected = new[] { Destination.Europa, Destination.Mars, Destination.Moon };

        var first = BookingReports.DemandByDestination(forward, AsOf, flatPrice);
        var second = BookingReports.DemandByDestination(reversed, AsOf, flatPrice);

        Assert.Equal(expected, first.Lines.Select(l => l.Destination));
        Assert.Equal(expected, second.Lines.Select(l => l.Destination));
        Assert.Equal(300m, first.TotalUsd);
    }

    [Fact]
    public void ReportsHandleEmptyInputNoMatchesAndOnlyCancelledBookings()
    {
        var empty = BookingReports.Build(Batch("empty"), AsOf, BookingPipeline.Standard);
        var late = BookingReports.Build(Batch("bookings"), new DateOnly(2032, 1, 1), BookingPipeline.Standard);
        var cancelledOnly = BookingReports.Build( BatchOf(Raw("X", Destination.Moon, cancelled: true)), AsOf, BookingPipeline.Standard);

        foreach (var report in new[] { empty, late, cancelledOnly })
        {
            Assert.Empty(report.Quotes.Quotes);
            Assert.Equal(0m, report.Quotes.TotalUsd);
            Assert.Empty(report.Demand.Lines);
            Assert.Equal(0m, report.Demand.TotalUsd);
        }
    }

    [Fact]
    public void ReportsFromInvalidDataAreExplicitFailuresNotPartialReports()
    {
        var invalid = Input.Parse(File.ReadAllText(SamplePath("invalid")))
        .Bind(Input.Validate)
        .Map(batch => BookingReports.Build(batch, AsOf, BookingPipeline.Standard));

        Assert.False(invalid.IsSuccess);
        Assert.Contains("Record 2", invalid.Error);
    }

    [Fact]
    public void BuildCallsThePriceFunctionOncePerBookingOfEachReport()
    {
        var calls = 0;
        Func<BookingRequest, decimal> probe = b => { calls++; return BookingPipeline.Standard(b); };
        BookingReports.Build(Batch("bookings"), AsOf, probe);
        Assert.Equal(4 + 5, calls);
    }
    [Fact]
    public void UpcomingKeepsActiveBookingsOnOrAfterTheReviewDateAndLeavesTheSourceAlone()
    {
        var bookings = Batch("bookings").Bookings;
        var before = bookings.ToArray();

        Assert.Equal(new[] { "A", "B", "C", "D", "F" }, bookings.Upcoming(AsOf).Select(b => b.Id));
        Assert.Equal(new[] { "D" }, bookings.Upcoming(new DateOnly(2030, 9, 11)).Select(b => b.Id));
        Assert.Equal(new[] { "C", "D", "F" }, bookings.Upcoming(new DateOnly(2030, 9, 10)).Select(b => b.Id));
        Assert.Empty(bookings.Upcoming(new DateOnly(2031, 3, 21)));
        Assert.Equal<BookingRequest>(before, bookings);
    }

    [Fact]
    public void UpcomingIsADeferredQueryOverTheCurrentSource()
    {
        var source = new List<BookingRequest>();
        var query = source.Upcoming(AsOf);

        source.Add(Booking("late", 1));
        source.Add(Booking("cancelled", 1, cancelled: true));
        source.Add(Booking("past", 1, departure: AsOf.AddDays(-1)));
        Assert.Equal("late", Assert.Single(query).Id);
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<BookingRequest>)null!).Upcoming(AsOf));
    }

    [Fact]
    public void WithContextPrefixesFailuresKeepsTheMessageAndLeavesSuccessUntouched()
    {
        var success = Result<int>.Success(7);
        var failure = Result<int>.Failure("Passengers must be between 1 and 6");

        Assert.Same(success, success.WithContext("Input 'a.json'"));
        var withContext = failure.WithContext("Input 'a.json'");
        Assert.False(withContext.IsSuccess);
        Assert.Equal("Input 'a.json': Passengers must be between 1 and 6", withContext.Error);
        Assert.Throws<ArgumentException>(() => failure.WithContext(" "));
    }
    [Fact]
    public void TwoPartiallyAppliedPoliciesGiveDifferentResultsOnTheSameBooking()
    {
        var bookings = Batch("bookings").Bookings;
        var b = bookings.Single(x => x.Id == "B");
        var f = bookings.Single(x => x.Id == "F");
        var a = bookings.Single(x => x.Id == "A");

        Assert.Equal(238_140m, Pricing.Group10(b));
        Assert.Equal(251_370m, Pricing.Family5(b));
        Assert.Equal(7_200_000m, Pricing.Group10(f));
        Assert.Equal(6_840_000m, Pricing.Family5(f));
        Assert.Equal(Pricing.Group10(a), Pricing.Family5(a));
        Assert.NotEqual(Pricing.Group10(f), Pricing.Family5(f));
    }

    [Fact]
    public void ConfiguredPoliciesAreUsedByTheSameWorkflowWithoutChangingAGlobalSetting()
    {
        var batch = Batch("bookings");

        var group = BookingReports.QuoteList(batch, AsOf, Pricing.Group10);
        var family = BookingReports.QuoteList(batch, AsOf, Pricing.Family5);
        var groupAgain = BookingReports.QuoteList(batch, AsOf, Pricing.Group10);

        Assert.Equal(30_815_140m, group.TotalUsd);
        Assert.Equal(31_764_370m, family.TotalUsd);
        Assert.Equal(group.TotalUsd, groupAgain.TotalUsd);
    }

    [Fact]
    public void CurriedPolicyWithGroupConfigurationMatchesTheOriginalGroupDiscount()
    {
        var config = PricingConfig.Create(4, 10m).Value;
        var policy = Pricing.CreatePolicy(config);

        foreach (var booking in Batch("bookings").Bookings)
            Assert.Equal(BookingPipeline.GroupDiscount(booking), policy(booking));
        Assert.Throws<ArgumentNullException>(() => Pricing.CreatePolicy(null!));
    }

    [Fact]
    public void InvalidPricingConfigurationIsAUsefulFailureAndEdgesAreAccepted()
    {
        Assert.Contains("Group minimum", PricingConfig.Create(0, 10m).Error);
        Assert.Contains("Group minimum", PricingConfig.Create(7, 10m).Error);
        Assert.Contains("Discount percent", PricingConfig.Create(4, -1m).Error);
        Assert.Contains("Discount percent", PricingConfig.Create(4, 100.1m).Error);
        Assert.True(PricingConfig.Create(1, 0m).IsSuccess);
        Assert.True(PricingConfig.Create(6, 100m).IsSuccess);
    }

    [Fact]
    public void PricingRunsOnlyDuringIterationAndRepeatsUntilTheResultIsMaterialised()
    {
        var bookings = Batch("bookings").Bookings;
        var calls = 0;
        Func<BookingRequest, decimal> probe = b => { calls++; return 10m; };

        var quotes = BookingQuotes.PriceLazily(bookings, probe);
        Assert.Equal(0, calls);

        using (var enumerator = quotes.GetEnumerator())
        {
            Assert.Equal(0, calls);
            Assert.True(enumerator.MoveNext());
            Assert.Equal(1, calls);
            Assert.Equal("A", enumerator.Current.Booking.Id);
        }

        Assert.Equal(1, calls);
        Assert.Equal(6, quotes.Count());
        Assert.Equal(1 + 6, calls);
        Assert.Equal(6, quotes.Count());
        Assert.Equal(1 + 6 + 6, calls);

        var snapshot = quotes.ToImmutableArray();
        var afterSnapshot = calls;
        Assert.Equal(1 + 6 + 6 + 6, afterSnapshot);
        Assert.Equal(6, snapshot.Length);
        Assert.Equal(6, snapshot.Count());
        Assert.Equal(60m, snapshot.Sum(q => q.TotalUsd));
        Assert.Equal(afterSnapshot, calls);
    }

    [Fact]
    public void LazyPricingRoundsToCentsLikeTheOriginalPipelineAndValidatesArgumentsImmediately()
    {
        var booking = Booking("R", 1);

        var quote = BookingQuotes.PriceLazily(new[] { booking }, _ => 10.005m).Single();

        Assert.Equal(10.01m, quote.TotalUsd);
        Assert.Throws<ArgumentNullException>(() => BookingQuotes.PriceLazily(null!, _ => 1m));
        Assert.Throws<ArgumentNullException>(() => BookingQuotes.PriceLazily(new[] { booking }, null!));
    }
    [Fact]
    public void ExportWritesTheLinesAndReleasesTheStreamAfterSuccess()
    {
        using var dir = new TempDir();
        var path = dir.PathFor("report.csv");
        File.WriteAllText(path, "old content");
        TrackingFileStream? tracked = null;
        var result = ReportFiles.Export(path, new[] { "one", "two" }, p => tracked = new TrackingFileStream(p));
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(tracked!.Disposed);
        Assert.Equal(new[] { "one", "two" }, File.ReadAllLines(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteFailureReleasesTheStreamKeepsTheOldFileAndReportsThePath()
    {
        using var dir = new TempDir();
        var path = dir.PathFor("report.csv");
        File.WriteAllText(path, "old content");
        TrackingFileStream? tracked = null;
        var result = ReportFiles.Export(path, LinesThenFail(new IOException("disk full")), p => tracked = new TrackingFileStream(p));
        Assert.False(result.IsSuccess);
        Assert.Contains(path, result.Error);
        Assert.Contains("disk full", result.Error);
        Assert.True(tracked!.Disposed);
        Assert.Equal("old content", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void UnexpectedErrorsStillReleaseTheStreamAndRemoveTheTemporaryFile()
    {
        using var dir = new TempDir();
        var path = dir.PathFor("report.csv");
        TrackingFileStream? tracked = null;
        Assert.Throws<InvalidOperationException>(() => ReportFiles.Export( path, LinesThenFail(new InvalidOperationException("bug")), p => tracked = new TrackingFileStream(p)));
        Assert.True(tracked!.Disposed);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ExportToAMissingDirectoryFailsWithThePathAndLeavesNothingBehind()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "no-such-folder", "report.csv");
        var result = ReportFiles.Export(path, new[] { "x" });
        Assert.False(result.IsSuccess);
        Assert.Contains(path, result.Error);
        Assert.Contains("Cannot export", result.Error);
        Assert.False(ReportFiles.Export("  ", new[] { "x" }).IsSuccess);
    }

    [Fact]
    public void ReadingWithAConvenienceApiLeavesNoOpenHandleAfterSuccessOrFailure()
    {
        using var dir = new TempDir();
        var good = dir.Write("good.json", "[]");
        var bad = dir.Write("bad.json", "[{");
        Assert.True(BookingFiles.Read(good).IsSuccess);
        Assert.False(BookingFiles.LoadQuotes(bad, AsOf, BookingPipeline.Standard).IsSuccess);
        foreach (var path in new[] { good, bad })
        {
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusive.CanWrite);
        }
    }
    [Fact]
    public void CsvLinesQuoteSpecialCharactersAndUseInvariantNumbers()
    {
        var batch = BatchOf(new RawBooking("He said \"hi\", ok", Destination.Moon, 1, TravelClass.Explorer, BookingPipeline.GetMission(Destination.Moon).Departure, false));
        var reports = BookingReports.Build(batch, AsOf, _ => 1234.5m);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var lines = ReportFormat.ToCsvLines(reports).ToArray();
            Assert.Equal("\"He said \"\"hi\"\", ok\",Moon,1,Explorer,1234.50", lines[2]);
            Assert.Equal("Total,,,,1234.50", lines[3]);
            Assert.Equal("Moon,1,1,1234.50", lines[7]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
    [Fact]
    public void NormalWorkflowRunsBothReportsAndExportsTheCsv()
    {
        using var dir = new TempDir();
        var export = dir.PathFor("report.csv");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run( new[] { "report", SamplePath("bookings"), "2030-06-15", "group10", export }, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal("", error.ToString());
        Assert.Contains("Report 1: Quote list", output.ToString());
        Assert.Contains("Accepted: 4; Total: 30815140.00 USD", output.ToString());
        Assert.Contains("Report 2: Demand by destination", output.ToString());
        Assert.Contains("Mars: 2 booking(s), 9 passenger(s) => 30528000.00 USD", output.ToString());
        Assert.Contains("Exported: " + export, output.ToString());
        Assert.Equal(
            new[]
            {
                "Report,Quote list",
                "Id,Destination,Passengers,TravelClass,TotalUsd",
                "A,Moon,1,Explorer,49000.00",
                "B,Moon,4,Premium,238140.00",
                "F,Mars,3,Explorer,7200000.00",
                "C,Mars,6,First,23328000.00",
                "Total,,,,30815140.00",
                "",
                "Report,Demand by destination",
                "Destination,Bookings,Passengers,RevenueUsd",
                "Mars,2,9,30528000.00",
                "Europa,1,1,8700000.00",
                "Moon,2,5,287140.00",
                "Total,,,39515140.00"
            },
            File.ReadAllLines(export));
    }

    [Fact]
    public void DifferentConfiguredPoliciesGiveDifferentWorkflowOutputsFromTheSameFile()
    {
        string Run(string policy)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CommandLine.Run(
                new[] { "report", SamplePath("bookings"), "2030-06-15", policy }, output, error));
            return output.ToString();
        }

        Assert.Contains("Total: 33433600.00 USD", Run("standard"));
        Assert.Contains("Accepted: 4; Total: 30815140.00 USD", Run("group10"));
        Assert.Contains("Accepted: 4; Total: 31764370.00 USD", Run("family5"));
    }

    [Fact]
    public void FailurePathsPrintOnlyAnErrorAndLeaveNoExportFile()
    {
        using var dir = new TempDir();
        var missing = dir.PathFor("missing.json");
        var malformed = dir.Write("malformed.json", "[{ \"Id\": ");
        var invalid = dir.Write("invalid.json", File.ReadAllText(SamplePath("invalid")));
        var export = dir.PathFor("report.csv");

        foreach (var (input, expected) in new[]
        {
            (missing, "Cannot read input"),
            (malformed, "Input '" + malformed + "': Invalid JSON"),
            (invalid, "Input '" + invalid + "': Record 2")
        })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = CommandLine.Run(new[] { "report", input, "2030-06-15", "standard", export }, output, error);

            Assert.Equal(1, exitCode);
            Assert.Equal("", output.ToString());
            Assert.Contains(expected, error.ToString());
            Assert.False(File.Exists(export));
            Assert.False(File.Exists(export + ".tmp"));
        }
    }

    [Fact]
    public void ExportFailureAndBadArgumentsAreReportedWithoutPartialOutputOrDamagedInput()
    {
        using var dir = new TempDir();
        var input = dir.Write("bookings.json", File.ReadAllText(SamplePath("bookings")));
        var badExport = Path.Combine(dir.Path, "no-such-folder", "report.csv");

        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            Assert.Equal(1, CommandLine.Run(new[] { "report", input, "2030-06-15", "family5", badExport }, output, error));
            Assert.Equal("", output.ToString());
            Assert.Contains("Cannot export", error.ToString());
        }

        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            var before = File.ReadAllText(input);
            Assert.Equal(1, CommandLine.Run(new[] { "report", input, "2030-06-15", "standard", input }, output, error));
            Assert.Contains("never overwritten", error.ToString());
            Assert.Equal(before, File.ReadAllText(input));
        }

        foreach (var args in new[]
        {
            new[] { "report", input, "2030-06-15", "unknown-policy" },
            new[] { "report", input, "15-06-2030", "standard" },
            new[] { "report", input }
        })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(2, CommandLine.Run(args, output, error));
            Assert.Contains("Usage", error.ToString());
        }
    }
    private static IEnumerable<string> LinesThenFail(Exception failure)
    {
        yield return "first line";
        throw failure;
    }

    private sealed class TrackingFileStream : FileStream
    {
        public bool Disposed { get; private set; }
        public TrackingFileStream(string path) : base(path, FileMode.Create, FileAccess.Write, FileShare.None) { }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "astra-assignment8-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string PathFor(string name) => System.IO.Path.Combine(Path, name);
        public string Write(string name, string content)
        {
            var file = PathFor(name);
            File.WriteAllText(file, content);
            return file;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
