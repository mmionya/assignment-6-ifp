using Astra.App;
using Astra.Core;
using Xunit;

namespace Astra.Tests;

public class ExceptionHandlingTests
{
    private static readonly DateOnly AsOf = new(2030, 6, 15);
    private const string MoonRecord = """
        { "Id": "A", "Destination": "moon", "Passengers": 1, "TravelClass": "explorer", "Departure": "2030-06-15", "IsCancelled": false }
        """;
    private const string GroupRecord = """
        { "Id": "B", "Destination": "moon", "Passengers": 4, "TravelClass": "premium", "Departure": "2030-06-15", "IsCancelled": false }
        """;

    private static string Json(params string[] records) => "[" + string.Join(",", records) + "]";

    private static RawBooking Moon(string id = "A") =>
        new(id, Destination.Moon, 1, TravelClass.Explorer, AsOf, false);

    private static void FailureContains<T>(Result<T> result, string expected)
    {
        Assert.False(result.IsSuccess);
        Assert.Contains(expected, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void FileWorkflowComposesReadParseValidateCalculateAndProducesCorrectQuotes()
    {
        using var files = new TestFiles();
        var path = files.Write("success.json", Json(GroupRecord, MoonRecord));

        var result = BookingFiles.LoadQuotes(path, AsOf, BookingPipeline.Standard);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { "A", "B" }, result.Value.Quotes.Select(quote => quote.Booking.Id));
        Assert.Equal(new[] { 49_000m, 264_600m }, result.Value.Quotes.Select(quote => quote.TotalUsd));
        Assert.Equal(313_600m, result.Value.TotalUsd);
        Assert.Equal(Json(GroupRecord, MoonRecord), File.ReadAllText(path));
    }

    [Fact]
    public void InvalidQuantityReportsTheRecordAndSkipsPricing()
    {
        var calls = 0;
        var json = Json(MoonRecord.Replace("\"Passengers\": 1", "\"Passengers\": 0"));

        var result = Input.Process(json, AsOf, _ => { calls++; return 10m; });

        FailureContains(result, "Record 1: Passengers must be between 1 and 6");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DuplicateNormalizedIdReportsTheSecondRecordAndSkipsPricing()
    {
        var calls = 0;
        var json = Json(MoonRecord, MoonRecord.Replace("\"A\"", "\" A \""));

        var result = Input.Process(json, AsOf, _ => { calls++; return 10m; });

        FailureContains(result, "Record 2: Id must be unique after trimming");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void WrongMissionDepartureReportsTheRuleAndSkipsPricing()
    {
        var calls = 0;
        var json = Json(MoonRecord.Replace("2030-06-15", "2030-06-16"));

        var result = Input.Process(json, AsOf, _ => { calls++; return 10m; });

        FailureContains(result, "Record 1: Departure must match the mission schedule");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void UnpreparedWorkflowRejectsCalculationWithoutInvokingPolicy()
    {
        var calls = 0;
        var pending = new QuoteState.Pending(new[] { Moon() }, AsOf, _ => { calls++; return 10m; });

        var result = QuoteWorkflow.Calculate(pending);

        FailureContains(result, "Missing prerequisite");
        Assert.Equal(0, calls);
        Assert.Equal("A", pending.Rows[0]?.Id);
    }

    [Fact]
    public void RealLookupHandlesPresentAndAbsentCasesAndRetainsMoonAndFalse()
    {
        var batch = ValidatedBookingBatch.Create(new[] { Moon() }).Value;

        var present = Assert.IsType<Option<BookingRequest>.Some>(BookingLookup.Find(batch, " A "));
        Assert.Equal("A", present.Value.Id);
        Assert.Equal(Destination.Moon, present.Value.Destination);
        Assert.Equal(0, (int)present.Value.Destination);
        Assert.False(present.Value.IsCancelled);
        Assert.IsType<Option<BookingRequest>.None>(BookingLookup.Find(batch, "missing"));
        Assert.IsType<Option<BookingRequest>.None>(BookingLookup.Find(batch, null));
        Assert.IsType<Option<BookingRequest>.None>(BookingLookup.Find(batch, " "));
        Assert.Same(present.Value, BookingLookup.Require(batch, "A").Value);
        FailureContains(BookingLookup.Require(batch, "missing"), "'missing' was not found");
    }

    [Fact]
    public void PrimitiveSomeValuesRemainPresentForZeroFalseAndEmptyString()
    {
        Option<int> zero = new Option<int>.Some(0);
        Option<bool> falseValue = new Option<bool>.Some(false);
        Option<string> empty = new Option<string>.Some("");

        Assert.Equal(0, Assert.IsType<Option<int>.Some>(zero).Value);
        Assert.False(Assert.IsType<Option<bool>.Some>(falseValue).Value);
        Assert.Equal("", Assert.IsType<Option<string>.Some>(empty).Value);
        Assert.IsType<Option<int>.None>(new Option<int>.None());
    }

    [Fact]
    public void MapPassesTheSuccessfulPayloadAndTransformsItsType()
    {
        var observed = 0;

        var mapped = Result<int>.Success(4).Map(value =>
        {
            observed = value;
            return $"Passengers: {value * 2}";
        });

        Assert.Equal(4, observed);
        Assert.Equal("Passengers: 8", mapped.Value);
    }

    [Fact]
    public void BindPassesThePayloadAndPreservesTheNextResult()
    {
        var nextResult = Result<string>.Success("Quote A");
        var observed = 0;

        var bound = Result<int>.Success(4).Bind(value =>
        {
            observed = value;
            return nextResult;
        });

        Assert.Equal(4, observed);
        Assert.Same(nextResult, bound);
        Assert.Equal("Quote A", bound.Value);
        var rejected = Result<string>.Failure("Quote rejected");
        Assert.Same(rejected, Result<int>.Success(4).Bind(_ => rejected));
    }

    [Fact]
    public void FailurePropagatesUnchangedAndSkipsBothDependentCallbacks()
    {
        var mapCalls = 0;
        var bindCalls = 0;
        var initial = Result<int>.Failure("Booking missing: A");

        var result = initial
            .Map(value => { mapCalls++; return value * 2; })
            .Bind(value => { bindCalls++; return Result<string>.Success(value.ToString()); });

        FailureContains(result, "Booking missing: A");
        Assert.Equal(initial.Error, result.Error);
        Assert.Equal(0, mapCalls);
        Assert.Equal(0, bindCalls);
    }

    [Fact]
    public void ResultRejectsContradictorySuccessAndUninformativeFailure()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string?>.Success(null));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(""));
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(" \t "));
        Assert.Equal(0, Result<int>.Success(0).Value);
        Assert.False(Result<bool>.Success(false).Value);
    }

