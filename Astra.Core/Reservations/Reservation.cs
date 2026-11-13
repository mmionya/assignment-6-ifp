namespace Astra.Core.Reservations;

public enum ReservationStatus { Confirmed, Cancelled }

public sealed class Reservation
{
    public string Id { get; }
    public string FlightId { get; }
    public int Passengers { get; }
    public TravelClass TravelClass { get; }
    public decimal TotalUsd { get; }
    public ReservationStatus Status { get; }
    public DateOnly BookedOn { get; }
    public DateOnly? CancelledOn { get; }
    public decimal RefundUsd { get; }

    private Reservation(string id, string flightId, int passengers, TravelClass travelClass,
        decimal totalUsd, ReservationStatus status, DateOnly bookedOn, DateOnly? cancelledOn, decimal refundUsd) =>
        (Id, FlightId, Passengers, TravelClass, TotalUsd, Status, BookedOn, CancelledOn, RefundUsd) =
        (id, flightId, passengers, travelClass, totalUsd, status, bookedOn, cancelledOn, refundUsd);

    public static Result<Reservation> Create(string? id, string? flightId, int passengers,
        TravelClass travelClass, decimal totalUsd, ReservationStatus status, DateOnly bookedOn,
        DateOnly? cancelledOn = null, decimal refundUsd = 0m)
    {
        if (!ReservationRules.ValidId(id) || !ReservationRules.ValidId(flightId))
            return Result<Reservation>.Failure("Reservation and flight Id must have 1 to 40 visible characters.");
        if (passengers is < 1 or > 6) return Result<Reservation>.Failure("Passengers must be between 1 and 6.");
        if (!Enum.IsDefined(travelClass)) return Result<Reservation>.Failure("Unknown travel class.");
        if (!Enum.IsDefined(status)) return Result<Reservation>.Failure("Unknown reservation status.");
        if (totalUsd < 0m || totalUsd > ReservationRules.MaximumTotal || !ReservationRules.IsMoney(totalUsd))
            return Result<Reservation>.Failure("Reservation total must be a non-negative amount in cents, at most 10800000000 USD.");
        if (bookedOn == default) return Result<Reservation>.Failure("Booking date is required.");
        if (refundUsd < 0m || refundUsd > totalUsd || !ReservationRules.IsMoney(refundUsd))
            return Result<Reservation>.Failure("Refund must be an amount in cents between zero and the reservation total.");
        if (status == ReservationStatus.Confirmed && (cancelledOn is not null || refundUsd != 0m))
            return Result<Reservation>.Failure("A confirmed reservation cannot have cancellation details.");
        if (status == ReservationStatus.Cancelled && (cancelledOn is null || cancelledOn < bookedOn))
            return Result<Reservation>.Failure("A cancelled reservation needs a cancellation date on or after booking.");
        return Result<Reservation>.Success(new Reservation(id!.Trim(), flightId!.Trim(), passengers,
            travelClass, totalUsd, status, bookedOn, cancelledOn, refundUsd));
    }
}

public sealed record ReservationQuote(Flight Flight, int Passengers, TravelClass TravelClass, decimal TotalUsd);
public sealed record ReservationChange(ReservationState State, Reservation Booking, decimal BalanceDueUsd);
