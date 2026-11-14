namespace Astra.Core.Reservations;

public static class ReservationChanges
{
    public static Result<ReservationChange> Cancel(ReservationState state, string id, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(state);
        return RequireActive(state, id, asOf).Bind(source =>
        {
            var daysBeforeDeparture = source.Flight.Departure.DayNumber - asOf.DayNumber;
            var refund = decimal.Round(source.Booking.TotalUsd * (daysBeforeDeparture >= 7 ? 1m : 0.5m),
                2, MidpointRounding.AwayFromZero);

            return Reservation.Create(source.Booking.Id, source.Booking.FlightId,
                    source.Booking.Passengers, source.Booking.TravelClass, source.Booking.TotalUsd,
                    ReservationStatus.Cancelled, source.Booking.BookedOn, asOf, refund)
                .Bind(cancelled => Replace(state, cancelled, -refund));
        });
    }

    public static Result<ReservationChange> Transfer(ReservationState state, string id,
        string targetFlightId, TravelClass targetClass, DateOnly asOf,
        Func<Flight, int, TravelClass, decimal> policy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(policy);

        return RequireActive(state, id, asOf)
            .Bind(source => RequireTarget(state, source, targetFlightId)
                .Bind(target => ReservationBooking.Quote(state, target.Id, source.Booking.Passengers,
                        targetClass, asOf, policy)
                    .Bind(quote => Reservation.Create(source.Booking.Id, target.Id,
                            source.Booking.Passengers, targetClass, quote.TotalUsd,
                            ReservationStatus.Confirmed, source.Booking.BookedOn)
                        .Bind(updated => Replace(state, updated,
                            updated.TotalUsd - source.Booking.TotalUsd)))));
    }

    private static Result<(Reservation Booking, Flight Flight)> RequireActive(
        ReservationState state, string id, DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<(Reservation, Flight)>.Failure("Reservation ID is required.");

        return state.FindReservation(id.Trim()) switch
        {
            Option<Reservation>.Some found => found.Value.Status switch
            {
                ReservationStatus.Cancelled => Result<(Reservation, Flight)>.Failure(
                    $"Reservation '{found.Value.Id}' is already cancelled."),
                ReservationStatus.Confirmed when asOf < found.Value.BookedOn =>
                    Result<(Reservation, Flight)>.Failure("Operation date cannot be before the booking date."),
                ReservationStatus.Confirmed => RequireFlight(state, found.Value.FlightId)
                    .Bind(flight => asOf >= flight.Departure
                        ? Result<(Reservation, Flight)>.Failure("Changes require a date before departure.")
                        : Result<(Reservation, Flight)>.Success((found.Value, flight))),
                _ => Result<(Reservation, Flight)>.Failure("Unknown reservation status.")
            },
            _ => Result<(Reservation, Flight)>.Failure($"Reservation '{id}' was not found.")
        };
    }

    private static Result<Flight> RequireTarget(ReservationState state,
        (Reservation Booking, Flight Flight) source, string targetFlightId) =>
        RequireFlight(state, targetFlightId).Bind(target =>
        {
            if (target.Id == source.Booking.FlightId)
                return Result<Flight>.Failure("Transfer requires a different flight.");
            return target.Destination != source.Flight.Destination
                ? Result<Flight>.Failure("Transfer requires the same destination.")
                : Result<Flight>.Success(target);
        });

    private static Result<Flight> RequireFlight(ReservationState state, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<Flight>.Failure("Flight ID is required.");
        return state.FindFlight(id.Trim()) switch
        {
            Option<Flight>.Some found => Result<Flight>.Success(found.Value),
            _ => Result<Flight>.Failure($"Flight '{id}' was not found.")
        };
    }

    private static Result<ReservationChange> Replace(ReservationState state,
        Reservation updated, decimal balanceDueUsd) =>
        ReservationState.Create(state.Flights,
                state.Reservations.Select(booking => booking.Id == updated.Id ? updated : booking))
            .Map(next => new ReservationChange(next, updated, balanceDueUsd));
}
