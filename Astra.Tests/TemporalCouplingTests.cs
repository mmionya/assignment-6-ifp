using System.Reflection;
using Astra.Core;
using Xunit;

namespace Astra.Tests;

public class TemporalCouplingTests
{
    private static readonly DateOnly AsOf = new(2030, 6, 15);

    private static RawBooking Moon(string id = "A", int passengers = 1) =>
        new(id, Destination.Moon, passengers, TravelClass.Explorer, AsOf, false);

    private static void FailsWith<T>(Result<T> result, string reason)
    {
        Assert.False(result.IsSuccess);
        Assert.Contains(reason, result.Error ?? "");
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void FactoryCreatesCompleteNormalizedValue()
    {
        var raw = Moon("  Alpha  ", 6) with { TravelClass = TravelClass.First };
        var result = ValidatedBookingBatch.Create(new[] { raw });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        var booking = Assert.Single(result.Value.Bookings);
        Assert.Equal(new BookingRequest("Alpha", Destination.Moon, 6,
            TravelClass.First, AsOf, false), booking);
        Assert.Equal("  Alpha  ", raw.Id);
    }

    [Fact]
    public void MissingDatasetFailsAndEmptyDatasetProducesUsableBatch()
    {
        FailsWith(ValidatedBookingBatch.Create(null), "Dataset is missing");
        var empty = ValidatedBookingBatch.Create(Array.Empty<RawBooking>()).Value;
        Assert.Empty(empty.Bookings);
        var calls = 0;
        var summary = BookingPipeline.Run(empty, AsOf, _ => { calls++; return 1m; });
        Assert.Empty(summary.Quotes);
        Assert.Equal(0m, summary.TotalUsd);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FactoryRejectsNullRowAndEveryMissingFieldWithoutExposingValue()
    {
        var row = Moon();
        RawBooking?[] invalidRows =
        [
            null,
            row with { Id = null },
            row with { Destination = null },
            row with { Passengers = null },
            row with { TravelClass = null },
            row with { Departure = null },
            row with { IsCancelled = null }
        ];

        foreach (var invalid in invalidRows)
        {
            var result = ValidatedBookingBatch.Create(new[] { invalid });
            FailsWith(result, "are required");
            Assert.StartsWith("Record 1:", result.Error ?? "");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nID")]
    [InlineData("\tID")]
    public void FactoryRejectsBlankAndControlCharacterIds(string id) =>
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon(id) }), "1 to 40 visible characters");

    [Fact]
    public void FactoryEnforcesExactIdLengthAndPassengerLimits()
    {
        var longest = new string('x', ValidatedBookingBatch.MaximumIdLength);
        var batch = ValidatedBookingBatch.Create(new[] { Moon(longest, 1), Moon("B", 6) }).Value;
        Assert.Equal(longest, batch.Bookings[0].Id);
        Assert.Equal(new[] { 1, 6 }, batch.Bookings.Select(booking => booking.Passengers));
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon(longest + "x") }), "1 to 40 visible characters");
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon(passengers: 0) }), "Passengers must be between 1 and 6");
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon(passengers: 7) }), "Passengers must be between 1 and 6");
    }

    [Fact]
    public void FactoryChecksTrimmedIdsWithOrdinalCaseSensitiveUniqueness()
    {
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon("A"), Moon(" A ") }),
            "Record 2: Id must be unique after trimming");
        var distinct = ValidatedBookingBatch.Create(new[] { Moon("A"), Moon("a") }).Value;
        Assert.Equal(new[] { "A", "a" }, distinct.Bookings.Select(booking => booking.Id));
    }

    [Fact]
    public void FactoryRejectsEachUndefinedEnumBeforeConsultingSchedule()
    {
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon() with { Destination = (Destination)99 } }),
            "unknown destination or travel class");
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon() with { TravelClass = (TravelClass)99 } }),
            "unknown destination or travel class");
    }

    [Fact]
    public void FactoryRequiresTheChosenMissionsExactDeparture()
    {
        foreach (var destination in Enum.GetValues<Destination>())
        {
            var departure = BookingPipeline.GetMission(destination).Departure;
            var matching = Moon() with { Destination = destination, Departure = departure };
            Assert.True(ValidatedBookingBatch.Create(new[] { matching }).IsSuccess);
            FailsWith(ValidatedBookingBatch.Create(new[] { matching with { Departure = departure.AddDays(1) } }),
                "Departure must match the mission schedule");
        }
    }

    [Fact]
    public void ValidatedCapabilityHasNoPublicConstructionOrMutationPath()
    {
        var type = typeof(ValidatedBookingBatch);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        var run = Assert.Single(typeof(BookingPipeline).GetMethods(),
            method => method.Name == nameof(BookingPipeline.Run));
        Assert.Equal(typeof(ValidatedBookingBatch), run.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void StateCasesAreClosedAndPreparedStatesCannotBeConstructedPublicly()
    {
        var constructor = Assert.Single(typeof(QuoteState).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.True(constructor.IsPrivate);
        var cases = typeof(QuoteState).GetNestedTypes();
        Assert.Equal(new[] { "Completed", "Pending", "Ready" }, cases.Select(type => type.Name).Order());
        Assert.All(cases, type =>
        {
            Assert.True(type.IsSealed);
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        });
        Assert.Empty(typeof(QuoteState.Ready).GetConstructors());
        Assert.Empty(typeof(QuoteState.Completed).GetConstructors());
    }

    [Fact]
    public void PendingAndValidatedBatchOwnSnapshotsOfMutableSource()
    {
        var original = Moon(" A ");
        var rows = new List<RawBooking?> { original };
        var pending = new QuoteState.Pending(rows, AsOf, BookingPipeline.Standard);
        var validated = ValidatedBookingBatch.Create(rows).Value;

        rows[0] = Moon("Changed", 6);
        rows.Add(Moon("Extra"));
        Assert.Same(original, Assert.Single(pending.Rows));
        Assert.Equal("A", Assert.Single(validated.Bookings).Id);
        Assert.Equal(1, validated.Bookings[0].Passengers);

        var editedCopy = validated.Bookings.SetItem(0, validated.Bookings[0] with { Passengers = 6 });
        Assert.Equal(6, editedCopy[0].Passengers);
        Assert.Equal(1, validated.Bookings[0].Passengers);
        Assert.Equal(" A ", original.Id);
    }

    [Fact]
    public void PrepareRetainsConfigurationWithoutInvokingPolicy()
    {
        var calls = 0;
        Func<BookingRequest, decimal> policy = _ => { calls++; return 5m; };
        var date = AsOf.AddDays(1);
        var pending = new QuoteState.Pending(new[] { Moon(" A ") }, date, policy);
        var ready = QuoteWorkflow.Prepare(pending).Value;

        Assert.Equal(date, ready.AsOf);
        Assert.Same(policy, ready.PricePolicy);
        Assert.Equal("A", Assert.Single(ready.Batch.Bookings).Id);
        Assert.Equal(" A ", Assert.Single(pending.Rows)?.Id);
        Assert.Equal(0, calls);
        Assert.Empty(QuoteWorkflow.Calculate(ready).Value.Summary.Quotes);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CalculatingPendingRejectsMissingPrerequisiteWithoutPolicyCalls()
    {
        var calls = 0;
        var source = new[] { Moon() };
        var pending = new QuoteState.Pending(source, AsOf, _ => { calls++; return 10m; });
        var originalRows = pending.Rows;

        FailsWith(QuoteWorkflow.Calculate(pending), "Missing prerequisite");

        Assert.Equal(0, calls);
        Assert.Equal(originalRows, pending.Rows);
        Assert.Same(source[0], pending.Rows[0]);
    }

    [Fact]
    public void ReadyCalculatesAnImmutableCompletedSummaryUsingItsStoredPolicy()
    {
        var calls = new List<string>();
        var pending = new QuoteState.Pending(new[] { Moon("B"), Moon("A", 2) }, AsOf,
            booking => { calls.Add(booking.Id); return booking.Passengers * 12.345m; });
        var ready = QuoteWorkflow.Prepare(pending).Value;
        var before = ready.Batch.Bookings;
        var completed = QuoteWorkflow.Calculate(ready).Value;

        Assert.Equal(new[] { "B", "A" }, calls);
        Assert.Equal(new[] { "B", "A" }, completed.Summary.Quotes.Select(quote => quote.Booking.Id));
        Assert.Equal(new[] { 12.35m, 24.69m }, completed.Summary.Quotes.Select(quote => quote.TotalUsd));
        Assert.Equal(37.04m, completed.Summary.TotalUsd);
        Assert.Equal(before, ready.Batch.Bookings);
    }

    [Fact]
    public void RepeatingCompletedReturnsSameInstanceAndNeverCallsPolicyAgain()
    {
        var calls = 0;
        var pending = new QuoteState.Pending(new[] { Moon() }, AsOf, _ => { calls++; return 42m; });
        var completed = QuoteWorkflow.Prepare(pending).Bind(ready => QuoteWorkflow.Calculate(ready)).Value;

        var second = QuoteWorkflow.Calculate(completed).Value;
        var third = QuoteWorkflow.Calculate(second).Value;

        Assert.Same(completed, second);
        Assert.Same(completed, third);
        Assert.Same(completed.Summary, third.Summary);
        Assert.Equal(42m, completed.Summary.TotalUsd);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RepeatingReadyRecomputesDeterministicallyForAPurePolicy()
    {
        var calls = 0;
        var pending = new QuoteState.Pending(new[] { Moon("A"), Moon("B", 2) }, AsOf,
            booking => { calls++; return BookingPipeline.Standard(booking); });
        var ready = QuoteWorkflow.Prepare(pending).Value;

        var first = QuoteWorkflow.Calculate(ready).Value;
        var second = QuoteWorkflow.Calculate(ready).Value;

        // Reusing Ready runs the calculation again; reusing Completed returns the existing result.
        Assert.NotSame(first, second);
        Assert.Equal<BookingQuote>(first.Summary.Quotes, second.Summary.Quotes);
        Assert.Equal(first.Summary.TotalUsd, second.Summary.TotalUsd);
        Assert.Equal(147_000m, first.Summary.TotalUsd);
        Assert.Equal(4, calls);
        Assert.Equal(new[] { "A", "B" }, ready.Batch.Bookings.Select(booking => booking.Id));
    }

    [Fact]
    public void FailureAfterValidRowLeavesInputsAndPreviouslyCompletedResultUnchanged()
    {
        var calls = 0;
        Func<BookingRequest, decimal> policy = booking => { calls++; return BookingPipeline.Standard(booking); };
        var completed = QuoteWorkflow.Prepare(new QuoteState.Pending(new[] { Moon("Existing") }, AsOf, policy))
            .Bind(ready => QuoteWorkflow.Calculate(ready)).Value;
        var originalSummary = completed.Summary;
        var originalQuotes = originalSummary.Quotes;
        var rows = new[] { Moon(" First "), Moon("Bad", 0) };
        var rawSnapshot = rows.ToArray();
        var pending = new QuoteState.Pending(rows, AsOf, policy);
        var pendingSnapshot = pending.Rows;

        FailsWith(QuoteWorkflow.Prepare(pending), "Record 2: Passengers must be between 1 and 6");

        Assert.Equal(rawSnapshot, rows);
        Assert.Equal(pendingSnapshot, pending.Rows);
        Assert.Equal(" First ", pending.Rows[0]?.Id);
        Assert.Equal(1, calls);
        Assert.Same(originalSummary, completed.Summary);
        Assert.Equal(originalQuotes, completed.Summary.Quotes);
        Assert.Equal(49_000m, completed.Summary.TotalUsd);
        Assert.Same(completed, QuoteWorkflow.Calculate(completed).Value);
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("A\u2028B")]   // line separator
    [InlineData("A\u200BB")]   // zero-width space
    public void FactoryRejectsInvisibleUnicodeInIds(string id) =>
    FailsWith(ValidatedBookingBatch.Create(new[] { Moon(id) }), "1 to 40 visible characters");

    [Fact]
    public void FactoryAcceptsInnerSpacesCancelledAndNonBookableRows()
    {
        var europa = new RawBooking("E", Destination.Europa, 1, TravelClass.Explorer, new DateOnly(2031, 3, 20), false);
        var batch = ValidatedBookingBatch.Create(new[] { Moon("Moon 1") with { IsCancelled = true }, europa }).Value;
        Assert.Equal(new[] { "Moon 1", "E" }, batch.Bookings.Select(b => b.Id));
        Assert.True(batch.Bookings[0].IsCancelled);
    }

    [Fact]
    public void FactoryRejectsCancelledRowWithWrongDeparture() =>
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon() with { IsCancelled = true, Departure = AsOf.AddDays(1) } }),
            "Departure must match the mission schedule");

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void FactoryRejectsNegativeAndHugePassengerCounts(int count) =>
        FailsWith(ValidatedBookingBatch.Create(new[] { Moon(passengers: count) }), "Passengers must be between 1 and 6");

    [Fact]
    public void FactoryReportsTheFirstInvalidRecordAndEnumeratesOnce()
    {
        var enumerations = 0;
        IEnumerable<RawBooking?> Rows()
        {
            enumerations++;
            yield return Moon("A", 0);
            yield return Moon(" ");
        }

        var result = ValidatedBookingBatch.Create(Rows());

        FailsWith(result, "Record 1: Passengers");
        Assert.Equal(1, enumerations);
    }
}
