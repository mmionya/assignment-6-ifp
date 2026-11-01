namespace Astra.Core;
public static class BookingExtensions
{
    public static IEnumerable<BookingRequest> Upcoming( this IEnumerable<BookingRequest> bookings, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        return bookings.Where(booking => !booking.IsCancelled && booking.Departure >= asOf);
    }
    public static Result<T> WithContext<T>(this Result<T> result, string context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        return result.Match(_ => result, error => Result<T>.Failure($"{context}: {error}"));
    }
}
