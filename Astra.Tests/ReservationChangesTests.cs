using Astra.Core;
using Astra.Core.Reservations;
using Xunit;

namespace Astra.Tests;

public class ReservationChangesTests
{
    private static readonly DateOnly BookedOn = new(2030, 6, 1);
    private static readonly DateOnly Departure = new(2030, 6, 15);

    private static Flight Flight(string id, decimal fare = 49_000m, int capacity = 6,
        Destination destination = Destination.Moon, DateOnly? departure = null, bool bookable = true) =>
        Astra.Core.Reservations.Flight.Create(id, destination, departure ?? Departure,
            capacity, fare, bookable).Value;

    private static Reservation Booking(string id = "B1", string flightId = "MOON-1",
        int passengers = 2, decimal price = 98_000m) =>
        Reservation.Create(id, flightId, passengers, TravelClass.Explorer, price,
            ReservationStatus.Confirmed, BookedOn).Value;

    private static ReservationState State(Flight? target = null, params Reservation[] additional) =>
        ReservationState.Create(new[] { Flight("MOON-1"), target ?? Flight("MOON-2", 60_000m,
                departure: new DateOnly(2030, 6, 25)) }, new[] { Booking() }.Concat(additional)).Value;

    private static decimal Fare(Flight flight, int passengers, TravelClass travelClass) =>
        flight.BaseFareUsd * passengers * (travelClass switch
        {
            TravelClass.Explorer => 1m,
            TravelClass.Premium => 1.35m,
            TravelClass.First => 1.8m,
            _ => throw new ArgumentOutOfRangeException(nameof(travelClass))
        });

    [Fact]
    public void CancellationExactlySevenDaysBeforeDepartureRefundsAllAndFreesSeats()
    {
        var original = State();
        var result = ReservationChanges.Cancel(original, "B1", new DateOnly(2030, 6, 8));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(-98_000m, result.Value.BalanceDueUsd);
        Assert.Equal(98_000m, result.Value.Booking.RefundUsd);
        Assert.Equal(ReservationStatus.Cancelled, result.Value.Booking.Status);
        Assert.Equal(new DateOnly(2030, 6, 8), result.Value.Booking.CancelledOn);
        Assert.Equal(6, result.Value.State.AvailableSeats("MOON-1"));
        Assert.Equal(4, original.AvailableSeats("MOON-1"));
        Assert.Equal(ReservationStatus.Confirmed, original.Reservations.Single().Status);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(14)]
    public void CancellationWithinSixDaysUsesHalfOfTheAgreedPrice(int day)
    {
        var result = ReservationChanges.Cancel(State(), "B1", new DateOnly(2030, 6, day));
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(49_000m, result.Value.Booking.RefundUsd);
        Assert.Equal(-49_000m, result.Value.BalanceDueUsd);
        Assert.Equal(98_000m, result.Value.Booking.TotalUsd);
    }

    [Fact]
    public void PartialRefundRoundsHalfCentAwayFromZero()
    {
        var state = ReservationState.Create(new[] { Flight("MOON-1") }, new[] { Booking(price: 0.01m) }).Value;
        var result = ReservationChanges.Cancel(state, "B1", new DateOnly(2030, 6, 14));
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0.01m, result.Value.Booking.RefundUsd);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void CancellationOnOrAfterDepartureIsRejectedWithoutChangingState(int day)
    {
        var state = State();
        var result = ReservationChanges.Cancel(state, "B1", new DateOnly(2030, 6, day));
        Assert.False(result.IsSuccess);
        Assert.Contains("before departure", result.Error);
        Assert.Equal(ReservationStatus.Confirmed, state.Reservations.Single().Status);
        Assert.Equal(4, state.AvailableSeats("MOON-1"));
    }

    [Fact]
    public void CancellationCannotPredateBooking()
    {
        var result = ReservationChanges.Cancel(State(), "B1", BookedOn.AddDays(-1));
        Assert.False(result.IsSuccess);
        Assert.Contains("before the booking date", result.Error);
    }

    [Fact]
    public void RepeatedCancellationCannotIssueASecondRefund()
    {
        var cancelled = ReservationChanges.Cancel(State(), "B1", BookedOn).Value.State;
        var original = cancelled.Reservations.Single();
        var repeated = ReservationChanges.Cancel(cancelled, "B1", BookedOn.AddDays(1));
        Assert.False(repeated.IsSuccess);
        Assert.Contains("already cancelled", repeated.Error);
        Assert.Same(original, cancelled.Reservations.Single());
        Assert.Equal(BookedOn, original.CancelledOn);
    }

