using System.Collections.Immutable;
using System.Globalization;


namespace Astra.Core;

// Nullable fields let us tell a missing value apart from false or 0.
public sealed record RawBooking(string? Id, Destination? Destination, int? Passengers,
    TravelClass? TravelClass, DateOnly? Departure, bool? IsCancelled);

// Every batch goes through Create before it can be used to calculate quotes.
public sealed class ValidatedBookingBatch
{
    public const int MaximumPassengers = 6;
    public const int MaximumIdLength = 40;

    public ImmutableArray<BookingRequest> Bookings { get; }

    private ValidatedBookingBatch(ImmutableArray<BookingRequest> bookings) => Bookings = bookings;

    private static bool IsVisible(char c) => !char.IsControl(c) && char.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    public static Result<ValidatedBookingBatch> Create(IEnumerable<RawBooking?>? rows)
    {
        if (rows is null)
            return Result<ValidatedBookingBatch>.Failure("Dataset is missing.");

        var snapshot = rows.ToArray();
        var bookings = ImmutableArray.CreateBuilder<BookingRequest>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < snapshot.Length; i++)
        {
            var row = snapshot[i];
            if (row is null || row.Id is null || row.Destination is null || row.Passengers is null ||
                row.TravelClass is null || row.Departure is null || row.IsCancelled is null)
                return Result<ValidatedBookingBatch>.Failure(
                    $"Record {i + 1}: Id, Destination, Passengers, TravelClass, Departure and IsCancelled are required.");

            var id = row.Id.Trim();
            string? error = null;
            if (id.Length is 0 or > MaximumIdLength || !row.Id.All(IsVisible))
                error = $"Id must contain 1 to {MaximumIdLength} visible characters";
            else if (!ids.Add(id))
                error = "Id must be unique after trimming (case-sensitive)";
            else if (!Enum.IsDefined(row.Destination.Value) || !Enum.IsDefined(row.TravelClass.Value))
                error = "unknown destination or travel class";
            else if (row.Passengers is < 1 or > MaximumPassengers)
                error = $"Passengers must be between 1 and {MaximumPassengers}";
            else if (row.Departure != BookingPipeline.GetMission(row.Destination.Value).Departure)
                error = "Departure must match the mission schedule";

            if (error is not null)
                return Result<ValidatedBookingBatch>.Failure($"Record {i + 1}: {error}.");

            bookings.Add(new BookingRequest(id, row.Destination.Value, row.Passengers.Value,
                row.TravelClass.Value, row.Departure.Value, row.IsCancelled.Value));
        }

        // We get here only if every row passed validation.
        return Result<ValidatedBookingBatch>.Success(new ValidatedBookingBatch(bookings.ToImmutable()));
    }
}
