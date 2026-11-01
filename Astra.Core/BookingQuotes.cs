namespace Astra.Core;
public static class BookingQuotes
{
    public static IEnumerable<BookingQuote> PriceLazily( IEnumerable<BookingRequest> bookings, Func<BookingRequest, decimal> price)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(price);
        return PriceIterator(bookings, price);
    }
    private static IEnumerable<BookingQuote> PriceIterator( IEnumerable<BookingRequest> bookings, Func<BookingRequest, decimal> price)
    {
        foreach (var booking in bookings)
            yield return new BookingQuote(booking, decimal.Round(price(booking), 2, MidpointRounding.AwayFromZero));
    }
}