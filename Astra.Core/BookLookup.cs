namespace Astra.Core;

public static class BookingLookup
{
    public static Option<BookingRequest> Find(ValidatedBookingBatch batch, string? id)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (string.IsNullOrWhiteSpace(id))
        return new Option<BookingRequest>.None();

        string normalizedId = id.Trim();
        foreach (var booking in batch.Bookings)
        {
            if (string.Equals(booking.Id, normalizedId, StringComparison.Ordinal))
            return new Option<BookingRequest>.Some(booking);
        }
        return new Option<BookingRequest>.None();
    }
    public static Result<BookingRequest> Require(ValidatedBookingBatch batch, string? id) =>
        Find(batch, id) switch
        {
            Option<BookingRequest>.Some found => Result<BookingRequest>.Success(found.Value),
            Option<BookingRequest>.None => Result<BookingRequest>.Failure( $"Booking with Id '{id}' was not found."),
            _ => Result<BookingRequest>.Failure("Unknown booking lookup result.")
        };
}
