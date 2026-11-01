using System.Collections.Immutable;

namespace Astra.Core;

public sealed record DestinationLine(Destination Destination, int Bookings, int Passengers, decimal RevenueUsd);
public sealed record DestinationReport(ImmutableArray<DestinationLine> Lines, decimal TotalUsd);
public sealed record ReportSet(QuoteSummary Quotes, DestinationReport Demand);

public static class BookingReports
{
    public static QuoteSummary QuoteList(ValidatedBookingBatch batch, DateOnly asOf, Func<BookingRequest, decimal> price) => 
    BookingPipeline.Run(batch, asOf, price);

    public static DestinationReport DemandByDestination(ValidatedBookingBatch batch, DateOnly asOf, Func<BookingRequest, decimal> price)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(price);
        var quotes = BookingQuotes.PriceLazily(batch.Bookings.Upcoming(asOf), price).ToImmutableArray();

        var lines = quotes
        .GroupBy(quote => quote.Booking.Destination)
        .Select(group => new DestinationLine(
            group.Key,
            group.Count(),
            group.Sum(quote => quote.Booking.Passengers),
            group.Sum(quote => quote.TotalUsd)))
        .OrderByDescending(line => line.RevenueUsd)
        .ThenBy(line => line.Destination.ToString(), StringComparer.Ordinal)
        .ToImmutableArray();
        return new DestinationReport(lines, lines.Sum(line => line.RevenueUsd));
    }
    public static ReportSet Build(ValidatedBookingBatch batch, DateOnly asOf, Func<BookingRequest, decimal> price) => 
    new(QuoteList(batch, asOf, price), DemandByDestination(batch, asOf, price));
}
