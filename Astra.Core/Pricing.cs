namespace Astra.Core;
public static class Pricing
{
    public static readonly Func<PricingConfig, Func<BookingRequest, decimal>> CreatePolicy =
        config =>
        {
            ArgumentNullException.ThrowIfNull(config);
            return booking =>
            {
                var price = BookingPipeline.Standard(booking);
                return booking.Passengers >= config.GroupMinimum ? price * (100m - config.DiscountPercent) / 100m : price;
            };
        };
    public static Func<BookingRequest, decimal> Group10 { get; } = CreatePolicy(Fixed(4, 10m));
    public static Func<BookingRequest, decimal> Family5 { get; } = CreatePolicy(Fixed(2, 5m));
    private static PricingConfig Fixed(int groupMinimum, decimal discountPercent) => PricingConfig.Create(groupMinimum, discountPercent).Value;
}
