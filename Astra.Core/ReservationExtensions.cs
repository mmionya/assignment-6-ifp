using Astra.Core.Reservations;

namespace Astra.Core;

public static class ReservationExtensions
{
    public static IEnumerable<Flight> Upcoming(this IEnumerable<Flight> flights, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(flights);
        return flights.Where(flight => flight.IsBookable && flight.Departure > asOf);
    }

    public static Result<T> WithContext<T>(this Result<T> result, string context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return result.Match(_ => result, error => Result<T>.Failure($"{context}: {error}"));
    }
}
