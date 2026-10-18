using System.Collections.Immutable;

namespace Astra.Core;

public enum Destination { Moon, Mars, Europa }
public enum TravelClass { Explorer, Premium, First }

public sealed record Mission(decimal BaseFareUsd, DateOnly Departure, bool IsBookable);
public sealed record BookingRequest(string Id, Destination Destination, int Passengers,
    TravelClass TravelClass, DateOnly Departure, bool IsCancelled);
public sealed record BookingQuote(BookingRequest Booking, decimal TotalUsd);
public sealed record QuoteSummary(ImmutableArray<BookingQuote> Quotes, decimal TotalUsd);

public static class BookingPipeline
{
    // Fixed fictional schedule and prices from ASTRA's missions.html and booking.html.
    public static Mission GetMission(Destination destination) => destination switch
    {
        Destination.Moon => new(49_000m, new(2030, 6, 15), true),
        Destination.Mars => new(2_400_000m, new(2030, 9, 10), true),
        Destination.Europa => new(8_700_000m, new(2031, 3, 20), false),
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    };

    public static decimal Standard(BookingRequest booking)
    {
        var multiplier = booking.TravelClass switch
        {
            TravelClass.Explorer => 1m,
            TravelClass.Premium => 1.35m,
            TravelClass.First => 1.80m,
            _ => throw new ArgumentOutOfRangeException(nameof(booking))
        };
        return GetMission(booking.Destination).BaseFareUsd * booking.Passengers * multiplier;
    }

    public static decimal GroupDiscount(BookingRequest booking) =>
        Standard(booking) * (booking.Passengers >= 4 ? 0.90m : 1m);

    // Stage 1: select active, bookable departures on or after the explicit review date.
    internal static ImmutableArray<BookingRequest> SelectBookable(
        ImmutableArray<BookingRequest> bookings, DateOnly asOf) =>
        bookings.Where(b => !b.IsCancelled && GetMission(b.Destination).IsBookable
            && b.Departure >= asOf).ToImmutableArray();

    // Stage 2: higher-order function; the caller supplies the pricing rule.
    internal static ImmutableArray<BookingQuote> CalculateQuotes(
        ImmutableArray<BookingRequest> bookings, Func<BookingRequest, decimal> pricePolicy) =>
        bookings.Select(b => new BookingQuote(b,
            decimal.Round(pricePolicy(b), 2, MidpointRounding.AwayFromZero))).ToImmutableArray();

    // Stage 3: produce a sorted, immutable summary with the sum of rounded quotes.
    internal static QuoteSummary Summarize(ImmutableArray<BookingQuote> quotes) => new(
        quotes.OrderBy(q => q.TotalUsd).ThenBy(q => q.Booking.Id, StringComparer.Ordinal).ToImmutableArray(),
        quotes.Sum(q => q.TotalUsd));

    public static QuoteSummary Run(ValidatedBookingBatch batch,
        DateOnly asOf, Func<BookingRequest, decimal> pricePolicy)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(pricePolicy);
        var selected = SelectBookable(batch.Bookings, asOf);
        var quotes = CalculateQuotes(selected, pricePolicy);
        return Summarize(quotes);
    }
}
