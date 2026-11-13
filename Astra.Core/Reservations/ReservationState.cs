using System.Collections.Immutable;

namespace Astra.Core.Reservations;

public sealed class ReservationState
{
    public ImmutableArray<Flight> Flights { get; }
    public ImmutableArray<Reservation> Reservations { get; }

    private ReservationState(ImmutableArray<Flight> flights, ImmutableArray<Reservation> reservations) =>
        (Flights, Reservations) = (flights, reservations);

    public static Result<ReservationState> Create(IEnumerable<Flight>? flights, IEnumerable<Reservation>? reservations)
    {
        if (flights is null || reservations is null)
            return Result<ReservationState>.Failure("Flights and reservations are required; use empty arrays for empty data.");
        var flightSnapshot = flights.ToImmutableArray();
        var bookingSnapshot = reservations.ToImmutableArray();
        if (flightSnapshot.Any(f => f is null) || bookingSnapshot.Any(b => b is null))
            return Result<ReservationState>.Failure("Flights and reservations cannot contain null records.");
        if (flightSnapshot.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != flightSnapshot.Length)
            return Result<ReservationState>.Failure("Flight Id must be unique.");
        if (bookingSnapshot.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != bookingSnapshot.Length)
            return Result<ReservationState>.Failure("Reservation Id must be unique, including cancelled reservations.");
        var byId = flightSnapshot.ToDictionary(f => f.Id, StringComparer.Ordinal);
        foreach (var booking in bookingSnapshot)
        {
            if (!byId.TryGetValue(booking.FlightId, out var flight))
                return Result<ReservationState>.Failure($"Reservation '{booking.Id}' references missing flight '{booking.FlightId}'.");
            if (booking.BookedOn >= flight.Departure || booking.CancelledOn >= flight.Departure)
                return Result<ReservationState>.Failure($"Reservation '{booking.Id}' booking/cancellation dates must be before departure.");
        }
        foreach (var group in bookingSnapshot.Where(b => b.Status == ReservationStatus.Confirmed).GroupBy(b => b.FlightId))
            if (group.Sum(b => (long)b.Passengers) > byId[group.Key].Capacity)
                return Result<ReservationState>.Failure($"Flight '{group.Key}' is over capacity.");
        return Result<ReservationState>.Success(new ReservationState(flightSnapshot, bookingSnapshot));
    }

    public Option<Flight> FindFlight(string? id) => Flights.FirstOrDefault(f => f.Id == id?.Trim()) is { } found
        ? new Option<Flight>.Some(found) : new Option<Flight>.None();

    public Option<Reservation> FindReservation(string? id) => Reservations.FirstOrDefault(b => b.Id == id?.Trim()) is { } found
        ? new Option<Reservation>.Some(found) : new Option<Reservation>.None();

    // A missing flight has no seats; workflows distinguish missing from full via FindFlight first.
    public int AvailableSeats(string flightId, string? excludingReservationId = null) => FindFlight(flightId) switch
    {
        Option<Flight>.Some found => found.Value.Capacity - Reservations
            .Where(b => b.FlightId == found.Value.Id && b.Status == ReservationStatus.Confirmed && b.Id != excludingReservationId)
            .Sum(b => b.Passengers),
        _ => 0
    };
}
