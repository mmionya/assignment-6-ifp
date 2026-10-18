namespace Astra.Core;

public static class QuoteWorkflow
{
    public static Result<QuoteState.Ready> Prepare(QuoteState.Pending pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return ValidatedBookingBatch.Create(pending.Rows)
            .Map(batch => new QuoteState.Ready(batch, pending.AsOf, pending.PricePolicy));
    }

    public static Result<QuoteState.Completed> Calculate(QuoteState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state switch
        {
            QuoteState.Pending => Result<QuoteState.Completed>.Failure(
                "Missing prerequisite: prepare and validate the batch before calculating quotes."),
            QuoteState.Ready ready => Result<QuoteState.Completed>.Success(new QuoteState.Completed(
                BookingPipeline.Run(ready.Batch, ready.AsOf, ready.PricePolicy))),
            // The result is already available, so we can return it without calculating again.
            QuoteState.Completed completed => Result<QuoteState.Completed>.Success(completed),
            _ => Result<QuoteState.Completed>.Failure("Unknown quote state.")
        };
    }
}
