using System.Globalization;

namespace Astra.Core.Reservations;

public sealed class Flight
{
    public string Id { get; }
    public Destination Destination { get; }
    public DateOnly Departure { get; }
    public int Capacity { get; }
    public decimal BaseFareUsd { get; }
    public bool IsBookable { get; }

    private Flight(string id, Destination destination, DateOnly departure, int capacity,
        decimal baseFareUsd, bool isBookable) =>
        (Id, Destination, Departure, Capacity, BaseFareUsd, IsBookable) =
        (id, destination, departure, capacity, baseFareUsd, isBookable);

    public static Result<Flight> Create(string? id, Destination destination, DateOnly departure,
        int capacity, decimal baseFareUsd, bool isBookable)
    {
        if (!ReservationRules.ValidId(id)) return Result<Flight>.Failure("Flight Id must have 1 to 40 visible characters.");
        if (!Enum.IsDefined(destination)) return Result<Flight>.Failure("Unknown flight destination.");
        if (departure == default) return Result<Flight>.Failure("Flight departure is required.");
        if (capacity is < 1 or > 10_000) return Result<Flight>.Failure("Flight capacity must be between 1 and 10000.");
        if (baseFareUsd <= 0m || baseFareUsd > 1_000_000_000m || !ReservationRules.IsMoney(baseFareUsd))
            return Result<Flight>.Failure("Base fare must be positive, at most 1000000000 USD, and expressed in cents.");
        return Result<Flight>.Success(new Flight(id!.Trim(), destination, departure, capacity, baseFareUsd, isBookable));
    }
}

internal static class ReservationRules
{
    internal const decimal MaximumTotal = 10_800_000_000m;
    internal static bool IsMoney(decimal value) => decimal.Round(value, 2) == value;
    internal static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 40 && value.All(c => !char.IsControl(c)
            && char.GetUnicodeCategory(c) is not (UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator));
}
