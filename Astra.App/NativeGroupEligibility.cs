using System.Runtime.InteropServices;
using Astra.Core;
using Astra.Core.Reservations;

namespace Astra.App;

public sealed class NativeGroupEligibility
{
    [DllImport("astra_native", EntryPoint = "astra_group_eligible", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int CheckNative(int passengers, int minimum);

    public Result<bool> IsEligible(int passengers, int minimum)
    {
        if (passengers is < 1 or > Reservation.MaximumPassengers ||
            minimum is < 1 or > Reservation.MaximumPassengers)
            return Result<bool>.Failure($"Native eligibility: passengers and minimum must be between 1 and {Reservation.MaximumPassengers}.");

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
