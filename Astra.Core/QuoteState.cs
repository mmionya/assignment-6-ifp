using System.Collections.Immutable;

namespace Astra.Core;

// These are the only three states a quote can be in.
public abstract class QuoteState
{
    private QuoteState() { }

    public sealed class Pending : QuoteState
    {
        public ImmutableArray<RawBooking?> Rows { get; }
        public DateOnly AsOf { get; }
        public Func<BookingRequest, decimal> PricePolicy { get; }

        public Pending(IEnumerable<RawBooking?> rows, DateOnly asOf, Func<BookingRequest, decimal> pricePolicy)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(pricePolicy);
            Rows = rows.ToImmutableArray();
            AsOf = asOf;
            PricePolicy = pricePolicy;
        }
    }

    public sealed class Ready : QuoteState
    {
        public ValidatedBookingBatch Batch { get; }
        public DateOnly AsOf { get; }
        public Func<BookingRequest, decimal> PricePolicy { get; }

        internal Ready(ValidatedBookingBatch batch, DateOnly asOf, Func<BookingRequest, decimal> pricePolicy)
        {
            Batch = batch;
            AsOf = asOf;
            PricePolicy = pricePolicy;
        }
    }

    public sealed class Completed : QuoteState
    {
        public QuoteSummary Summary { get; }
        internal Completed(QuoteSummary summary) => Summary = summary;
    }
}