    [Theory]
    [InlineData("MISSING")]
    [InlineData("")]
    public void CancellationRequiresAnExistingReservation(string id)
    {
        var result = ReservationChanges.Cancel(State(), id, BookedOn);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void TransferRepricesMovesSeatsAndPreservesOriginalSnapshot()
    {
        var original = State();
        var result = ReservationChanges.Transfer(original, "B1", "MOON-2", TravelClass.Premium,
            BookedOn, Fare);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(162_000m, result.Value.Booking.TotalUsd);
        Assert.Equal(64_000m, result.Value.BalanceDueUsd);
        Assert.Equal("B1", result.Value.Booking.Id);
        Assert.Equal("MOON-2", result.Value.Booking.FlightId);
        Assert.Equal(BookedOn, result.Value.Booking.BookedOn);
        Assert.Null(result.Value.Booking.CancelledOn);
        Assert.Equal(6, result.Value.State.AvailableSeats("MOON-1"));
        Assert.Equal(4, result.Value.State.AvailableSeats("MOON-2"));
        Assert.Equal(4, original.AvailableSeats("MOON-1"));
        Assert.Equal(6, original.AvailableSeats("MOON-2"));
        Assert.Equal("MOON-1", original.Reservations.Single().FlightId);
    }

    [Fact]
    public void TransferToCheaperFlightReportsARefundDifference()
    {
        var state = State(Flight("MOON-2", 30_000m, departure: new DateOnly(2030, 6, 25)));
        var result = ReservationChanges.Transfer(state, "B1", "MOON-2", TravelClass.Explorer, BookedOn, Fare);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(60_000m, result.Value.Booking.TotalUsd);
        Assert.Equal(-38_000m, result.Value.BalanceDueUsd);
    }

    [Fact]
    public void FullTargetDoesNotReleaseOriginalSeatsOrInvokePricing()
    {
        var state = State(Flight("MOON-2", capacity: 2), Booking("B2", "MOON-2"));
        var previous = state.Reservations.ToArray();
        var calls = 0;
        var result = ReservationChanges.Transfer(state, "B1", "MOON-2", TravelClass.Explorer,
            BookedOn, (flight, passengers, travelClass) => { calls++; return Fare(flight, passengers, travelClass); });
        Assert.False(result.IsSuccess);
        Assert.Equal(0, calls);
        Assert.Equal(4, state.AvailableSeats("MOON-1"));
        Assert.Equal(0, state.AvailableSeats("MOON-2"));
        Assert.Equal(previous, state.Reservations.ToArray());
    }

    [Fact]
    public void TransferRequiresADifferentFlightEvenIfClassChanges()
    {
        var calls = 0;
        var result = ReservationChanges.Transfer(State(), "B1", "MOON-1", TravelClass.First,
            BookedOn, (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Contains("different flight", result.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void TransferRequiresTheSameDestinationBeforePricing()
    {
        var state = State(Flight("MARS-1", destination: Destination.Mars));
        var calls = 0;
        var result = ReservationChanges.Transfer(state, "B1", "MARS-1", TravelClass.Explorer,
            BookedOn, (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Contains("same destination", result.Error);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false, 25)]
    [InlineData(true, 1)]
    public void TransferRejectsClosedOrDepartedTargetBeforePricing(bool bookable, int departureDay)
    {
        var state = State(Flight("MOON-2", departure: new DateOnly(2030, 6, departureDay), bookable: bookable));
        var calls = 0;
        var result = ReservationChanges.Transfer(state, "B1", "MOON-2", TravelClass.Explorer,
            BookedOn, (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Equal(0, calls);
        Assert.Equal("MOON-1", state.Reservations.Single().FlightId);
    }

    [Fact]
    public void CancelledReservationCannotBeTransferred()
    {
        var state = ReservationChanges.Cancel(State(), "B1", BookedOn).Value.State;
        var calls = 0;
        var result = ReservationChanges.Transfer(state, "B1", "MOON-2", TravelClass.Explorer,
            BookedOn, (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Contains("already cancelled", result.Error);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    [InlineData(15)]
    public void TransferRejectsInvalidOperationDatesBeforePricing(int offset)
    {
        var state = State();
        var calls = 0;
        var result = ReservationChanges.Transfer(state, "B1", "MOON-2", TravelClass.Explorer,
            BookedOn.AddDays(offset), (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("MISSING", "MOON-2")]
    [InlineData("B1", "MISSING")]
    [InlineData("B1", "")]
    public void TransferRequiresBothExistingRecords(string id, string target)
    {
        var calls = 0;
        var result = ReservationChanges.Transfer(State(), id, target, TravelClass.Explorer,
            BookedOn, (_, _, _) => { calls++; return 1m; });
        Assert.False(result.IsSuccess);
        Assert.Equal(0, calls);
    }
}
