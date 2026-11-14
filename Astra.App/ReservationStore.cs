using System.Text.Json;
using System.Text.Json.Serialization;
using Astra.Core;
using Astra.Core.Reservations;

namespace Astra.App;

public static class ReservationStore
{
    private sealed record FlightData(string? Id, Destination? Destination, DateOnly? Departure,
        int? Capacity, decimal? BaseFareUsd, bool? IsBookable);
    private sealed record ReservationData(string? Id, string? FlightId, int? Passengers,
        TravelClass? TravelClass, decimal? TotalUsd, ReservationStatus? Status,
        DateOnly? BookedOn, DateOnly? CancelledOn, decimal? RefundUsd);
    private sealed record Document(int? Version, FlightData?[]? Flights, ReservationData?[]? Reservations);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters =
        {
            new JsonStringEnumConverter<Destination>(JsonNamingPolicy.CamelCase, false),
            new JsonStringEnumConverter<TravelClass>(JsonNamingPolicy.CamelCase, false),
            new JsonStringEnumConverter<ReservationStatus>(JsonNamingPolicy.CamelCase, false)
        }
    };

    public static Result<ReservationState> Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return Result<ReservationState>.Failure($"Cannot read input '{path}': file not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result<ReservationState>.Failure($"Cannot read input '{path}': directory not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Result<ReservationState>.Failure($"Cannot read input '{path}': {ex.Message}");
        }
        return Parse(json).WithContext($"Input '{path}'");
    }

    public static Result<ReservationState> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<ReservationState>.Failure("State JSON is missing.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Options);
            if (document is null || document.Version != 1 || document.Flights is null || document.Reservations is null)
                return Result<ReservationState>.Failure("State requires Version 1, Flights and Reservations arrays.");
            var flights = new List<Flight>();
            for (var i = 0; i < document.Flights.Length; i++)
            {
                var row = document.Flights[i];
                if (row is null || row.Id is null || row.Destination is null || row.Departure is null ||
                    row.Capacity is null || row.BaseFareUsd is null || row.IsBookable is null)
                    return Result<ReservationState>.Failure($"Flight {i + 1}: all fields are required.");
                var result = Flight.Create(row.Id, row.Destination.Value, row.Departure.Value,
                    row.Capacity.Value, row.BaseFareUsd.Value, row.IsBookable.Value);
                if (!result.IsSuccess) return Result<ReservationState>.Failure($"Flight {i + 1}: {result.Error}");
                flights.Add(result.Value);
            }
            var reservations = new List<Reservation>();
            for (var i = 0; i < document.Reservations.Length; i++)
            {
                var row = document.Reservations[i];
                if (row is null || row.Id is null || row.FlightId is null || row.Passengers is null ||
                    row.TravelClass is null || row.TotalUsd is null || row.Status is null ||
                    row.BookedOn is null || row.RefundUsd is null)
                    return Result<ReservationState>.Failure($"Reservation {i + 1}: required fields are missing.");
                var result = Reservation.Create(row.Id, row.FlightId, row.Passengers.Value,
                    row.TravelClass.Value, row.TotalUsd.Value, row.Status.Value, row.BookedOn.Value,
                    row.CancelledOn, row.RefundUsd.Value);
                if (!result.IsSuccess) return Result<ReservationState>.Failure($"Reservation {i + 1}: {result.Error}");
                reservations.Add(result.Value);
            }
            return ReservationState.Create(flights, reservations);
        }
        catch (JsonException ex)
        {
            return Result<ReservationState>.Failure($"Invalid state JSON: {ex.Message}");
        }
    }

    public static string Serialize(ReservationState state) => JsonSerializer.Serialize(new Document(1,
        state.Flights.Select(f => new FlightData(f.Id, f.Destination, f.Departure, f.Capacity,
            f.BaseFareUsd, f.IsBookable)).ToArray(),
        state.Reservations.Select(b => new ReservationData(b.Id, b.FlightId, b.Passengers,
            b.TravelClass, b.TotalUsd, b.Status, b.BookedOn, b.CancelledOn, b.RefundUsd)).ToArray()), Options);

    public static Result<string> Save(string path, ReservationState state) =>
        ReportFiles.Export(path, [Serialize(state)]);

    public static Result<ReservationChange> Update(string path,
        Func<ReservationState, Result<ReservationChange>> operation) =>
        ReportFiles.ValidateDestination(path).Bind(fullPath =>
        {
            try
            {
                using var fileLock = new FileStream(fullPath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                return Load(fullPath).Bind(operation)
                    .Bind(change => Save(fullPath, change.State).Map(_ => change));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return Result<ReservationChange>.Failure($"Cannot update state '{path}': {ex.Message}");
            }
        });
}
