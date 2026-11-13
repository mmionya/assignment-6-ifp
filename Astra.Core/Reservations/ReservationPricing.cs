namespace Astra.Core.Reservations;

public static class ReservationPricing
{
    // Configure once, then apply the resulting function to any valid flight/request.
    public static readonly Func<PricingConfig, Func<Flight, int, TravelClass, decimal>> CreatePolicy =
        config =>
        {
            ArgumentNullException.ThrowIfNull(config);
            return (flight, passengers, travelClass) => Standard(flight, passengers, travelClass)
                * (passengers >= config.GroupMinimum ? (100m - config.DiscountPercent) / 100m : 1m);
        };

    public static Func<Flight, int, TravelClass, decimal> Group10 { get; } = CreatePolicy(PricingConfig.Create(4, 10m).Value);
    public static Func<Flight, int, TravelClass, decimal> Family5 { get; } = CreatePolicy(PricingConfig.Create(2, 5m).Value);

    public static decimal Standard(Flight flight, int passengers, TravelClass travelClass)
    {
        ArgumentNullException.ThrowIfNull(flight);
        if (passengers is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(passengers));
        var multiplier = travelClass switch
        {
            TravelClass.Explorer => 1m,
            TravelClass.Premium => 1.35m,
            TravelClass.First => 1.8m,
            _ => throw new ArgumentOutOfRangeException(nameof(travelClass))
        };
        return flight.BaseFareUsd * passengers * multiplier;
    }
}
