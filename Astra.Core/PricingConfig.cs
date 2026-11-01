namespace Astra.Core;
public sealed class PricingConfig
{
    public int GroupMinimum { get; }
    public decimal DiscountPercent { get; }

    private PricingConfig(int groupMinimum, decimal discountPercent) => (GroupMinimum, DiscountPercent) = (groupMinimum, discountPercent);

    public static Result<PricingConfig> Create(int groupMinimum, decimal discountPercent)
    {
        if (groupMinimum < 1 || groupMinimum > ValidatedBookingBatch.MaximumPassengers)
            return Result<PricingConfig>.Failure($"Group minimum must be between 1 and {ValidatedBookingBatch.MaximumPassengers}, got {groupMinimum}.");
        if (discountPercent < 0m || discountPercent > 100m)
            return Result<PricingConfig>.Failure( $"Discount percent must be between 0 and 100, got {discountPercent}.");
        return Result<PricingConfig>.Success(new PricingConfig(groupMinimum, discountPercent));
    }
}