    [Fact]
    public void MatchCallsOnlyTheActiveCaseHandler()
    {
        var successCalls = 0;
        var failureCalls = 0;
        string Success(int value) { successCalls++; return $"Quote {value}"; }
        string Failure(string error) { failureCalls++; return error; }

        Assert.Equal("Quote 7", Result<int>.Success(7).Match(Success, Failure));
        Assert.Equal(1, successCalls);
        Assert.Equal(0, failureCalls);
        Assert.Equal("Cannot quote A", Result<int>.Failure("Cannot quote A").Match(Success, Failure));
        Assert.Equal(1, successCalls);
        Assert.Equal(1, failureCalls);
    }

    [Fact]
    public void MissingFileReportsItsPathSkipsPolicyAndPreservesCompletedState()
    {
        using var files = new TestFiles();
        var path = files.PathFor("does-not-exist.json");
        var completed = CompleteExistingBooking();
        var before = completed.Summary;
        var quotesBefore = before.Quotes.ToArray();
        var calls = 0;

        var result = BookingFiles.LoadQuotes(path, AsOf, _ => { calls++; return 10m; });

        FailureContains(result, "Cannot read input");
        Assert.Contains(path, result.Error);
        Assert.Equal(0, calls);
        Assert.False(File.Exists(path));
        Assert.Same(before, completed.Summary);
        Assert.Equal<BookingQuote>(quotesBefore, completed.Summary.Quotes);
        Assert.Equal(49_000m, completed.Summary.TotalUsd);
        Assert.Same(completed, QuoteWorkflow.Calculate(completed).Value);
    }

    [Fact]
    public void TruncatedSecondRecordReportsItsPathWithoutPricingOrPartialStateUpdate()
    {
        using var files = new TestFiles();
        var content = "[" + MoonRecord + ", { \"Id\": \"Broken\", \"Passengers\":";
        var path = files.Write("truncated.json", content);
        var completed = CompleteExistingBooking();
        var before = completed.Summary;
        var quotesBefore = before.Quotes.ToArray();
        var calls = 0;

        var result = BookingFiles.LoadQuotes(path, AsOf, _ => { calls++; return 10m; });

        FailureContains(result, "Invalid JSON");
        Assert.Contains(path, result.Error);
        Assert.Equal(0, calls);
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Same(before, completed.Summary);
        Assert.Equal<BookingQuote>(quotesBefore, completed.Summary.Quotes);
        Assert.Equal(49_000m, completed.Summary.TotalUsd);
        Assert.Same(completed, QuoteWorkflow.Calculate(completed).Value);
    }

    [Fact]
    public void FileLookupFindsAnActualBookingAndTurnsAbsenceIntoFailure()
    {
        using var files = new TestFiles();
        var path = files.Write("lookup.json", Json(MoonRecord));

        var present = BookingFiles.FindBooking(path, "A");
        var absent = BookingFiles.FindBooking(path, "unknown");

        Assert.True(present.IsSuccess, present.Error);
        Assert.Equal("A", present.Value.Id);
        Assert.False(present.Value.IsCancelled);
        FailureContains(absent, "'unknown' was not found");
    }

    [Fact]
    public void CommandLineFindReportsPresentAndMissingBookingsWithAppropriateExitCodes()
    {
        using var files = new TestFiles();
        var path = files.Write("lookup.json", Json(MoonRecord));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run(new[] { "find", path, "A" }, output, error));
        Assert.Contains("Found booking: A", output.ToString());
        Assert.Contains("Moon", output.ToString());
        Assert.Contains("False", output.ToString());
        Assert.Equal("", error.ToString());

        output.GetStringBuilder().Clear();
        Assert.Equal(1, CommandLine.Run(new[] { "find", path, "missing" }, output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("'missing' was not found", error.ToString());
    }

    [Fact]
    public void CommandLineMalformedFileEmitsOnlyAnErrorAndNeverPartialQuotes()
    {
        using var files = new TestFiles();
        var path = files.Write("malformed.json", "[" + MoonRecord + ", {");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run(new[] { path, "both", "2030-06-15" }, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("Invalid JSON", error.ToString());
        Assert.Contains(path, error.ToString());
    }

    private static QuoteState.Completed CompleteExistingBooking() =>
        QuoteWorkflow.Prepare(new QuoteState.Pending(new[] { Moon("Existing") }, AsOf, BookingPipeline.Standard))
            .Bind(ready => QuoteWorkflow.Calculate(ready)).Value;

    private sealed class TestFiles : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-assignment7-" + Guid.NewGuid().ToString("N"));

        public TestFiles() => Directory.CreateDirectory(directory);

        public string PathFor(string name) => Path.Combine(directory, name);

        public string Write(string name, string content)
        {
            var path = PathFor(name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
