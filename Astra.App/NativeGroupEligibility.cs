using System.Runtime.InteropServices;
using Astra.Core;

namespace Astra.App;

public interface IGroupEligibility
{
    Result<bool> IsEligible(int passengers, int minimum);
}

public sealed class NativeGroupEligibility : IGroupEligibility
{
    [DllImport("astra_native", EntryPoint = "astra_group_eligible", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int CheckNative(int passengers, int minimum);

    public Result<bool> IsEligible(int passengers, int minimum)
    {
        if (passengers is < 1 or > ValidatedBookingBatch.MaximumPassengers ||
            minimum is < 1 or > ValidatedBookingBatch.MaximumPassengers)
            return Result<bool>.Failure("Native eligibility: passengers and minimum must be between 1 and 6.");

        try
        {
            return CheckNative(passengers, minimum) switch
            {
                1 => Result<bool>.Success(true),
                0 => Result<bool>.Success(false),
                var code => Result<bool>.Failure($"Native eligibility returned invalid status {code}.")
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result<bool>.Failure($"Native eligibility unavailable ({ex.GetType().Name}): {ex.Message}");
        }
    }
}

public sealed record NativeGroupSummary(int MinimumPassengers, int EligibleBookings);

public static class NativeReportCalculation
{
        public static Result<NativeGroupSummary> CountEligible(QuoteSummary quotes, IGroupEligibility eligibility,
        int minimum = 4)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        ArgumentNullException.ThrowIfNull(eligibility);
        if (minimum is < 1 or > ValidatedBookingBatch.MaximumPassengers)
            return Result<NativeGroupSummary>.Failure("Native group threshold must be between 1 and 6.");
        var total = 0;
        foreach (var quote in quotes.Quotes)
        {
            var outcome = eligibility.IsEligible(quote.Booking.Passengers, minimum);
            if (!outcome.IsSuccess)
                return Result<NativeGroupSummary>.Failure($"Booking '{quote.Booking.Id}': {outcome.Error}");
            if (outcome.Value) total++;
        }
        return Result<NativeGroupSummary>.Success(new NativeGroupSummary(minimum, total));
    }
}
