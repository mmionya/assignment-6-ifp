using Astra.Core;
using Astra.Core.Reservations;
using Xunit;

namespace Astra.Tests;

public sealed class ReservationBookingTests
{
    private static readonly DateOnly Today = new(2030, 6, 1);
    private static readonly DateOnly Departure = new(2030, 6, 15);
    private static Flight Flight(int capacity = 12, bool open = true) =>
        Astra.Core.Reservations.Flight.Create("MOON", Destination.Moon, Departure, capacity, 49_000m, open).Value;
    private static ReservationState State(int capacity = 12, bool open = true) =>
        ReservationState.Create([Flight(capacity, open)], []).Value;

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void FlightFactoryRejectsOutOfRangeCapacity(int capacity) =>
        Assert.False(Astra.Core.Reservations.Flight.Create("MOON", Destination.Moon, Departure,
            capacity, 49_000m, true).IsSuccess);

    [Fact]
    public void FlightFactoryValidatesIdentityDateDestinationAndExactMoney()
    {
        Assert.False(Astra.Core.Reservations.Flight.Create(" ", Destination.Moon, Departure, 2, 1m, true).IsSuccess);
        Assert.False(Astra.Core.Reservations.Flight.Create("M\nOON", Destination.Moon, Departure, 2, 1m, true).IsSuccess);
        Assert.False(Astra.Core.Reservations.Flight.Create("M", (Destination)99, Departure, 2, 1m, true).IsSuccess);
        Assert.False(Astra.Core.Reservations.Flight.Create("M", Destination.Moon, default, 2, 1m, true).IsSuccess);
        Assert.False(Astra.Core.Reservations.Flight.Create("M", Destination.Moon, Departure, 2, 0m, true).IsSuccess);
        Assert.False(Astra.Core.Reservations.Flight.Create("M", Destination.Moon, Departure, 2, 1.001m, true).IsSuccess);
        var edge = Astra.Core.Reservations.Flight.Create(" M ", Destination.Moon, Departure, 10_000, 1_000_000_000m, true);
        Assert.True(edge.IsSuccess, edge.Error);
        Assert.Equal("M", edge.Value.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void ReservationFactoryRejectsInvalidPassengerCounts(int count) =>
        Assert.False(Reservation.Create("A", "MOON", count, TravelClass.Explorer, 100m,
            ReservationStatus.Confirmed, Today).IsSuccess);

    [Fact]
    public void ReservationFactoryRejectsContradictoryCancellationPayloads()
    {
        Assert.False(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Confirmed, Today, Today, 0m).IsSuccess);
        Assert.False(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Cancelled, Today).IsSuccess);
        Assert.False(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Cancelled, Today, Today.AddDays(-1), 50m).IsSuccess);
        Assert.False(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 100m,
            ReservationStatus.Cancelled, Today, Today, 101m).IsSuccess);
        Assert.True(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 0m,
            ReservationStatus.Cancelled, Today, Today, 0m).IsSuccess);
    }

    [Fact]
    public void StateTakesSnapshotsAndDoesNotRetainCallerOwnedLists()
    {
        var flights = new List<Flight> { Flight() };
        var bookings = new List<Reservation>();
        var state = ReservationState.Create(flights, bookings).Value;
        flights.Clear();
        bookings.Add(Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 49000m,
            ReservationStatus.Confirmed, Today).Value);
        Assert.Single(state.Flights);
        Assert.Empty(state.Reservations);
        Assert.Equal(12, state.AvailableSeats("MOON"));
    }

    [Fact]
    public void StateRejectsDuplicateReferencesInvalidDatesAndOverbooking()
    {
        var flight = Flight(1);
        var booking = Reservation.Create("A", "MOON", 2, TravelClass.Explorer, 98000m,
            ReservationStatus.Confirmed, Today).Value;
        Assert.False(ReservationState.Create([flight, flight], []).IsSuccess);
        Assert.False(ReservationState.Create([], [booking]).IsSuccess);
        Assert.False(ReservationState.Create([flight], [booking]).IsSuccess);
        Assert.False(ReservationState.Create([Flight()], [booking, booking]).IsSuccess);
        var late = Reservation.Create("LATE", "MOON", 1, TravelClass.Explorer, 1m,
            ReservationStatus.Confirmed, Departure).Value;
        Assert.False(ReservationState.Create([flight], [late]).IsSuccess);
        Assert.False(ReservationState.Create(null, []).IsSuccess);
    }

    [Fact]
    public void QuoteUsesTheActualFlightFareAndTwoConfiguredPolicies()
    {
        var state = State();
        var standard = ReservationBooking.Quote(state, "MOON", 4, TravelClass.Premium, Today, ReservationPricing.Standard);
        var group = ReservationBooking.Quote(state, "MOON", 4, TravelClass.Premium, Today, ReservationPricing.Group10);
        var family = ReservationBooking.Quote(state, "MOON", 4, TravelClass.Premium, Today, ReservationPricing.Family5);
        Assert.Equal(264600m, standard.Value.TotalUsd);
        Assert.Equal(238140m, group.Value.TotalUsd);
        Assert.Equal(251370m, family.Value.TotalUsd);
        Assert.Empty(state.Reservations);
    }

    [Fact]
    public void PartialApplicationChangesOnlyItsOwnConfiguration()
    {
        var free = ReservationPricing.CreatePolicy(PricingConfig.Create(1, 100m).Value);
        var noDiscount = ReservationPricing.CreatePolicy(PricingConfig.Create(1, 0m).Value);
        Assert.Equal(0m, free(Flight(), 2, TravelClass.Explorer));
        Assert.Equal(98000m, noDiscount(Flight(), 2, TravelClass.Explorer));
        Assert.Equal(98000m, ReservationPricing.Group10(Flight(), 2, TravelClass.Explorer));
        Assert.Equal(93100m, ReservationPricing.Family5(Flight(), 2, TravelClass.Explorer));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(1, 99)]
    public void InvalidRequestStopsBeforePricing(int count, int travelClass)
    {
        var calls = 0;
        var result = ReservationBooking.Quote(State(), "MOON", count, (TravelClass)travelClass, Today,
            (_, _, _) => { calls++; return 10m; });
        Assert.False(result.IsSuccess);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MissingClosedDepartedAndFullFlightsSkipPricing()
    {
        var calls = 0;
        decimal Policy(Flight flight, int passengers, TravelClass travelClass) { calls++; return 1m; }
        Assert.False(ReservationBooking.Quote(State(), "MISSING", 1, TravelClass.Explorer, Today, Policy).IsSuccess);
        Assert.False(ReservationBooking.Quote(State(open: false), "MOON", 1, TravelClass.Explorer, Today, Policy).IsSuccess);
        Assert.False(ReservationBooking.Quote(State(), "MOON", 1, TravelClass.Explorer, Departure, Policy).IsSuccess);
        Assert.False(ReservationBooking.Quote(State(1), "MOON", 2, TravelClass.Explorer, Today, Policy).IsSuccess);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void QuoteRoundsAwayFromZeroAndRejectsUnsupportedPolicyResults()
    {
        Assert.Equal(1.01m, ReservationBooking.Quote(State(), "MOON", 1, TravelClass.Explorer,
            Today, (_, _, _) => 1.005m).Value.TotalUsd);
        Assert.False(ReservationBooking.Quote(State(), "MOON", 1, TravelClass.Explorer,
            Today, (_, _, _) => -1m).IsSuccess);
        Assert.False(ReservationBooking.Quote(State(), "MOON", 1, TravelClass.Explorer,
            Today, (_, _, _) => decimal.MaxValue).IsSuccess);
        Assert.False(ReservationBooking.Quote(State(), "MOON", 1, TravelClass.Explorer,
            Today, (_, _, _) => throw new OverflowException()).IsSuccess);
    }

    [Fact]
    public void ReserveLastSeatsProducesNewStateAndFurtherReservationFails()
    {
        var original = State(2);
        var first = ReservationBooking.Reserve(original, " A ", "MOON", 2, TravelClass.Explorer,
            Today, ReservationPricing.Standard).Value;
        Assert.Equal("A", first.Booking.Id);
        Assert.Equal(98000m, first.BalanceDueUsd);
        Assert.Equal(0, first.State.AvailableSeats("MOON"));
        Assert.Equal(2, original.AvailableSeats("MOON"));
        Assert.Empty(original.Reservations);
        var second = ReservationBooking.Reserve(first.State, "B", "MOON", 1, TravelClass.Explorer,
            Today, ReservationPricing.Standard);
        Assert.False(second.IsSuccess);
        Assert.Single(first.State.Reservations);
    }

    [Theory]
    [InlineData("-0.001")]
    [InlineData("-0.004")]
    [InlineData("10800000000.001")]
    [InlineData("10800000000.004")]
    public void RoundingCannotHideAnOutOfRangePolicyResult(string rawValue)
    {
        var state = State();
        var calls = 0;
        var value = decimal.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);
        decimal Policy(Flight _, int __, TravelClass ___) { calls++; return value; }
        var quote = ReservationBooking.Quote(state, "MOON", 1, TravelClass.Explorer, Today, Policy);
        Assert.False(quote.IsSuccess);
        Assert.Equal(1, calls);
        calls = 0;
        var reserve = ReservationBooking.Reserve(state, "A", "MOON", 1, TravelClass.Explorer, Today, Policy);
        Assert.False(reserve.IsSuccess);
        Assert.Equal(1, calls);
        Assert.Empty(state.Reservations);
        Assert.Equal(12, state.AvailableSeats("MOON"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10800000000")]
    public void ExactPolicyRangeBoundariesRemainValid(string rawValue)
    {
        var value = decimal.Parse(rawValue, System.Globalization.CultureInfo.InvariantCulture);
        var result = ReservationBooking.Reserve(State(), "A", "MOON", 1,
            TravelClass.Explorer, Today, (_, _, _) => value);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(value, result.Value.Booking.TotalUsd);
    }

    [Fact]
    public void DuplicateAndCancelledIdsCannotBeReusedAndSkipThePriceDelegate()
    {
        var active = ReservationBooking.Reserve(State(), "A", "MOON", 1, TravelClass.Explorer,
            Today, ReservationPricing.Standard).Value.State;
        var cancelledBooking = Reservation.Create("A", "MOON", 1, TravelClass.Explorer, 49000m,
            ReservationStatus.Cancelled, Today, Today, 49000m).Value;
        var cancelled = ReservationState.Create(active.Flights, [cancelledBooking]).Value;
        var calls = 0;
        foreach (var state in new[] { active, cancelled })
        {
            var result = ReservationBooking.Reserve(state, " A ", "MOON", 1, TravelClass.Explorer,
                Today, (_, _, _) => { calls++; return 1m; });
            Assert.False(result.IsSuccess);
        }
        Assert.Equal(0, calls);
    }

    [Fact]
    public void LookupsDistinguishAbsenceFromPresentFlightsAndReservations()
    {
        var state = ReservationBooking.Reserve(State(), "A", "MOON", 1, TravelClass.Explorer,
            Today, ReservationPricing.Standard).Value.State;
        Assert.IsType<Option<Flight>.Some>(state.FindFlight(" MOON "));
        Assert.IsType<Option<Flight>.None>(state.FindFlight("UNKNOWN"));
        Assert.IsType<Option<Reservation>.Some>(state.FindReservation("A"));
        Assert.IsType<Option<Reservation>.None>(state.FindReservation(null));
    }
}
