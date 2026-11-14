namespace Astra.Core.Reservations;

public static class ReservationBooking
{
    private sealed record CheckedRequest(ReservationState State, Flight Flight, int Passengers, TravelClass TravelClass);

    public static Result<ReservationQuote> Quote(ReservationState state, string? flightId, int passengers,
        TravelClass travelClass, DateOnly asOf, Func<Flight, int, TravelClass, decimal> policy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(policy);
        return ValidateRequest(state, flightId, passengers, travelClass, asOf)
            .Bind(CheckAvailability)
            .Bind(request => CalculateQuote(request, policy));
    }

    private static Result<CheckedRequest> ValidateRequest(ReservationState state, string? flightId,
        int passengers, TravelClass travelClass, DateOnly asOf)
    {
        if (asOf == default) return Result<CheckedRequest>.Failure("Review date is required.");
        if (passengers is < 1 or > Reservation.MaximumPassengers) return Result<CheckedRequest>.Failure($"Passengers must be between 1 and {Reservation.MaximumPassengers}.");
        if (!Enum.IsDefined(travelClass)) return Result<CheckedRequest>.Failure("Unknown travel class.");
        return state.FindFlight(flightId) switch
        {
            Option<Flight>.Some { Value: var flight } when !flight.IsBookable => Result<CheckedRequest>.Failure("Flight is not open for booking."),
            Option<Flight>.Some { Value: var flight } when flight.Departure <= asOf => Result<CheckedRequest>.Failure("Booking must be before the departure date."),
            Option<Flight>.Some { Value: var flight } => Result<CheckedRequest>.Success(new(state, flight, passengers, travelClass)),
            _ => Result<CheckedRequest>.Failure($"Flight '{flightId}' was not found.")
        };
    }

    private static Result<CheckedRequest> CheckAvailability(CheckedRequest request) =>
        request.State.AvailableSeats(request.Flight.Id) >= request.Passengers
            ? Result<CheckedRequest>.Success(request)
            : Result<CheckedRequest>.Failure($"Not enough seats on flight '{request.Flight.Id}'.");

    private static Result<ReservationQuote> CalculateQuote(CheckedRequest request, Func<Flight, int, TravelClass, decimal> policy)
    {
        try
        {
            var rawAmount = policy(request.Flight, request.Passengers, request.TravelClass);
            if (rawAmount < 0m || rawAmount > ReservationRules.MaximumTotal)
                return Result<ReservationQuote>.Failure("Pricing policy returned an amount outside the supported range.");
            var amount = decimal.Round(rawAmount, 2, MidpointRounding.AwayFromZero);
            return Result<ReservationQuote>.Success(new(request.Flight, request.Passengers, request.TravelClass, amount));
        }
        catch (OverflowException)
        {
            return Result<ReservationQuote>.Failure("Pricing calculation exceeded the supported amount.");
        }
    }

    public static Result<ReservationChange> Reserve(ReservationState state, string? id, string? flightId,
        int passengers, TravelClass travelClass, DateOnly asOf, Func<Flight, int, TravelClass, decimal> policy)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!ReservationRules.ValidId(id)) return Result<ReservationChange>.Failure("Reservation Id must have 1 to 40 visible characters.");
        if (state.FindReservation(id) is Option<Reservation>.Some)
            return Result<ReservationChange>.Failure($"Reservation Id '{id!.Trim()}' already exists.");
        return Quote(state, flightId, passengers, travelClass, asOf, policy)
            .Bind(quote => Reservation.Create(id, quote.Flight.Id, passengers, travelClass, quote.TotalUsd,
                ReservationStatus.Confirmed, asOf))
            .Bind(booking => ReservationState.Create(state.Flights, state.Reservations.Add(booking))
                .Map(updated => new ReservationChange(updated, booking, booking.TotalUsd)));
    }
}
