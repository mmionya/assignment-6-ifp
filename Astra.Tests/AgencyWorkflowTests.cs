using System.Diagnostics;
using System.Text.Json.Nodes;
using Astra.App;
using Astra.Core;
using Astra.Core.Reservations;
using Xunit;

namespace Astra.Tests;

public sealed class AgencyWorkflowTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "samples", "agency.json");

    [Fact]
    public void ReserveTransferCancelPersistsExactPricesAndReleasesCapacity()
    {
        using var files = new TestFiles();
        var path = files.CopySample();
        var reserve = Run("reserve", path, "NEW", "MOON-1", "4", "premium", "group10", "2030-06-01");
        Assert.Equal(0, reserve.Code);
        Assert.Contains("238140.00", reserve.Output);
        Assert.Equal(6, ReservationStore.Load(path).Value.AvailableSeats("MOON-1"));

        var transfer = Run("transfer", path, "NEW", "MOON-2", "premium", "group10", "2030-06-02");
        Assert.Equal(0, transfer.Code);
        Assert.Contains("Balance due: 14580.00", transfer.Output);
        var moved = ReservationStore.Load(path).Value;
        Assert.Equal(10, moved.AvailableSeats("MOON-1"));
        Assert.Equal(8, moved.AvailableSeats("MOON-2"));

        var cancel = Run("cancel", path, "NEW", "2030-06-08");
        Assert.Equal(0, cancel.Code);
        Assert.Contains("Balance due: -252720.00", cancel.Output);
        var final = ReservationStore.Load(path).Value;
        var booking = Assert.IsType<Option<Reservation>.Some>(final.FindReservation("NEW")).Value;
        Assert.Equal(ReservationStatus.Cancelled, booking.Status);
        Assert.Equal(252720m, booking.RefundUsd);
        Assert.Equal(12, final.AvailableSeats("MOON-2"));
        Assert.Equal(4, final.Reservations.Length);
    }

    [Fact]
    public void RejectedTransferAndRepeatedCancellationLeaveFileByteForByteUnchanged()
    {
        using var files = new TestFiles();
        var path = files.CopySample();
        var before = File.ReadAllBytes(path);
        var transfer = Run("transfer", path, "DEMO-A", "MOON-FULL", "explorer", "standard", "2030-06-01");
        Assert.Equal(1, transfer.Code);
        Assert.Empty(transfer.Output);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(0, Run("cancel", path, "DEMO-A", "2030-06-10").Code);
        var afterCancel = File.ReadAllBytes(path);
        Assert.Equal(1, Run("cancel", path, "DEMO-A", "2030-06-10").Code);
        Assert.Equal(afterCancel, File.ReadAllBytes(path));
        var cancelled = ReservationStore.Load(path).Value.Reservations.Single(b => b.Id == "DEMO-A");
        Assert.Equal(49000m, cancelled.RefundUsd);
    }

    [Fact]
    public void FileRoundTripPreservesConfirmedAndCancelledPayloads()
    {
        var state = ReservationStore.Load(Sample).Value;
        var change = ReservationChanges.Cancel(state, "DEMO-A", new DateOnly(2030, 6, 10)).Value;
        var serialized = ReservationStore.Serialize(change.State);
        var restored = ReservationStore.Parse(serialized);
        Assert.True(restored.IsSuccess, restored.Error);
        Assert.Equal(serialized, ReservationStore.Serialize(restored.Value));
        Assert.Equal(new DateOnly(2030, 6, 10), restored.Value.Reservations[0].CancelledOn);
    }

    [Fact]
    public void MissingAndMalformedFilesHaveExplicitFailuresAndSkipTheOperation()
    {
        using var files = new TestFiles();
        var missing = ReservationStore.Load(Path.Combine(files.Path, "missing.json"));
        Assert.False(missing.IsSuccess);
        Assert.Contains("file not found", missing.Error);
        var path = Path.Combine(files.Path, "malformed.json");
        File.WriteAllText(path, "{\"Version\":1,\"Flights\":[");
        var called = false;
        var invalid = ReservationStore.Update(path, state =>
        {
            called = true;
            return Result<ReservationChange>.Failure("Should not run.");
        });
        Assert.False(invalid.IsSuccess);
        Assert.Contains("Invalid state JSON", invalid.Error);
        Assert.False(called);
        Assert.Equal("{\"Version\":1,\"Flights\":[", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Flights\":[],\"Reservations\":[],\"Extra\":true}")]
    [InlineData("{\"Version\":1,\"Version\":1,\"Flights\":[],\"Reservations\":[]}")]
    [InlineData("{\"Version\":2,\"Flights\":[],\"Reservations\":[]}")]
    [InlineData("null")]
    [InlineData("{}")]
    public void StateSchemaRejectsUnknownDuplicateMissingAndUnsupportedFields(string json) =>
        Assert.False(ReservationStore.Parse(json).IsSuccess);

    [Fact]
    public void LoadingRejectsBrokenReferencesOverbookingAndMissingRequiredFields()
    {
        var reference = JsonNode.Parse(File.ReadAllText(Sample))!;
        reference["Reservations"]![0]!["FlightId"] = "UNKNOWN";
        Assert.False(ReservationStore.Parse(reference.ToJsonString()).IsSuccess);
        var capacity = JsonNode.Parse(File.ReadAllText(Sample))!;
        capacity["Flights"]![0]!["Capacity"] = 1;
        Assert.False(ReservationStore.Parse(capacity.ToJsonString()).IsSuccess);
        var missing = JsonNode.Parse(File.ReadAllText(Sample))!;
        missing["Reservations"]![0]!.AsObject().Remove("Passengers");
        Assert.False(ReservationStore.Parse(missing.ToJsonString()).IsSuccess);
    }

    [Fact]
    public void StateLockPreventsLostUpdatesAndReleasesOnBusinessFailure()
    {
        using var files = new TestFiles();
        var path = files.CopySample();
        var before = File.ReadAllBytes(path);
        using (var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var calls = 0;
            var result = ReservationStore.Update(path, state =>
            {
                calls++;
                return Result<ReservationChange>.Failure("blocked");
            });
            Assert.False(result.IsSuccess);
            Assert.Equal(0, calls);
        }
        var failed = ReservationStore.Update(path, _ => Result<ReservationChange>.Failure("domain failure"));
        Assert.Equal("domain failure", failed.Error);
        Assert.Equal(before, File.ReadAllBytes(path));
        using var released = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(released.CanWrite);
    }

    [Fact]
    public void ReportsUseStoredContractsAndActualNativeCountsWithoutChangingState()
    {
        using var files = new TestFiles();
        var path = files.CopySample();
        var csv = Path.Combine(files.Path, "report.csv");
        var before = File.ReadAllBytes(path);
        var result = Run("reports", path, "2030-06-01", csv);
        Assert.Equal(0, result.Code);
        Assert.Contains("Total retained contract value: 11860000.00 USD", result.Output);
        Assert.Contains("Native C: 1 confirmed booking(s)", result.Output);
        Assert.Contains("MOON-FULL", File.ReadAllText(csv));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(1, Run("reports", path, "2030-06-01", path).Code);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReportGroupingIncludesRefundsAndSortingHasStableTies()
    {
        var date = new DateOnly(2030, 7, 1);
        var a = Flight.Create("A", Destination.Moon, date, 10, 100m, true).Value;
        var b = Flight.Create("B", Destination.Mars, date, 10, 100m, true).Value;
        var first = Reservation.Create("ONE", "A", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Confirmed, date.AddDays(-30)).Value;
        var second = Reservation.Create("TWO", "B", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Confirmed, date.AddDays(-30)).Value;
        var state = ReservationState.Create([b, a], [first, second]).Value;
        var reports = AgencyReports.Build(state, date.AddDays(-20));
        Assert.Equal(new[] { "A", "B" }, reports.Occupancy.Select(l => l.FlightId));
        Assert.Equal(new[] { Destination.Mars, Destination.Moon }, reports.Finance.Select(l => l.Destination));
        var cancelled = ReservationChanges.Cancel(state, "ONE", date.AddDays(-1)).Value.State;
        var finance = AgencyReports.Build(cancelled, date.AddDays(-1)).Finance.Single(l => l.Destination == Destination.Moon);
        Assert.Equal(new FinanceLine(Destination.Moon, 0, 1, 100m, 50m, 50m), finance);
    }

    [Fact]
    public void EmptyAndNoUpcomingFlightReportsAreValid()
    {
        var empty = ReservationState.Create([], []).Value;
        var result = AgencyReports.Build(empty, new DateOnly(2030, 6, 1));
        Assert.Empty(result.Occupancy);
        Assert.Empty(result.Finance);
        var past = AgencyReports.Build(ReservationStore.Load(Sample).Value, new DateOnly(2035, 1, 1));
        Assert.Empty(past.Occupancy);
        Assert.Equal(11860000m, past.Finance.Sum(l => l.RetainedUsd));
    }

    [Fact]
    public void QuoteIsReadOnlyAndTwoConfiguredFunctionsDiffer()
    {
        using var files = new TestFiles();
        var path = files.CopySample();
        var before = File.ReadAllBytes(path);
        var group = Run("quote", path, "MOON-1", "4", "premium", "group10", "2030-06-01");
        var family = Run("quote", path, "MOON-1", "4", "premium", "family5", "2030-06-01");
        Assert.Equal(0, group.Code);
        Assert.Equal(0, family.Code);
        Assert.Contains("238140.00", group.Output);
        Assert.Contains("251370.00", family.Output);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("quote", "MOON-1", "x", "explorer", "standard", "2030-06-01")]
    [InlineData("quote", "MOON-1", "2", "9", "standard", "2030-06-01")]
    [InlineData("quote", "MOON-1", "2", "explorer", "unknown", "2030-06-01")]
    [InlineData("quote", "MOON-1", "2", "explorer", "standard", "bad-date")]
    public void InvalidCommandInputProducesOnlyAClearFailure(params string[] args)
    {
        var full = new[] { args[0], Sample }.Concat(args.Skip(1)).ToArray();
        var result = Run(full);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task MissingNativeLibraryInFreshProcessIsAControlledFailure()
    {
        using var files = new TestFiles();
        foreach (var name in new[] { "Astra.App.dll", "Astra.Core.dll", "Astra.App.deps.json", "Astra.App.runtimeconfig.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(files.Path, name));
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = files.Path, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { Path.Combine(files.Path, "Astra.App.dll"), "agency", "reports", Sample, "2030-06-01" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); Assert.Fail("Native-failure subprocess timed out."); }
        Assert.Equal(1, process.ExitCode);
        Assert.Empty(await output);
        Assert.Contains("Native eligibility unavailable (DllNotFoundException)", await error);
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CommandLine.Run(new[] { "agency" }.Concat(args).ToArray(), output, error);
        return (code, output.ToString(), error.ToString());
    }

    private sealed class TestFiles : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-final-test-" + Guid.NewGuid().ToString("N"));
        public TestFiles() => Directory.CreateDirectory(Path);
        public string CopySample()
        {
            var file = System.IO.Path.Combine(Path, "state.json");
            File.Copy(Sample, file);
            return file;
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
